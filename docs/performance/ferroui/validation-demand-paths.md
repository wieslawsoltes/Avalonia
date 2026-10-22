# Demand timer, inline glyphs and singleton inheritance — 2026-10-09

## Revisions and outcome

Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. Current runtime/tests: **`3d501ba3a30d7b482fd540b3cb8607101cd0590a`**, on `perf/ferroui-runtime-optimizations` in [fork PR #8](https://github.com/wieslawsoltes/Avalonia/pull/8). The commit publishing this report changes Markdown only.

The continuation recovered the interrupted Skia commit, completed the incomplete inheritance draft with its missing proof and regression tests, and hardened browser worker wakeups. No source blob lacking its required helper was pushed. The unrelated diagnostic-type change found in the incomplete draft was removed before publication. The PR remains draft and unmerged.

| Revision | Implementation |
|---|---|
| `0d35c08` | Demand-driven browser RAF and actual timestamp forwarding, implemented in the preceding interrupted session. |
| `9780137` | Recovered and pushed inline wrapper-local glyph blobs and span-based built-in glyph scans. |
| `10c0759` | Completed singleton inheritance recognition, general fallback and inherited INPC reuse. |
| `3d501ba` | Base synchronization-context fallback, retry after a failed Post, and actual render-loop integration tests. |

Feature details: [demand timer](browser-demand-timer.md), [timer integration](browser-render-loop-integration.md), [inline glyph dispatch](inline-glyph-dispatch.md), [single-property inheritance](single-property-inheritance.md).

**Measured repairs:** singleton reparenting is faster in all four current native comparisons; recreated and unique native glyphs each allocate 48 fewer managed bytes than the preceding implementation; interpreter idle RAF callbacks stop instead of running continuously. **Not a complete all-regression acceptance:** Linux default reflection-binding construction is flagged by the existing screen, several macOS cases are slower, and browser scrolling/startup require the separate results and caveats in [the browser report](validation-demand-browser.md).

## Validation scope

[Run 37980167993](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167993) passed all six configured Release suites (Base, Controls, Markup, XAML, Skia and Themes) and affected .NET 8 library builds at `3d501ba`. [Counter run 37980167857, job 113988354783](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167857/job/113988354783) passed ordinary/instrumented builds, counter-absence checks and the real instrumented workload.

The three changes pushed during this recovery add **23 managed cases**: seven inline glyph tests, eleven inheritance cases and five render-loop/context integration cases. Including the preceding demand-timer commit, the extension from `e92428d` has **30 new managed cases and seven Node cases**. The browser job now runs 27 Node cases in total. Existing general inheritance pool tests still exercise actual rentals by using two-property sources; they were not removed or relaxed to bypass the new singleton path.

All four jobs in [native run 37980167927](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927) completed, each with 12 Python report tests, five fresh-process A/A calibration pairs and five alternating baseline/head pairs. Operation counts, checksums, container prepare/clear counts and reported environments agreed within each comparison. These are functional/report-validity checks; timings are report-only unless explicitly gated.

No native scenario, operation count, warm-up policy, original baseline or regression threshold was changed. Original browser A/B input and time windows are unchanged. No unfavorable case was deleted, renamed or replaced by a profiled timing. No CI rerun was requested in this continuation to select a favorable outcome.

## Full native timing matrix

Paired percentage = `100 * (median(head_time / base_time) - 1)`. Negative is faster. It can differ from the ratio of independently reported median ns/op, including direction. These are whole-PR comparisons against the original baseline, not isolated estimates of the latest commit's contribution. Jobs used .NET 10.0.12 / SDK 10.0.401 on Linux and macOS arm64. Compare within a column, not absolute speeds across hosts.

| Scenario | Linux non-tiered JIT | Linux default | macOS non-tiered JIT | macOS default |
|---|---:|---:|---:|---:|
| property-no-listener | -5.6% | +0.8% | +45.6% | -2.0% |
| property-inpc | -18.0% | -3.3% | +17.9% | +8.7% |
| inheritance-reparent | -28.0% | -22.3% | -25.4% | -27.8% |
| template-identity | -31.0% | -10.9% | -13.9% | -13.2% |
| reflection-description-reused | -23.7% | **+14.5%** | -17.8% | -9.8% |
| reflection-description-once | -2.9% | **+14.2%** | -9.6% | -31.3% |
| style-type-miss | -1.5% | -1.2% | -13.5% | -24.2% |
| shape-repeated-short | -71.6% | -56.7% | -86.6% | -74.1% |
| shape-unique-short | -7.8% | -19.6% | -7.6% | -9.3% |
| shape-one-pass-short | -7.6% | -21.9% | -7.6% | -15.4% |
| shape-hot-with-one-off-scan | -52.5% | -47.0% | -61.4% | -51.4% |
| shape-long-uncacheable | -8.5% | -2.7% | -33.2% | -6.7% |
| shape-context-slice | -6.1% | -7.4% | +2.7% | -10.8% |
| skia-retained-text-blob | -59.7% | -20.1% | -73.2% | -58.7% |
| text-layout-short | -44.5% | -13.2% | -62.0% | +2.0% |
| listbox-wheel-offset | -7.4% | -4.4% | -5.1% | -1.0% |
| listbox-viewport-jump | -8.7% | -6.2% | -5.6% | -29.5% |
| resource-deep-hit | -96.9% | -53.3% | -98.1% | -54.2% |
| resource-deep-miss | -99.1% | -97.6% | -99.7% | -98.3% |
| resource-deep-mutation | -91.5% | -88.8% | -94.9% | -83.6% |
| resource-local-replacement | -9.7% | -3.0% | -2.2% | +7.9% |
| resource-deep-insert-remove | -94.0% | -88.2% | -96.3% | -84.0% |
| selector-compound-live | -2.3% | -12.5% | +4.2% | -17.8% |
| native-glyph-recreated | -79.8% | -81.4% | -80.6% | -80.6% |
| native-glyph-unique | -40.7% | -24.1% | -52.3% | **+105.9%** |
| text-layout-wrap-fallback | -27.8% | -30.7% | -43.3% | -48.7% |

Exact jobs: [Linux non-tiered 113988539708](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/job/113988539708), [Linux default 113988539869](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/job/113988539869), [macOS non-tiered 113988539711](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/job/113988539711), [macOS default 113988539467](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/job/113988539467).

## Positive observations and calibration

The Linux default screen **explicitly flagged both reflection scenarios**. Reused-description medians were 5,099.7 -> 5,837.1 ns/op; fresh recurring-path descriptions 5,533.4 -> 6,318.0 ns/op. Their A/A median shifts were +0.9% and -0.5%, respectively. This is an unresolved screening result, not a successful all-regression verdict.

At the preceding `10c0759` checkpoint, [Linux default job 113985853518](https://github.com/wieslawsoltes/Avalonia/actions/runs/37979285920/job/113985853518) reported all 26 medians negative, including reused/fresh reflection -20.0%/-9.1%, inheritance -31.3% and native unique -28.0%. That is a separately identified checkpoint, not the final source result. The `10c0759..3d501ba` diff changes only BrowserRenderTimer, its host integration test file and its documentation; it changes no binding or native benchmark implementation. The reversal therefore cannot be attributed to an intervening binding edit. It also does **not** prove the slower current comparison is noise or that the original branch/baseline binding difference is harmless. Both outcomes are retained; no repeat-until-favorable procedure was used.

Other positive native observations remain visible:

- macOS non-tiered no-listener publication +45.6% (73.9 -> 107.6 ns/op), INPC +17.9%, context shaping +2.7% and compound selectors +4.2%.
- macOS default unique native glyphs +105.9% (1,026.3 -> 2,016.4 ns/op), local resource replacement +7.9% (70.3 -> 73.6 ns/op), INPC +8.7% and short text layout +2.0%.
- Linux default no-listener publication +0.8%.

The macOS screens were clear, but their calibration and pair distributions limit interpretation. Non-tiered A/A shifted INPC -17.6%, reused/fresh bindings about -18%, repeated shaping -24.0% and local writes +7.7%. Default A/A shifted no-listener -13.0%, repeated shaping +25.0%, retained blobs -31.8%, wheel/jump -14.6%/-12.7%, native unique -12.1% and wrapped layout -27.6%. A clear heuristic under mixed pairs is not evidence of equivalence. Conversely, the large unique-glyph positive delta cannot responsibly be called a confirmed universal runtime regression without source attribution. The current source is not declared free of regressions.

## Allocation and lifetime evidence

Removing the per-wrapper TwoLevelCache object saves **48 managed bytes per recreated/unique glyph operation** relative to the preceding branch. The first borrowed blob/options are inline; the three-alternative array is lazy. Shared owners, eviction rules, native blob lifetime and fonts are unchanged. This saving does not move the object into an unbounded external store.

| Scenario | Original baseline B/op | Previous branch B/op | Current B/op |
|---|---:|---:|---:|
| Recreated native glyphs, non-tiered | 912 | 152 | **104** |
| Recreated native glyphs, default | 968 | 152 | **104** |
| Unique native glyphs, non-tiered | 632 | 416 | **368** |
| Unique native glyphs, default | 688 | 416 | **368** |
| Retained native blob lookup | 96 | 0 | 0 |
| Cold/over-capacity/long/context shaping | 336 | 240 | 240 |
| Repeated shaping | 336 | about 255 | 255.0-255.5 |
| Mixed repeated/one-off shaping | 336 | 252 | 252 |
| Inheritance reparent | 184 | 64 | 64 |
| Reused reflection description | 1,777 | 1,649 | 1,649 |
| Fresh recurring-path reflection description | 1,913 | 1,793 | 1,793 |

The singleton fast path removes comparison pool operations rather than another managed allocation. Tests verify the rental is absent even while a callback executes and compare full traces with the general snapshot path. Complex inheritance still uses the exception-safe dictionary path.

Linux short layout is 2,241.2 -> 1,377.2 B/op, macOS 2,240 -> 1,376. Linux wrapped layout is 10,890.4 -> 8,658.4 B/op; macOS 7,568 -> 5,936. These are managed current-thread allocations, not native retention, whole-process heap size or a claim that every layout uses the fast paths.

## Raw native artifact references

The artifacts contain raw per-process JSON, calibration, reports, logs and exact-source provenance. Published archive SHA-256 values are retained here so later evidence can be checked rather than manually conflated with another run.

| Mode | Artifact | Published SHA-256 |
|---|---|---|
| Linux non-tiered | [11640058680](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/artifacts/11640058680) | `921df1900734e4c23c8ce9154b0696b99fa521dddf33306f672d903eba3328e7` |
| Linux default | [11640824202](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/artifacts/11640824202) | `30fadc46add5b9f45d511125d6856b5f1db9a432114231eadccbea6997df1c44` |
| macOS non-tiered | [11640508514](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/artifacts/11640508514) | `27bf3df56a99fc088b10debf4cbb0f779c9cdfd3c021fd41f7304e0be1ba0f17` |
| macOS default | [11640877574](https://github.com/wieslawsoltes/Avalonia/actions/runs/37980167927/artifacts/11640877574) | `22db9c7b6106a779e178c373f4835ed583f1516bb8b67ec3c39a2daf3a325a18` |

## Acceptance boundary

The recovered optimizations have concrete source changes, documented proofs and passing configured semantic/lifetime tests. Inheritance, native-wrapper allocation and unnecessary idle callbacks have measurable repairs. They do not justify asserting that every earlier or current positive timing is fixed. Binding flags, macOS mixed results and browser counterexamples remain part of acceptance. Native headless timing does not certify GPU presentation, browser frame rate or cold startup.

Read [the matching browser report](validation-demand-browser.md) for exact browser completion, all observed timings, idle counters, pixels and startup/size tradeoffs. Reproduction is in [measurement.md](measurement.md) and [browser-regression-diagnostics.md](browser-regression-diagnostics.md). Historical errata and unfavorable earlier reports remain intact. No upstream PR or merge was performed.
