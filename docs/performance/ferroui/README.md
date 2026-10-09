# FerroUI performance designs applied to Avalonia

All implementation is on `perf/ferroui-runtime-optimizations` in [fork PR #8](https://github.com/wieslawsoltes/Avalonia/pull/8), targeting **wieslawsoltes/Avalonia:master**, not upstream. Original baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`.

The [source study](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance) contains nine designs, several explicitly conditional on preserving observable behavior or establishing a measured benefit. Its Rust/WebAssembly percentages are not Avalonia forecasts. The applicable mechanisms below are implemented; existing Avalonia mechanisms and incompatible proposals are distinguished rather than relabeled as new optimizations.

## Design coverage

| Design | Avalonia implementation | Compatibility boundary |
| --- | --- | --- |
| 01 Virtual dispatch | Audited managed dispatch; no Rust macro forwarding chain exists. | Keep extensible virtual callbacks. No closed-world override registry or forced inline policy. |
| 02 Notifications | Immutable name-only INPC argument reuse; exception-safe larger listener snapshots. Typed old/new values and small-listener paths already existed. | Keep sender-specific values, subscription order and reentrancy; do not suppress callbacks. |
| 03 Inheritance | Avoid unchanged-ancestor pool rental; return comparison dictionaries on exceptions. Existing indexed matching, identity checks and local-value pruning remain. | Do not reorder property-major cross-object notifications into object-major batches. |
| 04 Attachment, styles and resources | Shared immutable selector evaluation plans, own-type assignability cache, bounded merged/theme resource-resolution location caches with mutation-safe invalidation and location-preserving plain replacements. | Live selector/class/name/StyleKey/container/parent evaluation; dynamic custom resource providers remain probed. |
| 05 Recycling bindings | Unboxed same-type styled template-binding route; bounded immutable reflection-path sharing; dynamic resources use dictionary-resolution caches. Compiled binding paths already share immutable descriptions. | Converters, direct/differently typed/sentinel-capable properties and validation-sensitive cases keep the general path. Observers and subscriptions are per instance. |
| 06 Text layout | Independent-buffer shaping snapshots, expiring scan-aware admission/probing, generation-checked default single-run line metrics, shared native glyph geometry. | Context-sensitive text, mutable/exposed runs, complex paragraphs and custom backends take their existing algorithms. |
| 07 Composed frames | Leased cross-layout Skia blobs and native bounds, inline first-blob storage, per-font cold-probe backoff, real raster/browser parity and idle profiling. Existing dirty-subtree/dirty-rectangle algorithms stay intact. | Borrowed blobs remain alive across option changes and eviction. Do not change diagnostics defaults or equate RAF callbacks with GPU draws. |
| 08 Build settings | Actual interpreter, AOT and symbol-retaining browser build/measurement modes; native default/non-tiered JIT comparisons. | App deployment experiments, not blanket library defaults. Cargo/Emscripten/Rust-hasher changes do not apply. |
| 09 Measurement | Compile-time counters with absence tests and real-workload evidence; 26 native scenarios; same-head calibration plus baseline/head pairs; real wheel/drag/browser pixel tests, profiles and size/startup/idle reports. | Instrumented timing is not compared with uninstrumented timing. Optional calibrated regression screening is a heuristic, not a confidence interval. |

The substitutions are intentional: a whole-host cached selector result cannot safely skip mutable StyleKey/Or/container behavior, and a whole-line reuse shortcut cannot safely accept arbitrary mutable paragraphs or third-party renderer behavior. Reuse the immutable work underneath those APIs, not the observable callbacks themselves.

## Implementation documentation

[Core implementation](implementation.md) covers notifications, inheritance and the initial changes. [Typed template bindings](typed-template-bindings.md), [reflection syntax sharing](reflection-binding-path.md), [resource lookup](resource-lookup.md), [selector plans](selector-plans.md), [shaping admission](shaping-admission.md), [default line metrics](default-line-metrics.md), [shared native glyphs](shared-native-glyphs.md) and [cold-run refinement](cold-native-runs.md) document individual algorithms, ownership, invalidation, tests and tradeoffs.

[Expiring admission hints](shaping-admission-window.md), [weak-location reuse](resource-mutation-refinement.md) and [location-preserving replacement](resource-location-preserving-replacement.md) document refinements driven by measured cold/mutation-heavy regressions, with their new correctness and allocation tests.

[Native reproduction](measurement.md), [counters/calibration](counters-and-calibration.md), [browser validation](browser-validation.md) and [browser evidence/profiling](browser-evidence.md) document measurement. [The earlier validation report](validation.md) preserves historical results before the remaining-scope implementation; do not use its numbers as the current implementation's performance.

## Acceptance and interpretation

The fork-local workflows validate six Release suites, affected .NET 8 libraries, counter-off/on builds, native Linux/macOS comparisons and browser interpreter/AOT/named deployments. Read the result for the exact tested revision; a workflow definition or an older green run is not validation of newer source.

No universal speedup, hardware GPU frame rate, platform-wide screenshot certification or byte-identical complete binary is claimed. Cache metadata and synchronization have costs on unique/mutation-heavy workloads; report those alongside hits. Public application API and deployment/diagnostics defaults are intentionally unchanged. No merge is performed by the validation workflows.
