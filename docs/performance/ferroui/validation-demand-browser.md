# Browser demand-timer validation — 2026-10-09

Runtime: **`3d501ba3a30d7b482fd540b3cb8607101cd0590a`**. Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. This report accompanies [native and source validation](validation-demand-paths.md) and [browser timer implementation](browser-demand-timer.md). The final update records the now-completed AOT job; it changes no runtime, test or benchmark source.

## Exact completion status

**All three jobs in [run 37980167945](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945) completed successfully at the current runtime:** ordinary interpreter [113988902989](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/job/113988902989), symbol-retaining interpreter [113988903379](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/job/113988903379), and AOT [113988902775](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/job/113988902775). Each passed 27 Node tests, publishing, the original baseline/head input/pixel/resource comparison, additional same-build calibration and 24 separately timed profiles. All six A/B and A/A comparisons reported zero exact screenshot mismatches and verified visible resource change/restoration. All **72 profiles** completed.

The preceding documentation checkpoint marked AOT pending while its calibration/profile step was still running. That job subsequently completed without a rerun; its completed log supplies the AOT measurements below. Earlier timer-only `0d35c08` validation belongs to [run 37968425305](https://github.com/wieslawsoltes/Avalonia/actions/runs/37968425305), not this revision, and is not substituted here.

The browser is Chromium 141.0.7390.37, with three alternating A/B pairs per render mode. Additional A/A uses the identical published head served twice. Original inputs, idle windows and comparison settings were not changed by this continuation. Functional checks and report-only timing acceptance are different: the completed matrix does not establish that every workload is faster.

## All task-CPU observations

Percentages are paired medians: `100 * (median(head / base) - 1)`, not ratios of independent medians. Negative is faster. Software2D and default/WebGL are fixture modes; the latter can use software SwiftShader and does not certify physical GPU behavior. Different deployment-mode jobs ran independently and their absolute performance must not be compared as though they shared a host.

| Scenario | Interpreter Software2D | Interpreter WebGL | Named Software2D | Named WebGL | AOT Software2D | AOT WebGL |
|---|---:|---:|---:|---:|---:|---:|
| list-wheel | -4.2% | -1.9% | +2.7% | +3.9% | -5.3% | -0.7% |
| list-thumb | -2.7% | +0.9% | -2.9% | -1.3% | -6.3% | -6.7% |
| tree-wheel | +0.1% | +16.7% | -1.3% | +21.3% | +2.1% | +2.0% |
| tree-thumb | -0.0% | -4.5% | -3.5% | +22.9% | -3.5% | +9.8% |
| idle-overlay-off | -77.6% | -69.2% | -72.6% | -57.5% | -88.6% | -88.5% |
| idle-overlay-on | -71.6% | -75.5% | -78.4% | -63.2% | -92.6% | -93.4% |

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
| AOT / Software2D / off | 180 | **0** | 31.62 | 3.60 |
| AOT / Software2D / on | 180 | **0** | 29.61 | 2.20 |
| AOT / WebGL / off | 180 | **0** | 31.00 | 3.63 |
| AOT / WebGL / on | 180 | **0** | 28.04 | 1.84 |

The independent medians above are absolute context, not inputs for recomputing the paired percentages. Across the twelve cases, paired task CPU fell **57.5–93.4%**. A/A callback counts were zero on both sides in every idle case. This is a directly observed removal of unnecessary idle callbacks, not merely a clear noise screen. It is not a claim of zero process CPU, zero timer activity for every possible application, or measured battery savings. In particular, the AOT job's much larger baseline absolute CPU times must not be used to rank deployment modes across different hosts.

The diagnostic overlay settings are unchanged. The existing render loop remains responsible for continuous work. Host tests with the actual DefaultRenderLoop verify continuous tasks, sleep, Wakeup during Render and remove/readd. These tests plus the browser input/pixel tests do not constitute a complete real threaded-WebAssembly and animation/custom-host certification. A worker without a usable owner-thread synchronization context intentionally retains polling.

## Remaining scrolling observations and calibration

The idle improvement does not imply every scroll scenario improved. Positive measurements and their calibration are retained:

| Positive observation | Paired median absolute change | Same-build A/A ratios | Classification |
|---|---:|---|---|
| Interpreter WebGL tree-wheel, +16.7% | +25.49 ms | 1.138, 1.326, 0.837 | inconclusive-calibration |
| Named WebGL tree-wheel, +21.3% | +31.89 ms | 0.919, 1.506, 1.150 | inconclusive-calibration |
| Named WebGL tree-thumb, +22.9% | +19.32 ms | 1.058, 0.819, 0.917 | inconclusive-calibration |
| AOT Software2D tree-wheel, +2.1% | +3.34 ms | 1.023, 1.076, 0.849 | inconclusive-calibration |
| AOT WebGL tree-wheel, +2.0% | +4.19 ms | 1.046, 1.056, 0.903 | inconclusive-calibration |
| AOT WebGL tree-thumb, +9.8% | +12.00 ms | 0.831, 1.011, 0.873 | inconclusive-calibration |

Named list-wheel also measured +2.7% in software and +3.9% in WebGL; ordinary WebGL list-thumb was +0.9%. These are retained in the complete table rather than omitted because they are smaller. Same-build classifications do not establish that positive observations are absent or harmless. Named A/A also shifted tree-wheel +15.0% and software tree-thumb +10.7%; AOT WebGL tree-thumb A/A shifted -12.7%. Original unfavorable A/B values remain even when calibration is unstable.

For additional absolute context, AOT software tree-wheel independent task-CPU medians were 163.72 -> 171.11 ms; WebGL tree-wheel 212.32 -> 207.94 ms; WebGL tree-thumb 122.89 -> 134.90 ms. The WebGL tree-wheel paired statistic is positive despite the independent medians decreasing. The paired and independent statistics are different calculations; neither is substituted selectively for a favorable direction.

AOT software list-wheel/list-thumb and WebGL list-thumb were classified `no-slower-pair-observed`; AOT WebGL list-wheel was `inconclusive`. AOT WebGL idle-overlay-on was `consistent-speedup`, while the other three AOT idle cases were `inconclusive-calibration` despite large reductions. Small residual idle times have high relative A/A variation. The callback count has a clear structural change, but the exact CPU percentage is a shared-runner observation, not a confidence interval. No outliers were removed and no repeats were requested to find a favorable classification.

## Startup and deployment size

| Deployment | Baseline runtime raw bytes | Head runtime raw bytes | Baseline per-file gzip estimate | Head per-file gzip estimate | Native names, baseline/head |
|---|---:|---:|---:|---:|---:|
| Interpreter | 18,792,178 | 18,828,812 | 6,685,618 | 6,701,574 | 0 / 0 |
| Named interpreter | 31,667,264 | 31,703,898 | 11,068,987 | 11,084,933 | 28,291 / 28,291 |
| AOT | 39,434,843 | 39,626,768 | 12,260,394 | 12,325,678 | 0 / 0 |

These values describe each exact build. In particular, the named-build values are read from this job, not copied from an earlier report with different symbol counts. New runtime code increases deployment size; no unchanged-size claim is made. Per-file gzip is an estimate, not a measured network transfer.

| Deployment / mode | Baseline ready median ms | Head ready median ms | Paired readiness change |
|---|---:|---:|---:|
| Interpreter / Software2D | 786.8 | 768.2 | -6.2% |
| Interpreter / WebGL | 771.8 | 793.4 | +2.4% |
| Named / Software2D | 752.8 | 750.4 | -1.2% |
| Named / WebGL | 953.2 | 867.5 | -7.5% |
| AOT / Software2D | 9,529.4 | 9,652.5 | +1.8% |
| AOT / WebGL | 10,450.8 | 9,710.2 | -7.8% |

Ready-after-RAF is a fixture signal, not physical presentation or universal cold-start performance. The ordinary WebGL and AOT Software2D positive observations remain separate from idle CPU and scrolling. Different modes and hosts are not interchangeable baselines; prior AOT readiness percentages cannot be used as an isolated estimate of this timer change.

## Profiles and raw artifacts

All three deployments produced **72 separate profiles**, 24 each, preserving exact build/frame identities, raw signed deltas and version-2 timeline accounting. The named baseline/software/list-wheel and head/WebGL/list-wheel profiles each contained one negative delta and two reordered samples. They completed without clamping or discarding samples. All 24 AOT profiles reported zero negative/reordered samples. Unnamed AOT indices are not matched to named frames across different links.

| Result archive | Published artifact | Published SHA-256 |
|---|---|---|
| Interpreter | [11640269610](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11640269610) | `a2e662d1d89159f402405a3262538e63e6fb81c9b9335750836b4c211b7c0742` |
| Named interpreter | [11641013551](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11641013551) | `98a2122396fc63e08d66a6466da84190d5ac6d9db36dfaf9daf5318120ef16b0` |
| AOT | [11642475357](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11642475357) | `026adc4a4346d59265336bb8b255ccea62107f380c9ce9114f75d823c126537b` |

Exact deployments are retained separately: [interpreter 11639909991](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11639909991), [named interpreter 11640838886](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11640838886), [AOT 11642270699](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167945/artifacts/11642270699). The AOT exact-build archive's published SHA-256 is `357b99474c0c3b725156dc8bba22878198166c5b4c07eca26d95bda4f76b65be`. Result retention is 14 days and exact-build retention seven days; the checked-in tables preserve observations after expiry.

## Acceptance

Unnecessary idle RAF polling is repaired in all three completed real-browser deployment modes, with pixels and resource restoration preserved. The current runtime's managed, counter, native and three-mode browser validation is complete for the configured workloads; no pending job is being counted as a success.

**All-regression performance acceptance remains open.** Native Linux binding flags, positive/mixed scrolling, readiness and deployment-size tradeoffs are not resolved by the idle improvement. Host-side state tests do not certify every real threaded-WebAssembly, animation or custom-host case. The PR remains draft and unmerged. See [native validation](validation-demand-paths.md) and [diagnostic methodology](browser-regression-diagnostics.md).
