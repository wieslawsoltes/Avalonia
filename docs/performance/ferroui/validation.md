# Validation and measured results — 2026-10-09

## Exact revisions and scope

Baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`.
Measured runtime, tests and harness: `95a0ddcb129a2586ffe7659c54314b41178fd2a2`.
The documentation/temporary-toolchain cleanup commit that adds this report does not change that runtime or harness.

[Unit/library validation run 37890213588](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213588) completed successfully for all six Release suites: Avalonia.Base.UnitTests, Avalonia.Controls.UnitTests, Avalonia.Markup.UnitTests, Avalonia.Markup.Xaml.UnitTests, Avalonia.Skia.UnitTests and Avalonia.Themes.UnitTests. The affected .NET 8 library-build job also passed. This is not a claim that every platform, render/integration suite or repository project has been tested.

[Native measurement run 37890213606](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213606) completed successfully in all four Linux/macOS and runtime-mode combinations. Each job ran the eight Python report-validation tests and five fresh-process baseline/head pairs in alternating order. All measured scenario operation counts, checksums and container prepare/clear counts agreed, and the report validator found no environment drift within a comparison. Timing is report-only: a successful job does not mean every scenario improved.

## Paired elapsed-time results

The table preserves the paired median percentage reported by each job: `100 * (median(head_time / base_time) - 1)`. Negative is faster; positive is slower. This is not the percentage computed from the two independent median ns/op values, which can even have a different direction when the pairs vary. The raw artifacts retain both those medians and each pair's data/range. These are shared-runner observations, not confidence intervals or universal performance guarantees.

`optimized-jit` disables tiered compilation. `runtime-default` removes known inherited tiering/PGO/ReadyToRun environment overrides; tier transitions may still happen during warm-up and measurement. Neither mode changes application deployment defaults. The jobs used .NET 10.0.12 with SDK 10.0.401; the macOS jobs ran arm64. Compare baseline versus head within a column, not absolute performance between the different hosts.

| Scenario | Linux optimized JIT | Linux runtime default | macOS optimized JIT | macOS runtime default |
| --- | ---: | ---: | ---: | ---: |
| property-no-listener | -0.9% | +0.9% | -9.2% | -4.0% |
| property-inpc | -12.8% | -2.2% | +4.8% | +1.1% |
| inheritance-reparent | -6.6% | -2.4% | +12.8% | +2.8% |
| template-identity | -6.2% | -2.1% | +19.4% | -2.8% |
| reflection-description-reused | -24.3% | -20.2% | -10.0% | +1.0% |
| reflection-description-once | -2.4% | -9.9% | -17.7% | -15.4% |
| style-type-miss | -14.9% | -4.2% | -66.5% | -35.2% |
| shape-repeated-short | -69.3% | -50.6% | -78.1% | -61.1% |
| shape-unique-short | +4.8% | +7.0% | -5.4% | -0.6% |
| shape-one-pass-short | +6.5% | +9.4% | -12.1% | -6.5% |
| shape-hot-with-one-off-scan | -47.0% | -31.4% | -60.8% | -38.5% |
| shape-long-uncacheable | +0.1% | +1.3% | +5.7% | +4.4% |
| shape-context-slice | +2.3% | +6.2% | -1.5% | +16.5% |
| skia-retained-text-blob | -27.9% | -5.6% | -44.8% | -13.3% |
| text-layout-short | -17.1% | -9.7% | -45.2% | +25.4% |
| listbox-wheel-offset | -4.3% | +0.3% | +11.9% | +0.3% |
| listbox-viewport-jump | -5.5% | -8.7% | +3.5% | -38.6% |

Job logs: [Linux optimized](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213606/job/113689302839), [Linux default](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213606/job/113689302798), [macOS optimized](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213606/job/113689302655), [macOS default](https://github.com/wieslawsoltes/Avalonia/actions/runs/37890213606/job/113689302827).

## Allocation evidence and regression-driven revisions

Managed current-thread bytes per operation consistently fell from 88 to 64 for INPC notification, 184 to 64 in the reparent/no-op scenario, 216 to 192 for same-type template publication, 1,777 to 1,649 for reused reflection descriptions, 1,913 to 1,793 for fresh descriptions with recurring paths, and 96 to zero for retained Skia text-blob lookups. These measurements do not count all native allocations or establish exact retained-heap size.

The original eagerly populated shaping cache at `a59460a82e5e74b92317addb7b3df92c76c5b8fc` regressed the over-capacity short-string scan by 23.5%, with 2,183.3 rather than 336.0 bytes per operation in [run 37847755233](https://github.com/wieslawsoltes/Avalonia/actions/runs/37847755233). Two-touch admission now leaves the genuinely one-pass scenario at 336 bytes per operation, equal to baseline. The repeated over-capacity scan still occasionally admits entries and reports 343.5–345.7 rather than 336 bytes per operation across the four jobs. Its Linux elapsed-time overhead also remains; it has not been declared fixed merely because the large allocation regression was removed.

The first per-description syntax-cache revision added 128 bytes to fresh binding descriptions in [run 37889587051](https://github.com/wieslawsoltes/Avalonia/actions/runs/37889587051). Bounded shared pure-syntax snapshots changed that case from an allocation regression to a 120-byte saving versus baseline for the recurring-path scenario. Truly unique paths still need parsing and snapshot ownership; this benchmark does not prove they are cheaper.

## Interpretation and remaining acceptance work

Repeated-run shaping and retained-blob lookup improved in all four comparisons. Binding allocation reductions are consistent. Overall scrolling is not uniformly faster: macOS non-tiered wheel-offset work regressed 11.9%, and some other scenarios also regressed, including macOS default short text layout (+25.4%) and context slices (+16.5%). Those outcomes are recorded rather than dismissed as noise. Controlled reruns, profiling and cache-miss/runtime-mode analysis are needed to distinguish timing variation from actual regressions before accepting a broad performance claim.

The work remains a draft. The [design matrix](README.md) tracks missing per-host theme/resource caching, a fully typed binding route, line-metrics/native-glyph sharing, browser wheel/drag and idle profiling, browser build-size/AOT tradeoffs, screenshot/render validation and optional instrumentation. Object-major inheritance batching remains rejected because it changes observable order; Rust virtual forwarding and Cargo flags do not apply to C#.

These tests use real Skia/HarfBuzz with headless layout and a null renderer for the list scenarios. They do not measure GPU presentation, browser frame rate, input latency or cold startup. Matching checksums/recycling counts supplement, but do not replace, semantic and visual regression tests. No 60 FPS, universal speedup, full design completion or merge-readiness claim follows from this run.

For reproduction, see [measurement.md](measurement.md). Workflow artifacts have 14-day retention; this checked-in report preserves the observations and exact revision/run references after those artifacts expire.
