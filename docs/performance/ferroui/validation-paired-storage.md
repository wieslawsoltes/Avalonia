# Paired glyph storage and dispatch: validation, 2026-10-09

## Exact scope and outcome

Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. This continuation started at `ad96c386f5753545143c8844a76fb257e2ad1d5a` and ends at runtime/diagnostic revision **`ddcb036f011b0ec60dfa5da15a8cee8d2c5a1c0f`** on `perf/ferroui-runtime-optimizations`, in [fork PR #8](https://github.com/wieslawsoltes/Avalonia/pull/8).

Three commits implement [paired glyph storage](paired-glyph-storage.md) (`1daa968`), [profile timestamp reconstruction](profile-timestamp-repair.md) (`f7bcdce`), and [local resource write/notification dispatch](local-resource-write-overhead.md) (`ddcb036`). They add **23 managed test cases and seven Node tests**. The commit publishing this report changes documentation only, not the validated runtime, tests or harness.

**All configured managed, counter, native and three-mode browser checks completed at ddcb036, including all 72 separate browser profiles.** Cached and cold shaping allocation are now below the original baseline. Both Linux modes and macOS non-tiered JIT show faster local resource writes. However, positive and unstable native/browser timing observations remain; report-only success does not establish all-regression performance acceptance. The PR remains draft and unmerged.

The paired owner removes one redundant holder, counter, finalizable reference and buffer field while keeping the two arrays independent between runs and alive until the last split/bidi alias releases them. Metrics metadata, mutation generations and finalizer cleanup remain intact. Local resource writes retain the callback-sensitive guard, committed-value-before-notification sequence and all required invalidation. The profile repair changes diagnostic accounting, not runtime execution or the unprofiled benchmark.

The original 26 native scenarios, harness sources, operation counts, warm-up policy, comparison baseline and screening thresholds are unchanged. Browser A/B scenarios, input, pixels and timing are unchanged. Signed timestamp reconstruction affects only separate diagnostic profiles; their version-2 self-time summaries must not be compared with the old accounting as though the convention were unchanged.

## Completed checks at ddcb036

| Validation | Result and exact evidence |
|---|---|
| Six Release suites and affected .NET 8 builds | [Run 37963465211](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465211): Base, Controls, Markup, XAML, Skia, Themes and affected library builds passed. |
| Normal/instrumented builds, counter absence and workload | [Run 37963465262, job 113931832087](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465262/job/113931832087): passed. |
| Four native OS/runtime comparisons | [Run 37963465495, initial attempt](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/attempts/1): all four completed; each passed 12 Python tests and operation/checksum/container-count/environment validation. |
| One planned macOS default repeat | [Job 113936318314](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113936318314): completed with the same source, workload and checks. Both initial and repeat results are retained below. |
| Ordinary interpreter browser | [Job 113932061368](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061368): publish, 20 Node tests, original A/B, same-build A/A, resource/pixel checks and 24 separate profiles passed. |
| Symbol-retaining interpreter browser | [Job 113932061565](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061565): the same stages and 24 profiles passed, including two traces with actual negative timestamp deltas. |
| AOT browser | [Job 113932061913](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061913): the same stages and 24 profiles passed. |

Every browser A/B and A/A comparison reported zero exact screenshot mismatches and verified visible dynamic-resource changes/restoration. These are configured-suite results, not an assertion that every platform/integration project ran. Existing skipped tests are not counted as executed tests. Native timing and browser A/A diagnostics remain report-only.

## All native scenarios: initial attempt

Five alternating fresh-process A/B pairs follow five same-revision A/A calibration pairs. Percentage = `100 * (median(head_time / base_time) - 1)`; it is not the ratio of independent median ns/op values. Negative is faster. The table compares the **entire PR** with the original baseline, not the isolated contribution of one repair.

The jobs used .NET 10.0.12 / SDK 10.0.401, Ubuntu 24.04.5 and macOS 26.6.2 arm64. Non-tiered mode disables tiered compilation; default mode allows runtime tier transitions. Compare within a column, not absolute speeds between hosts.

| Scenario | Linux non-tiered | Linux default | macOS non-tiered | macOS default |
|---|---:|---:|---:|---:|
| property-no-listener | -2.7% | +1.8% | -8.7% | +5.8% |
| property-inpc | -12.1% | -2.5% | -4.8% | -2.9% |
| inheritance-reparent | -3.8% | -4.1% | -7.4% | -1.5% |
| template-identity | -25.1% | -9.9% | -18.6% | -2.3% |
| reflection-description-reused | -3.7% | -11.1% | -6.9% | -10.5% |
| reflection-description-once | -4.8% | -5.4% | -16.6% | -14.8% |
| style-type-miss | -5.8% | -7.5% | -18.4% | -20.3% |
| shape-repeated-short | -72.3% | -60.0% | -87.9% | -71.5% |
| shape-unique-short | -13.3% | -19.6% | -0.5% | -2.3% |
| shape-one-pass-short | -12.8% | -20.0% | -7.3% | -3.6% |
| shape-hot-with-one-off-scan | -53.6% | -43.5% | -58.8% | -52.5% |
| shape-long-uncacheable | -15.0% | -4.4% | -5.4% | +5.0% |
| shape-context-slice | -10.9% | -7.8% | +8.6% | -4.7% |
| skia-retained-text-blob | -39.6% | -15.8% | -41.0% | -10.4% |
| text-layout-short | -45.2% | -14.2% | -59.9% | -21.1% |
| listbox-wheel-offset | -3.4% | -4.6% | -10.4% | +31.0% |
| listbox-viewport-jump | -7.4% | -3.8% | -9.6% | +28.2% |
| resource-deep-hit | -96.2% | -54.4% | -98.0% | -54.0% |
| resource-deep-miss | -98.9% | -98.9% | -99.7% | -98.1% |
| resource-deep-mutation | -90.6% | -86.3% | -94.6% | -82.7% |
| resource-local-replacement | -8.9% | -4.5% | -13.3% | +1.3% |
| resource-deep-insert-remove | -91.6% | -87.6% | -96.3% | -82.9% |
| selector-compound-live | -2.1% | -35.8% | -1.1% | -41.0% |
| native-glyph-recreated | -71.0% | -78.3% | -71.1% | -25.0% |
| native-glyph-unique | -39.6% | -19.7% | -52.2% | -25.5% |
| text-layout-wrap-fallback | -27.7% | -25.9% | -48.7% | -16.1% |

Exact initial jobs: [Linux non-tiered 113931899352](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113931899352), [Linux default 113931898793](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113931898793), [macOS non-tiered 113931899221](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113931899221), [macOS default 113931899244](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113931899244).

All initial heuristic screens were clear, but that does not erase positive measurements. macOS default wheel/viewport medians were +31.0%/+28.2%; its A/A calibration itself had large shifts, including wheel -13.2%, viewport -31.2%, retained blob +43.8% and deep miss +95.1%. Independent wheel ns/op medians (752,039.8 -> 707,358.6) even have a different direction from the paired statistic. This indicates unstable comparisons, not proof that a source regression is absent. macOS non-tiered context shaping remained +8.6% against A/A +7.5%. Linux default no-listener publication was +1.8%.

## Planned macOS default repeat: retain both outcomes

One repeat was requested after examining the initial large calibration swings. [Fresh job 113936318314](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/job/113936318314) actually ran five new A/A and five A/B process pairs at ddcb036. The other successful jobs shown under the new attempt are copied prior results, not additional measurements. No subsequent repeat was requested to select a favorable outcome.

| Scenario | Initial paired change | Planned repeat paired change |
|---|---:|---:|
| property-no-listener | +5.8% | +5.4% |
| property-inpc | -2.9% | -3.3% |
| inheritance-reparent | -1.5% | +27.8% |
| template-identity | -2.3% | -11.4% |
| reflection-description-reused | -10.5% | -8.8% |
| reflection-description-once | -14.8% | -26.4% |
| style-type-miss | -20.3% | -22.5% |
| shape-repeated-short | -71.5% | -71.5% |
| shape-unique-short | -2.3% | -11.8% |
| shape-one-pass-short | -3.6% | +6.1% |
| shape-hot-with-one-off-scan | -52.5% | -40.9% |
| shape-long-uncacheable | +5.0% | -20.4% |
| shape-context-slice | -4.7% | -11.2% |
| skia-retained-text-blob | -10.4% | -5.0% |
| text-layout-short | -21.1% | -39.1% |
| listbox-wheel-offset | +31.0% | -2.6% |
| listbox-viewport-jump | +28.2% | +2.5% |
| resource-deep-hit | -54.0% | -52.6% |
| resource-deep-miss | -98.1% | -98.0% |
| resource-deep-mutation | -82.7% | -84.1% |
| resource-local-replacement | +1.3% | +28.0% |
| resource-deep-insert-remove | -82.9% | -80.8% |
| selector-compound-live | -41.0% | -42.6% |
| native-glyph-recreated | -25.0% | -41.6% |
| native-glyph-unique | -25.5% | +34.7% |
| text-layout-wrap-fallback | -16.1% | -37.1% |

The repeat did not establish a stable all-workload result. Its A/A calibration also varied strongly: mixed shaping -37.4%, resource mutation -45.3%, wrapped layout -46.5%, native unique glyphs +43.2%. It reported no consistent regression beyond the heuristic envelope, which has limited sensitivity under that variation. Inheritance (+27.8%), local replacement (+28.0%), unique native glyphs (+34.7%) and one-pass shaping (+6.1%) remain positive observations, alongside the initial scrolling and other positive cases. Neither attempt is discarded or presented as decisive causal attribution.

The repeat's [raw data artifact 11633940436](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465495/artifacts/11633940436) has published SHA-256 `4586e15c896c9290435463da581ef1511595b791d5501bb49ba1be9aeaa51b83`. The report retains initial and repeated observations after artifact expiry.

## Allocation regression closed

Removing duplicated array ownership more than offsets the optional per-hit metadata. State was not moved into an unbounded global table. All four initial jobs recorded the following counts; the planned repeat also confirmed cold shaping at 240 B/op and cached shaping at 255 B/op.

| Scenario | Original baseline B/op | Before paired ownership B/op | Current initial B/op |
|---|---:|---:|---:|
| Cold one-pass/over-capacity/long/context shaping | 336 | 336 | **240** |
| Repeated cacheable shaping | 336 | about 351-352 | **255.5** |
| Mixed hot/one-off shaping | 336 | about 348 | **252** |
| Unique native glyphs, non-tiered | 632 | 416 | 416 |
| Unique native glyphs, default | 688 | 416 | 416 |
| Recreated native glyphs | 912 / 968 | 152 | 152 |
| Retained native blob lookup | 96 | 0 | 0 |
| Resource mutation | 24 | 24 | 24 |

The intermediate [1daa968 run 37961650646](https://github.com/wieslawsoltes/Avalonia/actions/runs/37961650646) already measured cold shaping 240 and repeated shaping approximately 254.5-255.5 B/op in Linux, before the resource-dispatch follow-up. Its Linux default local replacement was still +9.6% (94.5 -> 101.5 ns/op). At ddcb036 the same original scenario was -4.5% (96.4 -> 92.5 ns/op). Different runs/hosts mean those elapsed-time observations are not an isolated causal estimate; the code removes real getter/branch work and the allocation comparison uses the same original workload.

Current Linux short-layout allocation is 2,241.2 -> 1,377.2 B/op. Wrapped layout is 10,889.2 -> 8,945.2 non-tiered and 10,890.4 -> 8,946.4 default. macOS is 2,240 -> 1,376 short and 7,568 -> 6,128 wrapped. Allocation is managed current-thread only, not exact retained heap, all-thread memory or native memory. The existing bounded native-font pool and glyph caches still retain native resources within their documented limits.

## All browser modes and scenarios

[Run 37963465457](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457) completed all three modes with Chromium 141.0.7390.37. Original A/B comparisons use three alternating pairs per rendering mode; additional A/A comparisons serve the identical published head twice. The latter measure repeat variation of the deployed build, not independent-build variation. Each comparison checks exact screenshots and visible resource replacement/restoration. Every A/B and A/A pixel check reported zero mismatches.

These are paired task-CPU changes, not ratios of independent medians and not GPU time or presented FPS. Software2D and default/WebGL are fixture modes; SwiftShader can be software. The six columns are independent deployment/render comparisons, not a claim that one deployment mode is faster than another.

| Scenario | Interpreter SW | Interpreter WebGL | Named SW | Named WebGL | AOT SW | AOT WebGL |
|---|---:|---:|---:|---:|---:|---:|
| list-wheel | -2.6% | -3.3% | -2.4% | -2.7% | +2.5% | +12.6% |
| list-thumb | -2.9% | -2.7% | -3.0% | -3.3% | -2.7% | -1.7% |
| tree-wheel | -7.5% | -2.5% | -5.4% | -4.9% | +1.2% | -12.4% |
| tree-thumb | -2.4% | -6.6% | -6.2% | -2.9% | +0.5% | +7.8% |
| idle-overlay-off | -3.2% | +6.0% | +9.5% | -6.1% | +0.8% | -0.6% |
| idle-overlay-on | -2.7% | -1.9% | +1.5% | +7.2% | +29.2% | -3.5% |

Exact jobs: [ordinary interpreter 113932061368](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061368), [named interpreter 113932061565](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061565), [AOT 113932061913](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/job/113932061913).

### Interpretation of A/A diagnostics and positive timings

All eight scroll medians in the ordinary interpreter and all eight in the named interpreter were negative. Many corresponding rows were classified `no-slower-pair-observed`; named Software2D tree-thumb was `consistent-speedup`. This does not certify all browser workloads: several other rows remain inconclusive under the descriptive screen.

No mode produced a `consistent-slowdown` classification, but positive medians were not converted into a claim of equivalence. In particular:

| Positive observation | Paired median absolute change | A/A ratios | Classification |
|---|---:|---|---|
| Interpreter WebGL idle-off, +6.0% | +0.88 ms over 3 s | 1.042, 1.053, 0.976 | inconclusive-positive-median |
| Named SW idle-off, +9.5% | +1.79 ms over 3 s | 1.004, 1.004, 0.998 | inconclusive-positive-median |
| Named WebGL idle-on, +7.2% | +0.96 ms over 3 s | 1.001, 1.001, 1.001 | inconclusive-positive-median |
| AOT SW list-wheel, +2.5% | +4.29 ms | 1.002, 0.980, 0.969 | inconclusive-positive-median |
| AOT WebGL list-wheel, +12.6% | +26.13 ms | 0.910, 0.925, 1.058 | inconclusive-positive-median |
| AOT SW tree-wheel, +1.2% | +0.92 ms | 0.971, 0.964, 0.993 | inconclusive-positive-median |
| AOT WebGL tree-thumb, +7.8% | +4.38 ms | 1.165, 0.980, 0.950 | inconclusive-calibration |
| AOT SW idle-on, +29.2% | +1.47 ms over 3 s | 1.127, 1.156, 1.034 | inconclusive-calibration |

All raw pairs, work counts and classifications remain in `calibration-audit.json`. Other inconclusive rows are not discarded. AOT WebGL list-wheel's independent task-CPU medians were 217.59 -> 233.83 ms; its paired percentage/absolute change above use paired samples, not those two independent medians. The source-versus-host/runtime attribution of the positive cases remains open. The separate profiles cover the affected scenarios but their sampled times are not substituted for unprofiled A/B results.

Idle measurements retain three seconds of elapsed observation. RAF counts were 180 per interval in all named/AOT cases; ordinary interpreter Software2D idle-off was 181 -> 180 and its other cases 180 -> 180. A callback count is not a rendered frame count, input latency or hardware presentation claim.

### Deployment size and startup remain separate tradeoffs

| Deployment | Base runtime raw bytes | Head runtime raw bytes | Base gzip estimate | Head gzip estimate | Native function names, base/head |
|---|---:|---:|---:|---:|---:|
| Interpreter | 18,792,178 | 18,826,482 | 6,685,647 | 6,700,806 | 0 / 0 |
| Named interpreter | 21,250,423 | 21,284,727 | 7,248,946 | 7,264,041 | 6,996 / 6,996 |
| AOT | 39,434,843 | 39,628,180 | 12,260,514 | 12,329,465 | 0 / 0 |

| Deployment/render mode | Base ready median ms | Head ready median ms | Paired readiness change |
|---|---:|---:|---:|
| Interpreter Software2D | 1,004.0 | 998.4 | -1.2% |
| Interpreter WebGL | 1,055.3 | 1,085.9 | +1.4% |
| Named Software2D | 1,167.9 | 1,128.3 | -2.4% |
| Named WebGL | 1,176.1 | 1,186.2 | +2.0% |
| AOT Software2D | 5,154.3 | 5,788.5 | +12.3% |
| AOT WebGL | 5,796.7 | 5,498.9 | -5.1% |

Readiness is the fixture's ready-after-RAF signal, not physical presentation. **AOT Software2D readiness +12.3% remains a positive observation**, separately from scrolling and the task-CPU A/A screen. Added implementation code also increases deployment size as shown; no unchanged-size or universal-startup improvement is claimed. Per-file gzip is an estimate, not measured network transfer, and different deployment modes/hosts are not interchangeable baselines.

## Profile accounting repair exercised by real traces

The predecessor's symbol-retaining job [113855330633](https://github.com/wieslawsoltes/Avalonia/actions/runs/37941087407/job/113855330633) passed publishing, A/B input/pixels and same-build A/A before failing in the old CPU-profile helper with `Invalid sample duration`. Its complete profiling stage did not pass. The old error did not print the rejected value; its exact sample is not inferred from that message alone.

The replacement reconstructs absolute timestamps from signed deltas, sorts timestamp/node pairs and assigns forward intervals, with explicit unattributed initial time and no clamping or discarded samples. It records versioned timing/reorder metadata and rejects malformed data. Twenty Node tests, including seven new timeline cases, passed in every browser mode.

All three current modes completed **24 separate profiles each, 72 total**, covering baseline/head wheel, thumb and idle in both render modes. Two named-interpreter traces actually exercised negative deltas: head/Software2D/list-thumb had one negative delta and two reordered samples (12.738 ms initial unattributed time); baseline/WebGL/list-wheel had one negative delta and two reordered samples (49.273 ms initial unattributed). All other 70 traces reported no negative deltas. These two real cases, preserved without clamping, provide direct validation beyond synthetic tests. See [profile-timestamp-repair.md](profile-timestamp-repair.md).

Result archives retain raw `.cpuprofile` files, per-frame summaries, A/B/A/A JSON, decoded screenshots, timing metadata and exact deployed file/code hashes:

| Archive | Artifact ID | Published SHA-256 |
|---|---:|---|
| Interpreter results | [11632659382](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11632659382) | `7d593ad0dfcdd29f9eb235476f37e869cbdd307ee0ce0e03fdf86c44e06597de` |
| Named interpreter results | [11632449464](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11632449464) | `faa6b61d19e03f2c70703851d6ee174c6919602570d4da60a5621b11ea0c4c4e` |
| AOT results | [11634110049](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11634110049) | `dd223d86256eb80998906f11260c383dab26f9d3ceecf44782fb858840592dd6` |

Results have 14-day retention; exact-build archives have seven days: [interpreter 11632214634](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11632214634), [named 11633698504](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11633698504), [AOT 11632554996](https://github.com/wieslawsoltes/Avalonia/actions/runs/37963465457/artifacts/11632554996). Raw frame identities are meaningful only for their fingerprinted build; unknown WebAssembly function indices are not assumed to align between different links.

## Historical attribution correction

The [preceding follow-up](remaining-regression-followup.md) corrected AOT percentages previously attributed to `fa8bb8c`. The claimed +21.0% software list-thumb, +30.1% software tree-thumb, +34.4% default tree-wheel and +32.3% default tree-thumb were not readings from the cited job. Its actual respective values were -8.7%, +0.6%, -11.3% and -8.0%. Correcting the report is not an optimization gain attributable to this continuation. Raw earlier evidence and the erratum remain available.

## Acceptance boundaries

Code-level duplicated ownership, publication-dispatch overhead and profiler defects have concrete repairs and regression coverage. Cached and cold shaping allocations are below baseline. All configured functional validation completed at the final runtime; there is no pending browser/profile job being counted as a success.

**All-regression performance acceptance remains incomplete.** Positive AOT wheel/readiness/idle observations and the unstable macOS default results are retained, not declared resolved because a heuristic screen is clear. The planned repeat changed which native cases were slower rather than establishing a stable result. Further source attribution requires controlling runtime transitions and host variation; it must not discard the existing cases or replace unprofiled timings with profile self-time.

No benchmark threshold, workload, required callback or invalidation was removed. Public application APIs, deployment defaults, diagnostic overlays and compositor behavior remain intentionally unchanged. The branch and PR are confined to the fork and are not automatically merged. Reproduction: [measurement.md](measurement.md), [browser-regression-diagnostics.md](browser-regression-diagnostics.md), [browser-evidence.md](browser-evidence.md).
