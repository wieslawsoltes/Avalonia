# Browser demand-timer validation — 2026-10-09

Runtime: **`3d501ba3a30d7b482fd540b3cb8607101cd0590a`**. Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. This report accompanies [native and source validation](validation-demand-paths.md) and [browser timer implementation](browser-demand-timer.md).

## Exact completion status

In [run 37980167945](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945), ordinary interpreter [job 113988902989](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/job/113988902989) and symbol-retaining interpreter [job 113988903379](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/job/113988903379) completed successfully. Each passed 27 Node tests, publishing, the original baseline/head input/pixel/resource comparison, additional same-build calibration and 24 separately timed profiles. Both A/B and A/A comparisons reported zero exact screenshot mismatches and verified visible resource change/restoration.

**AOT job 113988902775 was still running its calibration/profile stage at the last check.** Its publishing and original input/resource/pixel comparison steps had succeeded, but the completed report and full profiling result were not yet available. This document does not claim final-head AOT completion or infer its numbers from another build. The earlier timer-only `0d35c08` three-mode success belongs to [run 37968425305](https://github.com/wieslawsoltes/Avalonia/actions/runs/37968425305), not this revision.

The reports below come only from completed current-revision logs. They do not reuse the preceding checkpoint's browser values. The browser is Chromium 141.0.7390.37, with three alternating A/B pairs per render mode. Additional A/A uses the identical published head served twice. Original inputs, idle windows and comparison settings were not changed by this continuation.

## All completed task-CPU observations

Percentages are paired medians: `100 * (median(head / base) - 1)`, not ratios of independent medians. Negative is faster. Software2D and default/WebGL are fixture modes; the latter can use software SwiftShader and does not certify physical GPU behavior.

| Scenario | Interpreter Software2D | Interpreter WebGL | Named Software2D | Named WebGL |
|---|---:|---:|---:|---:|
| list-wheel | -4.2% | -1.9% | +2.7% | +3.9% |
| list-thumb | -2.7% | +0.9% | -2.9% | -1.3% |
| tree-wheel | +0.1% | +16.7% | -1.3% | +21.3% |
| tree-thumb | -0.0% | -4.5% | -3.5% | +22.9% |
| idle-overlay-off | -77.6% | -69.2% | -72.6% | -57.5% |
| idle-overlay-on | -71.6% | -75.5% | -78.4% | -63.2% |

The displayed -0.0% retains the rounded log value; it is not an equivalence claim. All raw pairs remain in the artifacts. Neither profile self-time nor an independently calculated percentage is substituted for these original task-CPU measurements.

## Idle callback repair

The existing render loop already clears its Tick callback when tasks no longer request work. The old browser adapter continued requesting JavaScript animation frames anyway. The new adapter stops that chain and restarts on Wakeup. The following use the original **three-second** idle windows:

| Deployment / mode / overlay | Baseline RAF callbacks | Head RAF callbacks | Baseline task-CPU median ms | Head task-CPU median ms |
|---|---:|---:|---:|---:|
| Interpreter / Software2D / off | 180 | **0** | 5.62 | 1.25 |
| Interpreter / Software2D / on | 180 | **0** | 5.08 | 1.43 |
| Interpreter / WebGL / off | 180 | **0** | 7.25 | 2.24 |
| Interpreter / WebGL / on | 180 | **0** | 6.83 | 1.67 |
| Named / Software2D / off | 181 | **0** | 5.38 | 1.64 |
| Named / Software2D / on | 180 | **0** | 4.95 | 1.07 |
| Named / WebGL / off | 180 | **0** | 6.29 | 2.67 |
| Named / WebGL / on | 180 | **0** | 5.43 | 1.99 |

The independent medians above are absolute context, not inputs for recomputing the paired percentages. A/A callback counts were zero on both sides in every completed idle case. This is a directly observed removal of unnecessary idle callbacks, not merely a clear noise screen. It is not a claim of zero process CPU, zero timer activity for every possible application, or measured battery savings.

The diagnostic overlay settings are unchanged. The existing render loop remains responsible for continuous work. Host tests with the actual DefaultRenderLoop verify continuous tasks, sleep, Wakeup during Render and remove/readd. These tests plus the browser input/pixel tests do not constitute a complete real threaded-WebAssembly and animation/custom-host certification. A worker without a usable owner-thread synchronization context intentionally retains polling.

## Remaining scrolling observations

The idle improvement does not imply every scroll scenario improved. Ordinary WebGL tree-wheel measured **+16.7%**, a paired median absolute increase of **25.49 ms**. Named WebGL tree-wheel was **+21.3% / +31.89 ms**, and named WebGL tree-thumb **+22.9% / +19.32 ms**. Named list-wheel was +2.7% in software and +3.9% in WebGL; ordinary WebGL list-thumb was +0.9%.

The same-build audit classified the tree cases as `inconclusive-calibration`: ordinary WebGL tree-wheel A/A ratios were **1.138, 1.326, 0.837**; named WebGL tree-wheel **0.919, 1.506, 1.150**; named WebGL tree-thumb **1.058, 0.819, 0.917**. These classifications do not establish that the positive observations are absent or harmless. Named A/A also shifted tree-wheel +15.0% and software tree-thumb +10.7%. Original unfavorable A/B values are retained even when calibration is unstable.

Small residual idle times have high relative A/A variation as well. The callback count has a clear structural change, but the exact CPU percentage is a shared-runner observation, not a confidence interval. No outliers were removed and no repeats were requested to find a favorable classification.

## Startup and deployment size

| Deployment | Baseline runtime raw bytes | Head runtime raw bytes | Baseline per-file gzip estimate | Head per-file gzip estimate | Native names, baseline/head |
|---|---:|---:|---:|---:|---:|
| Interpreter | 18,792,178 | 18,828,812 | 6,685,618 | 6,701,574 | 0 / 0 |
| Named interpreter | 31,667,264 | 31,703,898 | 11,068,987 | 11,084,933 | 28,291 / 28,291 |

These values describe each exact build. In particular, the named-build values are read from this job, not copied from an earlier report with different symbol counts. New runtime code increases deployment size; no unchanged-size claim is made. Per-file gzip is an estimate, not a measured network transfer.

| Deployment / mode | Baseline ready median ms | Head ready median ms | Paired readiness change |
|---|---:|---:|---:|
| Interpreter / Software2D | 786.8 | 768.2 | -6.2% |
| Interpreter / WebGL | 771.8 | 793.4 | +2.4% |
| Named / Software2D | 752.8 | 750.4 | -1.2% |
| Named / WebGL | 953.2 | 867.5 | -7.5% |

Ready-after-RAF is a fixture signal, not physical presentation or universal cold-start performance. The ordinary WebGL positive observation remains separate from idle CPU and scrolling. Different modes and hosts are not interchangeable baselines.

## Profiles and raw artifacts

The two completed deployments produced **48 separate profiles**, 24 each, preserving exact build/frame identities, raw signed deltas and version-2 timeline accounting. The named baseline/software/list-wheel and head/WebGL/list-wheel profiles each contained one negative delta and two reordered samples. They completed without clamping or discarding samples. Unnamed AOT indices are not matched to these named frames across different links.

| Completed result archive | Published artifact | Published SHA-256 |
|---|---|---|
| Interpreter | [11640269610](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11640269610) | `a2e662d1d89159f402405a3262538e63e6fb81c9b9335750836b4c211b7c0742` |
| Named interpreter | [11641013551](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11641013551) | `98a2122396fc63e08d66a6466da84190d5ac6d9db36dfaf9daf5318120ef16b0` |

Exact deployments are retained separately: [interpreter 11639909991](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11639909991), [named interpreter 11640838886](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11640838886). Result retention is 14 days and exact-build retention seven days; the checked-in tables preserve observations after expiry.

## Acceptance

Unnecessary idle RAF polling is repaired in the completed real-browser workloads, with pixels and resource restoration preserved. Final-head AOT profiling was not complete at the time recorded above. Remaining native binding flags, positive/mixed scrolling and readiness observations are not resolved by this improvement. The PR remains draft; the all-regression request is not represented as complete. See [native validation](validation-demand-paths.md) and [diagnostic methodology](browser-regression-diagnostics.md).
