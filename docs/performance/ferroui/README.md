# FerroUI performance designs applied to Avalonia

Branch: `perf/ferroui-runtime-optimizations`. [PR #8](https://github.com/wieslawsoltes/Avalonia/pull/8) targets **wieslawsoltes/Avalonia:master**, not upstream. Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`.

The [source study](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance) mixes transferable ideas, existing Avalonia machinery, Rust-specific mechanisms and hypotheses. Its percentages are not Avalonia forecasts. [Source applicability](source-applicability.md) pins the reviewed revision and distinguishes FerroUI's statement-for-statement port rule from the behavioral-equivalence contract for new Avalonia optimizations.

## Design coverage

| Design | Implementation | Compatibility boundary |
|---|---|---|
| 01 Virtual dispatch | Audit: C# has no Rust macro forwarding chain. | Keep extensible virtual callbacks; no closed-world registry or blanket inlining policy. |
| 02 Notifications | Name-only INPC argument reuse; exception-safe listener snapshots; direct resource-owner publication. | Typed old/new values, listener order, reentrancy, owner replacement and exceptions remain live. |
| 03 Inheritance | Avoid no-op rentals and release comparison dictionaries on exceptions. | Keep property-major notification order and existing subtree pruning. |
| 04 Styles/resources | Immutable selector plans, small single-node dispatch; weak resource locations, cheaper live traversal, guarded single-change revalidation and small local read/write paths. | StyleKey, classes, parents, container queries, arbitrary type/key/theme/provider callbacks remain live; structural changes still invalidate. |
| 05 Bindings | Typed compatible styled template publication; bounded reflection-path syntax sharing. | Converters, direct/different/sentinel-sensitive types retain the general route; observers and scopes are per target. |
| 06 Text layout | Independent shaped snapshots, expiring admission and cyclic recovery; optional generation-checked metrics; paired glyph-array ownership. | Both arrays live until the last alias releases them. Public writes invalidate aliases; context-sensitive and complex text retains normal shaping/formatting. |
| 07 Native/frame cost | Leased shared geometry/blobs, raw native views and exclusively leased configured fonts. Existing compositor dirty-region algorithms retained. | No concurrent sharing of mutable fonts or premature disposal of borrowed blobs; RAF is not physical presentation. |
| 08 Build settings | Interpreter/AOT/named browser experiments; native default/non-tiered JIT measurement. | Application experiments, not changed deployment defaults. Rust flags do not apply. |
| 09 Measurement | Compile-time counters, unchanged 26-scenario native comparisons and calibration; real browser input/pixels/startup/idle/size; separate wheel/thumb/idle profiles with timestamp-order handling. | Preserve counterexamples, raw profiles and unfavorable results; report-only success is not universal performance acceptance. |

The managed last-type assignability memo was removed after repeat measurements exposed overhead. Current selectors use direct runtime assignability while retaining reduced single-node dispatch. Whole-host match caching or arbitrary mutable/custom-renderer line reuse would bypass observable behavior and is not substituted for safe reuse beneath those APIs.

## Implementation and regression repairs

[Implementation notes](implementation.md) summarize current behavior. Detailed designs: [typed template bindings](typed-template-bindings.md), [reflection syntax](reflection-binding-path.md), [resource lookup](resource-lookup.md), [selector plans](selector-plans.md), [default line metrics](default-line-metrics.md), and [shared native glyphs](shared-native-glyphs.md).

| Repair | Design, invariants and tests |
|---|---|
| Resource mutation | [Live traversal](resource-traversal-regression.md), [inline locations](resource-inline-revalidation.md), [single-change proof](resource-single-change-proof.md), [local access](local-resource-regression.md) |
| Local resource dispatch | [Local reads](local-resource-dispatch.md), [guarded string writes](resource-indexer-dispatch.md), [write and owner-notification overhead](local-resource-write-overhead.md) |
| Cold/context shaping | [Optional metadata](shaping-allocation-regression.md), [paired glyph storage](paired-glyph-storage.md), [cache/shaper dispatch](shaping-cache-dispatch.md), [ineligible inputs](uncacheable-shaping.md), [expiry](shaping-admission-window.md), [recovery](bounded-cache-recovery.md) |
| Unique native glyphs | [Configured-font leases](native-font-setup-regression.md), [raw native views](raw-native-blob-buffer.md), [native admission](native-glyph-admission-window.md) |
| Selector misses | [Single-node dispatch and removal of redundant memoization](single-selector-regression.md) |
| Custom callback safety | [Opaque resource/type keys](resource-single-change-proof.md), [mutable theme keys](mutable-theme-key-safety.md) |
| Browser diagnostics | [Exact-build calibration and scenario profiles](browser-regression-diagnostics.md), [signed timestamp-delta repair](profile-timestamp-repair.md) |

## Validation history and attribution

The [paired-storage and dispatch validation](validation-paired-storage.md) records the latest runtime and diagnostic checks. [The preceding follow-up](remaining-regression-followup.md) records the dispatch work and corrects historical AOT attribution. The earlier claims of +21.0% AOT software list-thumb, +30.1% software tree-thumb, +34.4% default tree-wheel and +32.3% default tree-thumb were not readings from the cited `fa8bb8c` job. The erratum is a correction, not a performance gain produced by a later commit.

[The earlier repair checkpoint](validation-regression-repairs.md), [intermediate repeats](validation-regression-checkpoint.md), [earlier cold paths](validation-cold-paths.md) and [initial results](validation.md) remain historical. Their descriptions must not override explicit later repairs or their own errata. New source revisions are not validated by earlier successful jobs.

## Reproduction and acceptance

See [native reproduction](measurement.md), [counters/calibration](counters-and-calibration.md), [browser validation](browser-validation.md) and [browser evidence](browser-evidence.md). The original 26 native benchmark scenarios, operation counts, warm-up and screen thresholds are retained. Browser A/B input and comparison settings are unchanged; same-build A/A and separately timed profiles are additional diagnostics. Superseded runs are cancelled in favor of the latest revision.

Read the exact-revision results and remaining uncertainty in [validation-paired-storage.md](validation-paired-storage.md). Correctness, allocation reduction, a clear heuristic screen and physical-GPU performance are different claims. No universal speedup, all-platform screenshot certification or zero-cost native retention is implied. Public application APIs and deployment/diagnostics defaults are intentionally unchanged. The pull request remains in the fork and is not automatically merged.
