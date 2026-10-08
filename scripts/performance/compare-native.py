#!/usr/bin/env python3
"""Alternate exact-base/head native runs with identical measurement sources.

Timings are report-only. Runtime failures or mismatched scenario outputs fail the run.
Requires git, Python 3 and the SDK from global.json. No runtime source edits or pushes; uses an isolated baseline checkout.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import tempfile


def run(command: list[str], cwd: Path, log: Path | None = None, env: dict[str, str] | None = None) -> None:
    if log is None:
        subprocess.run(command, cwd=cwd, env=env, check=True)
        return
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        print(log.read_text(encoding="utf-8", errors="replace")[-16000:])
        raise RuntimeError(f"Command failed ({result.returncode}); see {log}")


def summarize(output: Path, pairs: int, base_sha: str, head_sha: str) -> None:
    baselines = [json.loads((output / f"base-{i}.json").read_text()) for i in range(pairs)]
    heads = [json.loads((output / f"head-{i}.json").read_text()) for i in range(pairs)]
    grouped: dict[str, list[tuple[dict, dict]]] = {}
    for base, head in zip(baselines, heads):
        bs = {row["Scenario"]: row for row in base["results"]}
        hs = {row["Scenario"]: row for row in head["results"]}
        if bs.keys() != hs.keys():
            raise RuntimeError("Baseline and head have different scenarios")
        for name, before in bs.items():
            after = hs[name]
            for field in ("Operations", "Checksum", "PreparedRows", "ClearedRows"):
                if before[field] != after[field]:
                    raise RuntimeError(f"Logical output differs: {name}.{field}: {before[field]} != {after[field]}")
            grouped.setdefault(name, []).append((before, after))
    rows = []
    for name, samples in grouped.items():
        ratios = [h["Nanoseconds"] / b["Nanoseconds"] for b, h in samples]
        rows.append({
            "scenario": name,
            "base_ns_per_op": statistics.median(b["Nanoseconds"] / b["Operations"] for b, _ in samples),
            "head_ns_per_op": statistics.median(h["Nanoseconds"] / h["Operations"] for _, h in samples),
            "paired_time_delta_percent": (statistics.median(ratios) - 1) * 100,
            "paired_time_ratio_min": min(ratios),
            "paired_time_ratio_max": max(ratios),
            "base_bytes_per_op": statistics.median(b["AllocatedBytes"] / b["Operations"] for b, _ in samples),
            "head_bytes_per_op": statistics.median(h["AllocatedBytes"] / h["Operations"] for _, h in samples),
            "prepared_rows_per_run": samples[0][1]["PreparedRows"],
            "cleared_rows_per_run": samples[0][1]["ClearedRows"],
        })
    report = {"base": base_sha, "head": head_sha, "pairs": pairs,
              "environment": {k: v for k, v in heads[0].items() if k != "results"},
              "tiered_compilation": False, "timing_policy": "report-only", "results": rows}
    (output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n")
    lines = ["# Paired native performance report", "", f"Base: `{base_sha}`. Head: `{head_sha}`.",
             f"{pairs} fresh-process pairs, alternating order. Identical harness sources; Release; tiered compilation disabled.",
             "Real Skia/HarfBuzz and headless layout; no GPU presentation, browser FPS or cold-start claim.",
             "Checksums and recycling counts agree for each pair. Managed allocation is current-thread only.", "",
             "| Scenario | Base ns/op | Head ns/op | Paired time change | Base B/op | Head B/op |", "|---|---:|---:|---:|---:|---:|"]
    for row in rows:
        lines.append(f"| {row['scenario']} | {row['base_ns_per_op']:.1f} | {row['head_ns_per_op']:.1f} | "
                     f"{row['paired_time_delta_percent']:+.1f}% | {row['base_bytes_per_op']:.1f} | {row['head_bytes_per_op']:.1f} |")
    lines += ["", "Negative time change is faster. Positive is slower. Shared-runner timing noise is not a hard gate.",
              "Raw JSON includes paired ranges, runtime/OS, operations, checksums and prepare/clear counts.",
              "Microbenchmarks isolate costs; ListBox scenarios include real five-column data binding and container recycling."]
    (output / "comparison.md").write_text("\n".join(lines) + "\n")
    print("\n".join(lines))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="a9429a328057befa287ffb5e981f58b86a86eda0")
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--output", type=Path, default=Path("artifacts/ferroui-performance"))
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.base) or not 3 <= args.pairs <= 20:
        parser.error("Use an exact 40-character base SHA and 3 through 20 pairs")
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    head_sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_TieredCompilation="0")
    project = "tests/Avalonia.Benchmarks/Avalonia.Benchmarks.csproj"
    harness = "tests/Avalonia.Benchmarks/FerroUi/PerformanceProgram.cs"
    remote = subprocess.check_output(["git", "remote", "get-url", "origin"], cwd=root, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="avalonia-ferroui-") as temporary:
        base = Path(temporary) / "base"
        base.mkdir()
        run(["git", "init", "--quiet"], base)
        run(["git", "remote", "add", "origin", remote], base)
        # A shared clone of a shallow Actions checkout need not include an unreferenced base.
        # Fetch the exact base into its own repository instead of depending on alternate objects.
        run(["git", "fetch", "--depth=1", "origin", args.base], base, output / "base-checkout.log")
        run(["git", "checkout", "--detach", args.base], base)
        try:
            run(["git", "submodule", "update", "--init", "--recursive"], base, output / "base-submodules.log")
            for relative in (project, harness):
                target = base / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(root / relative, target)
            for label, checkout in (("base", base), ("head", root)):
                run(["dotnet", "build", project, "-c", "Release", "-p:FerroUiPerformanceHarness=true",
                     "-p:AvsSkipBuildingLegacyTargetFrameworks=True"], checkout, output / f"{label}-build.log", env)
            for i in range(args.pairs):
                order = (("base", base), ("head", root)) if i % 2 == 0 else (("head", root), ("base", base))
                for label, checkout in order:
                    run(["dotnet", "run", "--no-build", "--no-restore", "--project", project, "-c", "Release",
                         "-p:FerroUiPerformanceHarness=true", "-p:AvsSkipBuildingLegacyTargetFrameworks=True", "--",
                         str(output / f"{label}-{i}.json")], checkout, output / f"{label}-{i}.log", env)
            summarize(output, args.pairs, args.base, head_sha)
        finally:
            shutil.rmtree(base)


if __name__ == "__main__":
    main()
