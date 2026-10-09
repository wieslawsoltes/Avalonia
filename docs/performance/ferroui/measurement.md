# Designs 08/09: native measurement and reproduction

The native harness uses actual Avalonia Skia/HarfBuzz with headless layout and five-column data-bound ListBox recycling. It has 24 scenarios: notifications, inheritance, template and reflection bindings, simple/compound selectors, shaping reuse/unique/one-pass/mixed/context/long cases, retained/recreated/unique native glyphs, default/wrapped text layout, resource graph hits/misses/mutation and scrolling. It is not GPU presentation or browser timing.

The benchmark project and all `tests/Avalonia.Benchmarks/FerroUi/*.cs` harness sources are copied unchanged to an exact baseline checkout; runtime implementation is not overlaid. Artifacts include refs, harness hashes, runtime/OS/architecture, every sample, current-thread allocations and preparation/clearing counts. Tracked source edits are rejected. Semantic/numeric/environment errors fail independently of timing policy.

Each Linux/macOS and runtime-mode job runs five same-head A/A calibration pairs, then five original-baseline/head pairs, alternating process order on the same host. `optimized-jit` disables tiered compilation. `runtime-default` removes known inherited tiering/PGO/ReadyToRun overrides and permits normal tier transitions; it does not guarantee steady state. App deployment settings are not changed.

A calibration-informed screen flags a case only when all candidate ratios exceed the worst symmetric A/A envelope plus a 3% margin. It validates revision/environment/scenario compatibility and requires five calibration pairs. This is a conservative heuristic, not a confidence interval; unflagged cases can still regress. Timing is report-only by default, matching the source design's caution about shared-host noise. An explicit `--fail-regressions` activates that gate in a controlled environment. Twelve Python tests validate report and screen behavior.

```sh
python3 -m unittest discover -s scripts/performance -p 'test_*.py' -v
python3 scripts/performance/compare-native.py --calibrate --pairs 5 --runtime-mode optimized-jit --output artifacts/calibration
python3 scripts/performance/compare-native.py --pairs 5 --runtime-mode optimized-jit --calibration-file artifacts/calibration/comparison.json --output artifacts/comparison
# Optional controlled-environment gate:
python3 scripts/performance/compare-native.py --pairs 5 --runtime-mode optimized-jit --calibration-file artifacts/calibration/comparison.json --fail-regressions --output artifacts/gated-comparison
```

Use the SDK selected by `global.json`, initialized submodules and a clean tracked source tree. Repeat with `runtime-default` using its own matching calibration. Calibration must be generated for the same head and host/runtime environment; do not reuse a different machine's numbers.

Compile-time counter evidence runs separately with `AvaloniaPerfCounters=true` and is never used as uninstrumented timing. See [counters-and-calibration.md](counters-and-calibration.md). Browser input, exact pixels, startup, idle, profiles and app-level AOT/size tradeoffs are separate in [browser-validation.md](browser-validation.md).
