# Cold-path continuation: validation and measurements, 2026-10-09

## Revisions and scope

Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`.

Runtime and tests: **`e8c4cb23d1f023fb0bd907ca01a23e7f4dc7af01`**. The following commit `43b2ec2de3a417b2521445b157cb63c067f5b400` only updates the documentation index and source-applicability explanation. The commit adding this report likewise changes no runtime, tests, harness or build settings.

The two runtime commits in this continuation are `c485640` ([raw native blob views](raw-native-blob-buffer.md)) and `e8c4cb2` ([ineligible shaping bypass](uncacheable-shaping.md)). They add nine test cases. The existing 26-scenario benchmark sources are unchanged. The comparisons below measure the **entire branch versus the original baseline**, not the isolated contribution of these two commits.

## Completed correctness validation

At `e8c4cb2`, [run 37920257531](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257531) passed all six configured Release suites: Base, Controls, Markup, XAML, Skia and Themes. The affected .NET 8 library builds passed as well. These suites include the new raw-buffer allocation/raster tests and the ineligible-memory/cache-policy tests; existing skips and unrelated platform/integration suites are not being represented as executed tests.

[Counter run 37920257708](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257708) passed the normal/instrumented build checks, counter-absence tests and the real-workload instrumentation checks at the same runtime revision.

[Native run 37920257553](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257553) completed successfully in all four OS/runtime-mode combinations. Every job ran the 12 Python report-validation tests and five alternating fresh-process pairs for both same-revision calibration and baseline/head comparison. Operations, checksums, preparation/clearing counts and reported environments agreed within comparisons. Timing remains report-only: successful execution is not performance acceptance.

## Native paired timing results

Negative means faster; positive means slower. Each entry is `100 * (median(head_time / base_time) - 1)` across the five process pairs. It is not calculated from the two independent median ns/op values; those can have a different direction. Raw artifacts and logs retain both forms. Optimized JIT means tiered compilation is disabled; runtime-default removes known inherited tiering/PGO/ReadyToRun overrides but does not prevent tier transitions during the workload.

| Scenario | Linux optimized JIT | Linux default | macOS optimized JIT | macOS default |
|---|---:|---:|---:|---:|
| property-no-listener | -6.6% | +1.3% | +1.7% | +39.5% |
| property-inpc | -18.0% | -1.9% | -12.1% | +22.0% |
| inheritance-reparent | -11.7% | -4.3% | -8.5% | +15.2% |
| template-identity | -33.4% | -11.4% | -23.3% | +0.1% |
| reflection-description-reused | -23.7% | -3.9% | -13.8% | +4.9% |
| reflection-description-once | -4.1% | +5.9% | -31.3% | -12.1% |
| style-type-miss | -6.0% | +21.0% | -67.7% | -44.6% |
| shape-repeated-short | -63.0% | -39.6% | -79.9% | -59.1% |
| shape-unique-short | +2.7% | +5.2% | -6.1% | +40.1% |
| shape-one-pass-short | +3.1% | +4.7% | -8.9% | -14.2% |
| shape-hot-with-one-off-scan | -43.1% | -22.4% | -58.6% | -45.7% |
| shape-long-uncacheable | +0.1% | -0.1% | -24.1% | +12.5% |
| shape-context-slice | +1.1% | +4.1% | -2.3% | +14.8% |
| skia-retained-text-blob | -28.7% | -21.6% | -46.3% | +1.1% |
| text-layout-short | -41.5% | -14.7% | -59.7% | +12.1% |
| listbox-wheel-offset | -5.1% | -2.7% | -32.9% | +20.0% |
| listbox-viewport-jump | -6.1% | +0.7% | -14.9% | +13.1% |
| resource-deep-hit | -95.9% | -53.4% | -96.9% | -46.0% |
| resource-deep-miss | -98.3% | -97.9% | -98.7% | -98.0% |
| resource-deep-mutation | -92.4% | -88.8% | -94.5% | -84.8% |
| resource-local-replacement | -1.7% | +6.6% | -6.1% | +19.3% |
| resource-deep-insert-remove | +8.0% | +53.5% | +1.0% | +218.4% |
| selector-compound-live | -10.5% | -19.0% | -12.2% | -16.2% |
| native-glyph-recreated | -70.5% | -78.3% | -75.2% | -75.3% |
| native-glyph-unique | +3.7% | +1.2% | -0.2% | +101.4% |
| text-layout-wrap-fallback | -21.9% | -24.9% | -42.5% | -27.0% |

Exact logs: [Linux optimized](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257553/job/113786463250), [Linux default](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257553/job/113786463465), [macOS optimized](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257553/job/113786463957), [macOS default](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257553/job/113786463566).

These jobs used .NET 10.0.12 / SDK 10.0.401. Linux was Ubuntu 24.04.5; macOS was 26.6.2 on arm64. Compare baseline and head within a column rather than absolute ns/op across machines or modes.

## Allocation evidence and limits

Across these jobs, INPC notifications were 88 -> 64 B/op; the reparent scenario 184 -> 64; same-type template binding 216 -> 128; reused reflection descriptions 1,777 -> 1,649; fresh descriptions with recurring syntax 1,913 -> 1,793. Retained Skia blob lookups were 96 -> 0 B/op. Ordinary resource replacements and the insertion/removal scenario remained 24 -> 24 B/op.

Shaping remains **336 -> 352 B/op**, including ineligible runs. Moving eligibility before the adaptive policy does not remove the additional metrics metadata on returned buffers. Linux short-layout allocation was 2,241.2 -> 1,473.2 B/op and wrapped-layout allocation 10,890.4 -> 9,602.4. The corresponding macOS figures were 2,240 -> 1,472 and 7,568 -> 6,560.

The new isolated raw-buffer test passed its lower-bound allocation comparison against an independently implemented previous wrapper path. However, **the complete unique-native-glyph scenario still reports 624 B/op**, the same as the previous `64f2b10` checkpoint. Its baseline is 632 B/op in optimized-JIT mode and 688 in runtime-default mode. Recreated-run allocation is 152 B/op versus baseline 912 optimized or 968 default. Do not attribute an additional aggregate allocation saving to the raw API from these results: the isolated test and the complete workload establish different things. The build script was reviewed: it copies only the identical harness into the baseline checkout and builds each revision's own libraries; it does not substitute baseline runtime sources into the head.

Current-thread managed allocation excludes native heap usage, retained-heap totals and work on other threads.

## Regressions and interpretation

The calibration-informed screen flags **resource-deep-insert-remove** in both Linux modes and macOS runtime-default. The macOS optimized screen flags no scenario. This is a heuristic based on the observed same-revision range, not a confidence interval or proof that all unflagged cases are safe.

The default-runtime results, particularly macOS, contain large positive deltas: insertion/removal +218.4%, unique native glyphs +101.4%, over-capacity shaping +40.1%, wheel-offset layout +20.0%. The same macOS calibration also has large changes between identical revisions, including -59.5% paired time for recreated native glyphs and -33.9% for wheel layout. That limits attribution; it does **not** justify discarding the unfavorable comparison or claiming the regression is fixed. Controlled steady-state profiling, runtime-transition analysis and repeated measurements are still needed.

The two latest changes are local mechanical improvements with passing semantic/allocation tests. They do not resolve structural resource invalidation overhead or establish a universal scrolling improvement. Resource insertion/removal must still invalidate cached locations; this report does not propose skipping that necessary invalidation. Eligibility checks for short cold strings also have a cost that remains visible in the Linux scan results.

## Browser checkpoint and acceptance

The previous runtime `64f2b10` is now confirmed to have passed all three browser modes in [run 37916544329](https://github.com/wieslawsoltes/Avalonia/actions/runs/37916544329), as well as all four native jobs. That closes the previously pending historical checkpoint, not validation of the two new runtime commits.

At preparation of this report, the new runtime's [browser run 37920257616](https://github.com/wieslawsoltes/Avalonia/actions/runs/37920257616) was pending behind the serialized matrix for `c485640`. In that intermediate matrix, interpreter and named-interpreter checks passed; AOT had reached the real-input/pixel/profiling step. Consult the run for subsequent completion; no unobserved result is asserted here.

The PR remains draft and unmerged. Six-suite, library and counter correctness validation is complete for `e8c4cb2`; the full browser checkpoint and performance-regression acceptance are not complete in this report. No browser FPS, physical-GPU timing, cold-start improvement or all-platform render-certification claim follows from native measurements.

Workflow artifacts have 14-day retention. This checked-in table preserves every scenario's paired timing observation and exact source/run references after that retention expires. Reproduction and limitations: [measurement.md](measurement.md), [browser-evidence.md](browser-evidence.md), [source-applicability.md](source-applicability.md).
