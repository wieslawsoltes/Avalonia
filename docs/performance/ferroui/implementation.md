# Current implementation notes

Baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. Latest repaired runtime: `fa8bb8c926cf6d1271b23d6e89a06633051f56cf`. The [coverage matrix](README.md) distinguishes implementations, existing mechanisms and incompatible proposals. [Validation](validation-regression-repairs.md) records all latest results, including unresolved positive timings. Earlier reports preserve historical evidence rather than being relabeled as current.

## 02: Notification allocation and ownership

AvaloniaProperty lazily retains immutable name-only INPC arguments, used by AvaloniaObject when an INPC listener exists. Typed Avalonia arguments remain per notification because they carry sender-specific old/new values and are observable reentrantly. Virtual overrides, property handlers, instance handlers and INPC listeners retain their ordering.

LightweightObservableBase already avoided snapshot rentals for zero through three observers. Larger snapshots now return cleared arrays in finally, including throwing callbacks. Publication still follows the captured subscription order outside the lock; an exception still interrupts it. FerroUiNotificationTests covers reuse, reentrancy, subscription mutation and exception recovery. No callback-skipping registry is introduced.

## 03: Inheritance comparison pools

ValueStore.SetInheritanceParent resolves/comparisons ancestors before renting the dictionary. No-op ancestor returns no longer consume an unreleased rental; real changes return rentals in finally. Existing property-ID matching, identity checks, local-value pruning and property-major order remain. FerroUiInheritanceTests checks ownership, exceptions, exact order, pruning and detached defaults. Object-major batching is excluded because cross-object notification order is observable.

## 04: Live selectors and resource locations

Selector reuses immutable compound predecessor plans in original evaluation order, not match results or activators. A single non-container-query node returns its original match instead of building an AND around its only operand. Container queries, combinators and result-category conversions remain unchanged. TypeNameAndClassSelector reads its own constraint directly and calls Type.IsAssignableFrom. The earlier managed last-type memo was removed after a repeated default-runtime measurement exposed overhead. StyleKey remains a live read and inherited constraints preserve callback-sensitive repeated resolution. See [selector plans](selector-plans.md) and [dispatch repair](single-selector-regression.md).

ResourceDictionary caches weak supplying-dictionary locations or absence, never resource values. Found-null, non-shared deferred factories, custom providers and ancestor-host precedence remain live. Plain replacement retains a location and still notifies; structural changes advance the epoch before callbacks. Direct leaf probes and iterative last-merged tail calls make a full fallback cheaper without flattening a graph which callbacks may mutate. One inline string/theme entry avoids secondary-dictionary rewriting during revalidation and flushes dirty state before changing keys. The mirror remains within the original capacity.

A single-change shortcut requires a completed negative search, the same weak still-reachable dictionary/key, exactly one intervening epoch and a known plain insert/remove mutation. Normal winners are not assumed unique; deferred, unrelated, multi-epoch or graph changes still search. Opaque stored resource keys, arbitrary Type subclasses and opaque stored/requested/inherited theme keys disable location caching so arbitrary equality/hash callbacks cannot be frozen. All changes still invalidate/notify. Documentation: [lookup](resource-lookup.md), [live traversal](resource-traversal-regression.md), [inline locations](resource-inline-revalidation.md), [single-change proof](resource-single-change-proof.md), [standalone access](local-resource-regression.md), [theme safety](mutable-theme-key-safety.md).

## 05: Binding machinery

The general TemplateBindingExpression avoids redundant conversion for identical declared types without a converter. Compatible styled-property pairs use TypedTemplateBindingExpression<T> and IValueEntry<T>, removing boxing on normal publication through a generic property factory without runtime reflection. Direct/different/converter/sentinel/error-sensitive cases retain the original route. General-versus-typed traces verify clear, reparent, priority, reentrancy and two-way behavior. See [typed bindings](typed-template-bindings.md).

ReflectionBinding shares owned immutable grammar syntax per description and in a bounded cache for recurring paths. Observer nodes, converted index arguments, type resolution, name scopes and source selection remain fresh. Ownership prevents reentrant parsing from corrupting shared scratch syntax. See [reflection syntax](reflection-binding-path.md). Compiled paths already shared immutable descriptions; that is not new work.

## 06: Independent shaping and optional line metrics

HarfBuzzTextShaper caches immutable short-run glyph snapshots with complete conservative keys and bounded retention. Returned pooled arrays are independent. Whole immutable strings without features are eligible; slices preserve surrounding context and mutable/ineligible input uses normal shaping without consuming adaptive policy probes. Admission requires recent repeated use; hints expire on eligible bypasses as well as probes. Only actual hits reset miss backoff. Periodic bounded consecutive probes recover small cyclic sets otherwise missed by fixed-stride sampling. See [admission](shaping-admission.md), [expiry](shaping-admission-window.md), [recovery](bounded-cache-recovery.md) and [ineligible inputs](uncacheable-shaping.md).

Admitted snapshots own value-only default-line metrics. Returned buffers carry optional state on the existing glyph reference rather than fields on every ShapedBuffer. Cache-hit construction allocates that reference directly; admission upgrades only an admitted buffer. Clone/CloneAs drop view-local state, while the holder generation still detects sibling mutation. Fresh unpublished buffers initialize glyph info and parallel IDs without redundant per-glyph invalidation; all public writes retain generation/cluster invalidation. See [allocation repair](shaping-allocation-regression.md).

Metrics reuse accepts only unchanged single runs with built-in default paragraph/run properties and an opted-in renderer. Exposed mutable GlyphRuns, complex paragraphs and custom cases use the original formatter; width-dependent overflow is recomputed. Cached metrics come from the original algorithm, not approximations. The final shaper reads each native position once, retaining exact arithmetic order, tabs, offsets and clusters with fewer helper calls. See [metrics](default-line-metrics.md) and [repeat-driven refinement](single-selector-regression.md).

## 07: Native geometry, fonts and frame evidence

SharedGlyphRunData leases geometry, relative bounds and retained blobs across recreated layouts. Full glyph records verify hash candidates; typeface/size and normalized rendering/hinting/baseline settings determine reuse. Wrapper-local borrowed caches keep warm lookup cheap; other users or cache eviction cannot dispose a leased blob. First-blob storage is inline; the 18-state vector is lazy. Raw run views copy into native storage without managed builder-view wrappers. Admission/backoff is per font and includes expiry/cyclic recovery. See [shared glyphs](shared-native-glyphs.md), [native admission](native-glyph-admission-window.md) and [raw views](raw-native-blob-buffer.md).

Unique geometry uses a lazy configured-font pool with at most 16 retained fonts per used typeface. Each mutable SKFont is exclusively leased. Effective float size and normalized option state form the key; owner identity/simulations remain separate. Slot generations reject stale copied returns. Overflow is unretained. Owner disposal rejects new rentals, disposes idle fonts and defers active release until return; general CreateSKFont callers remain unpooled. Tests cover configurations, concurrency, capacity, ownership, raster equivalence and allocation. This trades bounded native retention for reduced construction/configuration, not free memory. See [font setup](native-font-setup-regression.md).

Existing dirty-subtree traversal and clipping remain unchanged. Exact native raster and browser light/dark/resource-change comparisons exercise output. Browser wheel/thumb/idle measurements retain diagnostics defaults and distinguish task CPU/RAF from GPU presentation.

## 01, 08, 09: Applicability and evidence

C# has no Rust macro forwarding chain; Cargo/allocator/hasher flags are not managed library settings. Browser interpreter/AOT/named builds are application experiments rather than forced defaults. Compile-time counters disappear from ordinary build types/storage/call sites and are exercised by a separate instrumented workload.

Native measurement uses unchanged 26-scenario harness sources on exact refs, A/A calibration and five alternating baseline/head pairs. Browser evidence uses real input, decoded pixels, visible resource restoration, readiness/task CPU/RAF/size data and separate wheel profiles. See [calibration](counters-and-calibration.md) and [browser evidence](browser-evidence.md). Wheel profiles are not profiles of slower thumb cases. Passing correctness and heuristic screens do not establish an all-workload or physical-GPU speedup; unfavorable runs/repeats and retention/deployment tradeoffs remain documented.
