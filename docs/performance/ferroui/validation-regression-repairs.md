# Regression repairs: fa8bb8c validation snapshot, 2026-10-09

## Revisions and outcome

Original baseline: **`a9429a328057befa287ffb5e981f58b86a86eda0`**. Repair series starts at branch head `ad6d8a2db8fa467e66e6fd3b3c532cefbcc8c0b3` (runtime `e8c4cb23d1f023fb0bd907ca01a23e7f4dc7af01`). This historical snapshot describes runtime/tests **`fa8bb8c926cf6d1271b23d6e89a06633051f56cf`**, not subsequent follow-up commits.

Nine runtime commits and 46 new tests were pushed on `perf/ferroui-runtime-optimizations` in [fork PR #8](https://github.com/wieslawsoltes/Avalonia/pull/8), targeting `wieslawsoltes/Avalonia:master`, not upstream. All configured correctness checks and report-generation jobs completed, including all three browser modes. The largest resource-mutation, cold-allocation and unique-native-glyph counterexamples improved. Positive native and browser observations still required investigation. The PR remained draft and unmerged.

**AOT transcription correction:** the first version of this document, published in `74828fb`, incorrectly attributed software list-thumb +21.0%, software tree-thumb +30.1%, default tree-wheel +34.4% and default tree-thumb +32.3%, as well as incorrect CPU medians/deployment sizes, to job `113828663614`. Those numbers do not match that job's log and are not valid evidence for the cited revision/run. The corrected AOT table and sizes below come from the actual cited job. This correction is not a runtime improvement and must not be presented as fixing those alleged slowdowns. The old document remains in Git history; the actual positive observations are retained below. Follow-up source work is described in [remaining-regression-followup.md](remaining-regression-followup.md).

## Repair commits

| Commit | Implementation and detailed documentation |
|---|---|
| `a124cba` | [Direct resource leaf probes, tail traversal and deferred slow-path separation](resource-traversal-regression.md) |
| `aac1fb5` | [Optional metrics metadata and unpublished-buffer initialization without redundant invalidation](shaping-allocation-regression.md) |
| `9396230` | [Inline weak resource location with bounded capacity and dirty-state flushing](resource-inline-revalidation.md) |
| `f6e406e` | [Exclusive configured-native-font leases, capacity and disposal](native-font-setup-regression.md) |
| `07c625b` | [Guarded single-change revalidation and live opaque-resource-key lookup](resource-single-change-proof.md); cancel superseded browser runs |
| `181bfbe` | [Direct single-selector evaluation with original activator/container/combinator behavior](single-selector-regression.md) |
| `b3c95fa` | [Cheap standalone resource reads/writes and callback-sensitive dependency checks](local-resource-regression.md) |
| `bb80607` | [Live lookup for mutable requested/inherited/stored theme keys](mutable-theme-key-safety.md) |
| `fa8bb8c` | [Repeat-driven removal of managed assignability memoization and per-glyph helper/span calls](single-selector-regression.md) |

The 46 cases cover allocation, snapshot independence, alias/ref-counted lifetime, deep and reentrant resource traversal, weak graph collection, precedence/null/deferred behavior, single-change proof counterexamples, concurrent exclusive font rentals, owner disposal, activators/combinators and arbitrary type/key/theme callbacks. Existing glyph/metrics/bounds/raster tests remain required.

**The original 26 native scenarios, harness source, operation counts, warm-up policy and screen thresholds were unchanged.** No counterexample was deleted or renamed. Browser input/comparison/deployment settings were unchanged; only superseded-run cancellation changed. Public application APIs and runtime/diagnostics defaults were preserved.

## Completed checks at fa8bb8c

[Managed run 37933178513](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178513): all six configured Release suites (Base, Controls, Markup, XAML, Skia, Themes) and affected .NET 8 builds passed. Existing skips and unrun platform/integration projects are not presented as executed tests.

[Counter run 37933178442](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178442/job/113828663034): normal/instrumented builds, absence tests and real-workload instrumentation passed.

[Native run 37933178554](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178554): all four OS/runtime combinations completed; each passed 12 Python validator tests and verified matching operation/checksum/container prepare-clear counts and reported environments within the comparison. Timing is report-only, not an all-workload speedup gate.

[Browser run 37933178593](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178593): interpreter, named-interpreter and AOT all completed successfully, including exact baseline/head publishing, real wheel/thumb input, resource-change/restoration, pixels, profiles and reports. Ordinary interpreter and AOT each reported zero exact screenshot mismatches.

## All native scenarios and modes

Each job ran five alternating fresh-process baseline/head pairs after five same-revision A/A pairs. Percentages are `100 * (median(head_time / base_time) - 1)`, not ratios of independent median ns/op values. Negative is faster. Results compare the entire PR to the original baseline, not isolated repair contributions.

Jobs used .NET 10.0.12 / SDK 10.0.401, Ubuntu 24.04.5 and macOS 26.6.2 arm64. Non-tiered JIT disables tiered compilation. Default mode removes known inherited tiering/PGO/ReadyToRun overrides but permits tier transitions; it is not a stabilized steady-state measurement. Compare within columns rather than absolute speeds between hosts/modes.

| Scenario | Linux non-tiered | Linux default | macOS non-tiered | macOS default |
|---|---:|---:|---:|---:|
| property-no-listener | -4.0% | -1.2% | -6.6% | +0.9% |
| property-inpc | -15.3% | -3.6% | -25.4% | -1.1% |
| inheritance-reparent | -3.1% | -5.0% | -2.5% | +2.5% |
| template-identity | -26.4% | -10.0% | -35.3% | -3.6% |
| reflection-description-reused | -27.8% | -10.9% | -13.9% | -10.8% |
| reflection-description-once | -2.6% | -4.0% | -24.7% | -13.2% |
| style-type-miss | -1.4% | +26.6% | +1.3% | -5.4% |
| shape-repeated-short | -65.9% | -51.2% | -81.3% | -67.5% |
| shape-unique-short | -4.3% | -16.8% | -5.1% | -7.0% |
| shape-one-pass-short | -4.9% | -16.6% | -20.3% | -6.2% |
| shape-hot-with-one-off-scan | -47.1% | -36.7% | -71.2% | -46.5% |
| shape-long-uncacheable | -8.3% | -3.7% | -9.8% | -4.2% |
| shape-context-slice | -1.1% | +3.2% | +3.3% | -1.4% |
| skia-retained-text-blob | -31.6% | -19.7% | -48.1% | -9.9% |
| text-layout-short | -43.3% | -15.2% | -68.7% | -13.7% |
| listbox-wheel-offset | -5.3% | -3.4% | -19.7% | -0.7% |
| listbox-viewport-jump | -6.7% | -4.1% | +4.2% | -37.9% |
| resource-deep-hit | -96.9% | -53.7% | -97.8% | -48.5% |
| resource-deep-miss | -99.1% | -99.0% | -99.7% | -98.4% |
| resource-deep-mutation | -91.9% | -85.9% | -95.3% | -83.9% |
| resource-local-replacement | +0.4% | +11.6% | -10.4% | +4.1% |
| resource-deep-insert-remove | -93.3% | -88.0% | -96.6% | -83.3% |
| selector-compound-live | -3.1% | -39.5% | -5.6% | -38.0% |
| native-glyph-recreated | -70.7% | -78.3% | -72.6% | -63.5% |
| native-glyph-unique | -39.4% | -18.9% | -50.4% | -18.0% |
| text-layout-wrap-fallback | -23.3% | -24.3% | -37.5% | -39.7% |

Exact logs: [Linux non-tiered 113828662738](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178554/job/113828662738), [Linux default 113828662502](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178554/job/113828662502), [macOS non-tiered 113828662851](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178554/job/113828662851), [macOS default 113828662871](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178554/job/113828662871).

The unchanged screen reported no consistent regression outside the observed A/A envelope in any of these four jobs. It requires the fastest candidate pair to exceed that envelope plus the existing margin; it is not a confidence interval, and high A/A variation reduces sensitivity. **Linux default selector misses remained +26.6%, local replacement +11.6%, and context slices +3.2%; macOS non-tiered slices +3.3%.** A clear heuristic screen did not resolve these positive observations.

## Allocation and retention

Cold one-pass, over-capacity, long and sliced shaping used **336 B/op**, matching the original baseline rather than previous 352. Cached hits carried optional metadata, approximately 351-352 B/op versus baseline 336; mixed input about 348. The unconditional field cost was removed, not moved to an unbounded table.

Unique native glyphs used **416 B/op**, versus previous 624 and original 632 non-tiered / 688 default. Recreated runs used 152 versus baseline 912 / 968. The native-font pool retains at most 16 configured fonts per used typeface under exclusive leases. Current-thread managed B/op excludes that native retention and other-thread/native allocations.

Other counts: INPC 88 -> 64; reparent 184 -> 64; same-type template 216 -> 128; reused reflection 1,777 -> 1,649; new recurring-path reflection 1,913 -> 1,793; retained blob 96 -> 0; resource mutation 24 -> 24 B/op. Linux short/wrapped layout was 2,241.2 -> 1,473.2 and 10,890.4 -> 9,442.4; macOS 2,240 -> 1,472 and 7,568 -> 6,464.

## Repeat evidence and corrected browser results

[The intermediate checkpoint](validation-regression-checkpoint.md) preserves planned default-runtime repeats at `bb80607`. Despite an initially clear screen, the Linux repeat flagged selector misses (+45.0%) and context shaping (+19.5%); macOS flagged none. Those results prompted the ninth source commit, not threshold changes or reruns until favorable. Copied successful jobs with new rerun IDs are not counted as fresh measurements.

[Interpreter job 113828663494](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178593/job/113828663494) and [AOT job 113828663614](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178593/job/113828663614) used Chromium 141.0.7390.37, three alternating pairs per render mode, with zero screenshot mismatches each. [Named-interpreter 113828663186](https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178593/job/113828663186) also passed; its timing is not inferred from the other modes.

| Browser task-CPU scenario | Interpreter Software2D | Interpreter default/WebGL | AOT Software2D | AOT default/WebGL |
|---|---:|---:|---:|---:|
| list-wheel | +1.0% | -1.8% | -7.4% | -5.3% |
| list-thumb | -0.7% | -5.6% | -8.7% | +2.4% |
| tree-wheel | +2.0% | -0.3% | -5.1% | -11.3% |
| tree-thumb | +9.1% | -4.3% | +0.6% | -8.0% |
| idle-overlay-off | +8.9% | +6.3% | -14.7% | -13.2% |
| idle-overlay-on | +21.7% | -7.4% | -10.8% | +8.5% |

AOT independent software list-thumb medians were **614.58 -> 561.19 ms**, software tree-thumb **103.68 -> 99.52 ms**, default tree-wheel **188.59 -> 166.04 ms**, default tree-thumb **114.02 -> 104.87 ms**. Actual positive AOT observations include default list-thumb **652.23 -> 655.13 ms** (+2.4% paired) and overlay-on idle **20.83 -> 18.99 ms** (+8.5% paired). Paired percentages are not ratios of independent medians; mixed pairs can give opposite signs. These are report-only observations, not controlled causal attribution.

Interpreter software idle medians were 14.46 -> 16.98 ms (off) and 15.57 -> 17.65 ms (on) over three seconds. AOT default idle medians were 22.63 -> 18.38 ms (off) and 20.83 -> 18.99 ms (on). Both revisions recorded 180 RAF callbacks in each idle mode. RAF callbacks are not draws/physical presentation; task CPU is not GPU time. The original exact-build profiles cover wheel activity, not the thumb/idle cases. The additive [browser regression diagnostics](browser-regression-diagnostics.md) address that evidence gap for follow-up revisions.

Interpreter deployment was **18,792,178 -> 18,826,482** raw bytes and estimated per-file gzip **6,685,614 -> 6,700,614**; first-ready changes +2.3% software and -0.3% default. AOT deployment was **39,434,843 -> 39,620,575** raw and **12,260,433 -> 12,325,888** gzip; first-ready -0.2% software and -8.0% default. Readiness is a fixture ready-after-RAF signal, not hardware presentation. AOT used the explicit build flag/fixture setting; this does not prove every method runs without fallback or that AOT is universally faster.

## Acceptance at this snapshot

The configured managed/native/counter/browser checks completed at `fa8bb8c`. Major mutation, cold-allocation and unique-native-font counterexamples improved across the four native comparisons. **This snapshot did not establish that all timing regressions were resolved.** Corrected source/run attribution also means the originally transcribed large AOT regressions cannot be claimed as repaired later. Positive native and browser observations required controlled follow-up, with all raw pairs retained.

No native benchmark threshold was weakened, case suppressed, required callback/invalidation skipped or NuGet audit disabled. Reproduction: [measurement.md](measurement.md), [browser-evidence.md](browser-evidence.md).
