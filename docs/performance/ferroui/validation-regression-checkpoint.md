# Historical repair checkpoint and repeats: bb80607

Checkpoint: `bb8060735e99648ca073f0f90310864fdf1549a0`, before final runtime `fa8bb8c926cf6d1271b23d6e89a06633051f56cf`. Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. This report preserves repeat evidence that prompted the last fix; it is not current-runtime validation. See [final repair results](validation-regression-repairs.md).

At this checkpoint, [six-suite/.NET 8 validation](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712478), [counter validation](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712553) and [all three browser modes](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712404) passed. The eight repair commits then present introduced 46 tests. Configured-suite success does not include all platform/integration projects.

## First native matrix and planned repeats

All four original jobs in [native run 37930712531](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531) completed with no calibration-screen flags, but positive timings remained. Original jobs: [Linux non-tiered 113820713263](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113820713263), [Linux default 113820713701](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113820713701), [macOS non-tiered 113820713601](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113820713601), [macOS default 113820713473](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113820713473).

Each default-runtime job was repeated once at the same revision. Actual repeats: [Linux 113823999986](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113823999986) and [macOS 113826086465](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712531/job/113826086465). Other successful jobs copied to new IDs by GitHub rerun attempts are not fresh measurements. Five alternating process pairs followed five A/A pairs; the original 26 scenarios and thresholds were unchanged. Negative below means faster; percentages are medians of pair ratios, not ratios of independent medians.

| Scenario | Linux default repeat | macOS default repeat |
|---|---:|---:|
| property-no-listener | +0.6% | +0.5% |
| property-inpc | -1.7% | +4.8% |
| inheritance-reparent | +0.6% | +7.8% |
| template-identity | -10.0% | -2.3% |
| reflection-description-reused | -23.2% | -6.4% |
| reflection-description-once | -27.5% | -11.8% |
| style-type-miss | +45.0% | -35.0% |
| shape-repeated-short | -56.0% | -65.7% |
| shape-unique-short | -0.8% | -0.7% |
| shape-one-pass-short | -7.3% | -0.5% |
| shape-hot-with-one-off-scan | -39.2% | -44.8% |
| shape-long-uncacheable | -3.1% | -4.6% |
| shape-context-slice | +19.5% | -1.2% |
| skia-retained-text-blob | -4.6% | -11.2% |
| text-layout-short | -9.0% | -23.2% |
| listbox-wheel-offset | +4.9% | -13.4% |
| listbox-viewport-jump | -17.4% | -18.2% |
| resource-deep-hit | -55.4% | -50.8% |
| resource-deep-miss | -98.4% | -98.3% |
| resource-deep-mutation | -88.6% | -84.3% |
| resource-local-replacement | +5.1% | -4.6% |
| resource-deep-insert-remove | -86.1% | -85.1% |
| selector-compound-live | -42.8% | -46.0% |
| native-glyph-recreated | -78.7% | -38.7% |
| native-glyph-unique | -32.3% | -20.8% |
| text-layout-wrap-fallback | -28.7% | -25.4% |

**Linux flagged simple selector misses and context-sensitive shaping; macOS flagged none.** The initial clean screen was not treated as conclusive. The ninth runtime commit removed managed assignability memoization and repeated per-glyph helper/span reads in response. It changed neither benchmark source nor runtime settings/thresholds. See [follow-up rationale](single-selector-regression.md).

## Browser observations were not erased by pixel success

The [interpreter](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712404/job/113820987511) and [AOT](https://github.com/wieslawsoltes/Avalonia/actions/runs/37930712404/job/113820987369) reports each recorded zero screenshot mismatches; all three deployment modes passed. However, task-CPU timings were mixed across three pairs per rendering mode:

| Scenario | Interpreter software | Interpreter default | AOT software | AOT default |
|---|---:|---:|---:|---:|
| list-wheel | -12.6% | -16.0% | +0.8% | -3.4% |
| list-thumb | -17.8% | -14.5% | +15.3% | +3.5% |
| tree-wheel | -4.0% | -3.3% | +10.2% | -12.2% |
| tree-thumb | -0.9% | -4.6% | +6.6% | -4.7% |
| idle-overlay-off | +5.0% | -4.8% | +5.8% | -4.9% |
| idle-overlay-on | -3.2% | +0.7% | +9.3% | -7.8% |

AOT software thumb/tree/idle observations were slower. Browser timings were report-only, not statistically calibrated; native microbenchmarks and exact pixels do not resolve these observations. The following runtime needed its own browser measurements, now recorded in the final report. Named-interpreter timings must be taken from its own job, not inferred from these columns.

The first matrix and both actual repeats remain linked so favorable reruns are not substituted for unfavorable observations. Workflow artifacts have finite retention; this checked-in table preserves the complete repeat and the browser counterexamples that informed the next source change.
