#!/usr/bin/env python3
"""Alternate exact-base/head native runs with identical measurement sources.

Timings are report-only. Invalid measurements or mismatched scenario outputs fail.
Requires git, Python 3 and the SDK from global.json. Uses an isolated baseline checkout.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import tempfile

MODES = ("optimized-jit", "runtime-default")
ENVIRONMENT_FIELDS = ("schema", "runtime", "os", "architecture", "processorCount", "backend", "runtimeMode")


def run(command: list[str], cwd: Path, log: Path | None = None, env: dict[str, str] | None = None) -> None:
    if log is None:
        subprocess.run(command, cwd=cwd, env=env, check=True)
        return
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        print(log.read_text(encoding="utf-8", errors="replace")[-16000:])
        raise RuntimeError(f"Command failed ({result.returncode}); see {log}")


def measurement_environment(mode: str) -> dict[str, str]:
    if mode not in MODES:
        raise ValueError(f"Unknown runtime mode: {mode}")
    env = dict(os.environ)
    # Do not accidentally inherit a developer's JIT/PGO/ReadyToRun overrides into one mode.
    for prefix in ("DOTNET_", "COMPlus_"):
        for setting in ("TieredCompilation", "TieredPGO", "TC_QuickJit", "TC_QuickJitForLoops", "ReadyToRun"):
            env.pop(prefix + setting, None)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", AVALONIA_PERF_RUNTIME_MODE=mode)
    if mode == "optimized-jit":
        env["DOTNET_TieredCompilation"] = "0"
    return env


def validate_payload(payload: dict, label: str, mode: str) -> dict[str, dict]:
    if not isinstance(payload, dict):
        raise ValueError(f"{label}: expected an object")
    if payload.get("schema") != 1 or payload.get("runtimeMode") != mode:
        raise ValueError(f"{label}: incompatible schema or runtime mode")
    for field in ("runtime", "os", "architecture", "backend"):
        if not isinstance(payload.get(field), str) or not payload[field]:
            raise ValueError(f"{label}: missing environment field {field}")
    if type(payload.get("processorCount")) is not int or payload["processorCount"] < 1:
        raise ValueError(f"{label}: invalid processor count")
    results = payload.get("results")
    if not isinstance(results, list) or not results:
        raise ValueError(f"{label}: no scenario results")
    indexed = {}
    for row in results:
        if not isinstance(row, dict):
            raise ValueError(f"{label}: expected a scenario object")
        name = row.get("Scenario")
        if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9._-]+", name) or name in indexed:
            raise ValueError(f"{label}: invalid or duplicate scenario {name!r}")
        for field in ("Operations", "AllocatedBytes", "Checksum", "PreparedRows", "ClearedRows"):
            value = row.get(field)
            minimum = 1 if field == "Operations" else 0
            if type(value) is not int or (field != "Checksum" and value < minimum):
                raise ValueError(f"{label}: invalid {name}.{field}")
        elapsed = row.get("Nanoseconds")
        if type(elapsed) not in (int, float) or not math.isfinite(elapsed) or elapsed <= 0:
            raise ValueError(f"{label}: invalid {name}.Nanoseconds")
        indexed[name] = row
    return indexed


def summarize(output: Path, pairs: int, base_sha: str, head_sha: str,
              mode: str = "optimized-jit") -> None:
    if pairs < 1:
        raise ValueError("At least one pair is required")
    baselines = [json.loads((output / f"base-{i}.json").read_text(encoding="utf-8")) for i in range(pairs)]
    heads = [json.loads((output / f"head-{i}.json").read_text(encoding="utf-8")) for i in range(pairs)]
    grouped: dict[str, list[tuple[dict, dict]]] = {}
    environment = None
    scenarios = None
    for i, (base, head) in enumerate(zip(baselines, heads)):
        bs = validate_payload(base, f"base-{i}", mode)
        hs = validate_payload(head, f"head-{i}", mode)
        for payload in (base, head):
            current = {key: payload[key] for key in ENVIRONMENT_FIELDS}
            if environment is None:
                environment = current
            elif current != environment:
                raise ValueError(f"Environment changed in pair {i}")
        if scenarios is None:
            scenarios = bs.keys()
        if bs.keys() != scenarios or hs.keys() != scenarios:
            raise ValueError(f"Scenario set changed in pair {i}")
        for name, before in bs.items():
            after = hs[name]
            for field in ("Operations", "Checksum", "PreparedRows", "ClearedRows"):
                if before[field] != after[field]:
                    raise ValueError(f"Logical output differs: {name}.{field}: {before[field]} != {after[field]}")
            grouped.setdefault(name, []).append((before, after))
    rows = []
    for name, samples in grouped.items():
        ratios = [h["Nanoseconds"] / b["Nanoseconds"] for b, h in samples]
        if not all(math.isfinite(ratio) and ratio > 0 for ratio in ratios):
            raise ValueError(f"Invalid timing ratio for {name}")
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
    report = {"base": base_sha, "head": head_sha, "pairs": pairs, "environment": environment,
              "runtime_mode": mode, "timing_policy": "report-only", "results": rows}
    (output / "comparison.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    lines = ["# Paired native performance report", "", f"Base: `{base_sha}`. Head: `{head_sha}`.",
             f"{pairs} fresh-process pairs, alternating order. Identical harness sources; Release; mode: {mode}.",
             "Real Skia/HarfBuzz and headless layout; no GPU presentation, browser FPS or cold-start claim.",
             "Checksums, recycling counts and runtime environment agree for all pairs. Managed allocation is current-thread only.", "",
             "| Scenario | Base ns/op | Head ns/op | Paired time change | Base B/op | Head B/op |", "|---|---:|---:|---:|---:|---:|"]
    for row in rows:
        lines.append(f"| {row['scenario']} | {row['base_ns_per_op']:.1f} | {row['head_ns_per_op']:.1f} | "
                     f"{row['paired_time_delta_percent']:+.1f}% | {row['base_bytes_per_op']:.1f} | {row['head_bytes_per_op']:.1f} |")
    lines += ["", "Negative time change is faster. Positive is slower. Shared-runner timing noise is not a hard gate.",
              "Raw JSON includes paired ranges, runtime/OS, operations, checksums and prepare/clear counts.",
              "One-pass and mixed text scans expose cache admission costs; binding cases compare one-use and reused descriptions.",
              "The runtime-default mode permits tiering and PGO transitions during warm-up/measurement; it is not a steady-state claim."]
    (output / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="a9429a328057befa287ffb5e981f58b86a86eda0")
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--runtime-mode", choices=MODES, default="optimized-jit")
    parser.add_argument("--output", type=Path, default=Path("artifacts/ferroui-performance"))
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.base) or not 3 <= args.pairs <= 20:
        parser.error("Use an exact 40-character base SHA and 3 through 20 pairs")
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # A failed rerun must not publish a previous run's successful summary.
    for name in ("comparison.json", "comparison.md"):
        (output / name).unlink(missing_ok=True)
    head_sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    env = measurement_environment(args.runtime_mode)
    project = "tests/Avalonia.Benchmarks/Avalonia.Benchmarks.csproj"
    harness = "tests/Avalonia.Benchmarks/FerroUi/PerformanceProgram.cs"
    run(["git", "diff", "--exit-code", "HEAD", "--", "src", "tests", "build", "global.json",
         "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"], root)
    provenance = {"head": head_sha, "base": args.base, "runtime_mode": args.runtime_mode,
                  "harness_sha256": {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in (project, harness)}}
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    remote = subprocess.check_output(["git", "remote", "get-url", "origin"], cwd=root, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="avalonia-ferroui-") as temporary:
        base = Path(temporary) / "base"
        base.mkdir()
        run(["git", "init", "--quiet"], base)
        run(["git", "remote", "add", "origin", remote], base)
        run(["git", "fetch", "--depth=1", "origin", args.base], base, output / "base-checkout.log")
        run(["git", "checkout", "--detach", args.base], base)
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
        summarize(output, args.pairs, args.base, head_sha, args.runtime_mode)


if __name__ == "__main__":
    main()
