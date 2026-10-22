#!/usr/bin/env python3
"""Alternate exact-base/head runs, with optional same-revision noise calibration.

Semantic/numeric errors fail. Timing is reported unless --fail-regressions is explicitly selected
with a compatible calibration produced on the same host/runtime mode and head revision.
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
    for prefix in ("DOTNET_", "COMPlus_"):
        for setting in ("TieredCompilation", "TieredPGO", "TC_QuickJit", "TC_QuickJitForLoops", "ReadyToRun"):
            env.pop(prefix + setting, None)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", AVALONIA_PERF_RUNTIME_MODE=mode)
    if mode == "optimized-jit": env["DOTNET_TieredCompilation"] = "0"
    return env


def validate_payload(payload: dict, label: str, mode: str) -> dict[str, dict]:
    if not isinstance(payload, dict): raise ValueError(f"{label}: expected an object")
    if payload.get("schema") != 1 or payload.get("runtimeMode") != mode:
        raise ValueError(f"{label}: incompatible schema or runtime mode")
    for field in ("runtime", "os", "architecture", "backend"):
        if not isinstance(payload.get(field), str) or not payload[field]:
            raise ValueError(f"{label}: missing environment field {field}")
    if type(payload.get("processorCount")) is not int or payload["processorCount"] < 1:
        raise ValueError(f"{label}: invalid processor count")
    results = payload.get("results")
    if not isinstance(results, list) or not results: raise ValueError(f"{label}: no scenario results")
    indexed = {}
    for row in results:
        if not isinstance(row, dict): raise ValueError(f"{label}: expected a scenario object")
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
              mode: str = "optimized-jit") -> dict:
    if pairs < 1: raise ValueError("At least one pair is required")
    baselines = [json.loads((output / f"base-{i}.json").read_text(encoding="utf-8")) for i in range(pairs)]
    heads = [json.loads((output / f"head-{i}.json").read_text(encoding="utf-8")) for i in range(pairs)]
    grouped: dict[str, list[tuple[dict, dict]]] = {}
    environment = scenarios = None
    for i, (base, head) in enumerate(zip(baselines, heads)):
        bs = validate_payload(base, f"base-{i}", mode)
        hs = validate_payload(head, f"head-{i}", mode)
        for payload in (base, head):
            current = {key: payload[key] for key in ENVIRONMENT_FIELDS}
            if environment is None: environment = current
            elif current != environment: raise ValueError(f"Environment changed in pair {i}")
        if scenarios is None: scenarios = bs.keys()
        if bs.keys() != scenarios or hs.keys() != scenarios: raise ValueError(f"Scenario set changed in pair {i}")
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
            "paired_time_ratio_min": min(ratios), "paired_time_ratio_max": max(ratios),
            "paired_time_ratios": ratios,
            "base_bytes_per_op": statistics.median(b["AllocatedBytes"] / b["Operations"] for b, _ in samples),
            "head_bytes_per_op": statistics.median(h["AllocatedBytes"] / h["Operations"] for _, h in samples),
            "prepared_rows_per_run": samples[0][1]["PreparedRows"], "cleared_rows_per_run": samples[0][1]["ClearedRows"]})
    report = {"base": base_sha, "head": head_sha, "pairs": pairs, "environment": environment,
              "runtime_mode": mode, "timing_policy": "report-only", "calibration": base_sha == head_sha, "results": rows}
    (output / "comparison.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    lines = ["# Paired native performance report", "", f"Base: `{base_sha}`. Head: `{head_sha}`.",
             f"{pairs} fresh-process pairs, alternating order. Identical harness sources; Release; mode: {mode}.",
             "Same-revision A/A calibration." if base_sha == head_sha else "Baseline/head comparison.",
             "Real Skia/HarfBuzz and headless layout; no GPU presentation, browser FPS or cold-start claim.",
             "Checksums, recycling counts and environment agree. Managed allocation is current-thread only.", "",
             "| Scenario | Base ns/op | Head ns/op | Paired time change | Base B/op | Head B/op |", "|---|---:|---:|---:|---:|---:|"]
    for row in rows:
        lines.append(f"| {row['scenario']} | {row['base_ns_per_op']:.1f} | {row['head_ns_per_op']:.1f} | "
                     f"{row['paired_time_delta_percent']:+.1f}% | {row['base_bytes_per_op']:.1f} | {row['head_bytes_per_op']:.1f} |")
    lines += ["", "Negative is faster. Positive is slower. Report-only unless an explicit calibrated gate is requested.",
              "Default-runtime tier transitions and shared-host variation may occur; raw pairs are retained."]
    (output / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return report


def calibrated_screen(report: dict, calibration: dict, margin: float = 0.03) -> list[dict]:
    if not 0 <= margin <= 1: raise ValueError("Invalid calibration margin")
    if calibration.get("calibration") is not True or calibration.get("base") != calibration.get("head"):
        raise ValueError("Expected same-revision calibration")
    if calibration.get("head") != report.get("head") or calibration.get("pairs", 0) < 5:
        raise ValueError("Calibration must use this head and at least five pairs")
    if calibration.get("environment") != report.get("environment") or calibration.get("runtime_mode") != report.get("runtime_mode"):
        raise ValueError("Calibration environment/runtime mode does not match")
    noise = {row["scenario"]: row for row in calibration["results"]}
    if noise.keys() != {row["scenario"] for row in report["results"]}: raise ValueError("Calibration scenarios differ")
    result = []
    for row in report["results"]:
        sample = noise[row["scenario"]]
        lo, hi = sample["paired_time_ratio_min"], sample["paired_time_ratio_max"]
        candidate = row["paired_time_ratio_min"]
        if not all(type(x) in (int, float) and math.isfinite(x) and x > 0 for x in (lo, hi, candidate)) or lo > hi:
            raise ValueError("Invalid calibration ratios")
        threshold = max(1.0, hi, 1.0 / lo) * (1 + margin)
        result.append({"scenario": row["scenario"], "noise_envelope_upper": threshold,
                       "fastest_candidate_ratio": candidate, "flagged": candidate > threshold})
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="a9429a328057befa287ffb5e981f58b86a86eda0")
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--runtime-mode", choices=MODES, default="optimized-jit")
    parser.add_argument("--calibrate", action="store_true")
    parser.add_argument("--calibration-file", type=Path)
    parser.add_argument("--fail-regressions", action="store_true")
    parser.add_argument("--output", type=Path, default=Path("artifacts/ferroui-performance"))
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.base) or not 3 <= args.pairs <= 20:
        parser.error("Use an exact base SHA and 3 through 20 pairs")
    if args.fail_regressions and not args.calibration_file: parser.error("A calibrated gate requires --calibration-file")
    root = Path(__file__).resolve().parents[2]
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    for name in ("comparison.json", "comparison.md", "calibrated-screen.json"):
        (output / name).unlink(missing_ok=True)
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    base_sha = head if args.calibrate else args.base
    env = measurement_environment(args.runtime_mode)
    project = "tests/Avalonia.Benchmarks/Avalonia.Benchmarks.csproj"
    sources = [project] + [str(p.relative_to(root)) for p in sorted((root / "tests/Avalonia.Benchmarks/FerroUi").glob("*.cs"))]
    run(["git", "diff", "--exit-code", "HEAD", "--", "src", "tests", "build", "global.json",
         "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"], root)
    provenance = {"head": head, "base": base_sha, "runtime_mode": args.runtime_mode,
                  "harness_sha256": {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sources}}
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    remote = subprocess.check_output(["git", "remote", "get-url", "origin"], cwd=root, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="avalonia-ferroui-") as temporary:
        base = Path(temporary) / "base"; base.mkdir()
        run(["git", "init", "--quiet"], base)
        run(["git", "remote", "add", "origin", remote], base)
        run(["git", "fetch", "--depth=1", "origin", base_sha], base, output / "base-checkout.log")
        run(["git", "checkout", "--detach", base_sha], base)
        run(["git", "submodule", "update", "--init", "--recursive"], base, output / "base-submodules.log")
        for relative in sources:
            target = base / relative; target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(root / relative, target)
        for label, checkout in (("base", base), ("head", root)):
            run(["dotnet", "build", project, "-c", "Release", "-p:FerroUiPerformanceHarness=true",
                 "-p:AvsSkipBuildingLegacyTargetFrameworks=True", "-p:AvaloniaPerfCounters=false"], checkout, output / f"{label}-build.log", env)
        for i in range(args.pairs):
            order = (("base", base), ("head", root)) if i % 2 == 0 else (("head", root), ("base", base))
            for label, checkout in order:
                run(["dotnet", "run", "--no-build", "--no-restore", "--project", project, "-c", "Release",
                     "-p:FerroUiPerformanceHarness=true", "-p:AvsSkipBuildingLegacyTargetFrameworks=True", "-p:AvaloniaPerfCounters=false", "--",
                     str(output / f"{label}-{i}.json")], checkout, output / f"{label}-{i}.log", env)
        report = summarize(output, args.pairs, base_sha, head, args.runtime_mode)
    if args.calibration_file:
        calibration = json.loads(args.calibration_file.read_text(encoding="utf-8"))
        screening = calibrated_screen(report, calibration)
        (output / "calibrated-screen.json").write_text(json.dumps(screening, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        flagged = [row["scenario"] for row in screening if row["flagged"]]
        message = "\nCalibration-informed screen (not a statistical confidence interval): " + (", ".join(flagged) if flagged else "no consistent regressions outside the observed envelope") + ".\n"
        with (output / "comparison.md").open("a", encoding="utf-8") as stream: stream.write(message)
        print(message)
        if args.fail_regressions and flagged: raise SystemExit(1)


if __name__ == "__main__":
    main()
