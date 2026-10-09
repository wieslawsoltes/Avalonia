# Designs 08 and 09: reproducible managed measurements

## Scope

The native harness exercises Avalonia with real Skia/HarfBuzz and headless layout, including five-column data-bound ListBox recycling. It does not measure rendered GPU frames, browser scrolling, input-to-display latency, browser download size or cold startup. Its small checksum validates scenario work counts; exact glyph equivalence, lifetime and notification semantics are covered separately by unit tests.

The same benchmark project and single harness source are compiled against the exact original baseline and current commit, without copying runtime implementation changes. Five fresh-process pairs alternate execution order. Artifacts record exact refs, harness SHA-256 hashes, runtime/OS/architecture, raw elapsed time, managed current-thread allocations and container prepare/clear counts. Tracked runtime/build/test modifications are rejected rather than silently attributing dirty source to a commit.

## Runtime and platform matrix

The fork-only workflow runs Linux and macOS in two modes:

- `optimized-jit`: disables tiered compilation, preserving the original measurement setup.
- `runtime-default`: removes known inherited tiering, PGO and ReadyToRun environment overrides and uses the SDK/runtime's normal configuration. Tier transitions may occur during warm-up and measurement; this is not a claim of stabilized steady-state performance.

These are measurement-process settings, not changes to Avalonia libraries or application deployment policy. Rust Cargo/emscripten optimization flags cannot be copied to .NET. Browser AOT/size/startup tradeoffs still need separate browser evidence before deployment defaults are changed.

## Counterexamples as well as cache hits

The harness retains the original repeated-short-run and over-capacity working-set scans. It adds a genuinely one-pass short-string scan, mixed hot labels with one-off strings, and long uncacheable text. Strings are constructed outside the measured operation; one-pass inputs never wrap during warm-up or measurement. Binding construction is measured both with a reused description and with a fresh description each time, exposing the owned syntax snapshot's first-use cost.

## Validation and reproduction

The report generator rejects missing/duplicate scenarios, inconsistent scenario sets across pairs, invalid or non-finite timing/allocation data, environment drift and changes in operation/checksum/recycling outputs. It removes stale summaries before a run so a failed rerun cannot upload an old success report. Timing remains report-only until runner noise is calibrated. Eight Python unit tests exercise the report and environment validation without requiring .NET.

```sh
python3 -m unittest discover -s scripts/performance -p 'test_*.py' -v
python3 scripts/performance/compare-native.py --pairs 5 --runtime-mode optimized-jit --output artifacts/ferroui-jit
python3 scripts/performance/compare-native.py --pairs 5 --runtime-mode runtime-default --output artifacts/ferroui-default
```

Use the SDK in `global.json`, initialized submodules and a clean tracked working tree. Compare within the same platform/runtime-mode job; do not interpret differences between independent machines as optimization effects. Native counters behind a zero-overhead build feature, real browser traces and browser build-size budgets are not implemented by this harness.
