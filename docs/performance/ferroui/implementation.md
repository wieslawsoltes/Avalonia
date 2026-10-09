# Implementation notes

Original Avalonia baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. Source: [FerroUI's nine performance designs](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance). The [coverage matrix](README.md) is the current disposition; [validation.md](validation.md) records an earlier checkpoint, not the final remaining-scope implementation.

## 02. Property notification allocation and observer ownership

`AvaloniaProperty` lazily retains one immutable `PropertyChangedEventArgs` containing its name. `AvaloniaObject.RaisePropertyChanged<T>` uses it when an INPC listener exists, avoiding a new name-only argument per effective change. Typed Avalonia arguments are still per change because they carry sender-specific old/new values and may be observed reentrantly. Virtual overrides, property handlers, instance handlers and INPC handlers keep their original ordering.

The existing `LightweightObservableBase` already avoids pooled snapshots for zero through three listeners. Larger snapshots are now returned with references cleared in `finally`, including throwing observers. Publication still uses a subscription-order snapshot outside the lock; an exception still interrupts that publication.

`FerroUiNotificationTests` covers argument reuse, reentrant old/new values and ordering, mutation with 1/2/3/8 observers, and throwing-observer recovery. No listener-skipping bitset or dense-ID rewrite is substituted for extensible callbacks.

## 03. Inheritance pool ownership

`ValueStore.SetInheritanceParent` resolves and compares ancestors before renting the comparison dictionary. The previous unchanged-ancestor return consumed a pool rental without releasing it. Real changes now return the dictionary in `finally` even if a callback throws.

Old/new traversal, specialized property-ID matching, effective-value identity checks, local-value subtree pruning and property-major notification ordering remain. Detach and reattach are separate observable operations. `FerroUiInheritanceTests` verifies rental ownership, exception paths, exact ordering, local pruning and detached default values. Object-major batching was rejected because it changes cross-object callback order.

## 04. Styling and resource machinery

`TypeNameAndClassSelector` caches only its own immutable type constraint's last assignability result against the actual StyleKey read from each target. Dynamic classes/names, mutable Or alternatives and inherited constraints retain their original evaluation. `Selector` additionally reuses an immutable left-to-right evaluation plan for compound predecessor chains, avoiding repeated reverse recursion without caching matches or activators. Simple selectors allocate no plan. See [selector-plans.md](selector-plans.md).

`ResourceDictionary` memoizes the supplying dictionary or an absence result through ordinary merged/theme graphs. It never caches the materialized resource value, preserving non-shared deferred factories and found-null semantics. Epoch invalidation precedes mutation callbacks, including unowned graph updates, and cached location references are weak. Custom providers and live ancestor-host lookup remain observable. See [resource-lookup.md](resource-lookup.md).

## 05. Binding construction and publication

The initial general `TemplateBindingExpression` avoids redundant target conversion for identical declared property types without a converter. The completed fast path uses `TypedTemplateBindingExpression<T>` and `IValueEntry<T>` directly for compatible styled-property pairs, removing value boxing from normal source-to-target publication. A generic property factory creates it without reflection or runtime generic construction. General converter/error/direct/sentinel-sensitive cases still use the original expression. Exact general-versus-typed event traces verify priority, reentrancy, null, clear, reparenting and two-way behavior; see [typed-template-bindings.md](typed-template-bindings.md).

`ReflectionBinding` owns/reuses immutable grammar syntax per description and through a bounded shared cache for recurring paths. Per-target expression nodes, type resolution, name scopes, source selection and indexer conversions remain fresh. Owned syntax prevents a reentrant type-resolution callback from overwriting parser scratch state. See [reflection-binding-path.md](reflection-binding-path.md). Compiled bindings already shared their immutable path; that existing mechanism is not claimed as new.

## 06. Shaping and line metrics

`HarfBuzzTextShaper` caches immutable short-run glyph records with conservative complete keys and bounded ownership. Returned pooled buffers are independent. Whole immutable strings without explicit features are eligible; surrounding-context slices and mutable inputs keep normal shaping. Two-touch admission avoids ordinary one-off snapshot allocation, and adaptive probing avoids repeated hashing/locking during sustained misses. Current limits and history are in [shaping-admission.md](shaping-admission.md).

Admitted snapshots also own a small, value-only default line-metrics context. It is consumed only by a fresh unchanged single shaped run with built-in default paragraph/run properties and an opted-in renderer. Shared holder generations reject mutated aliases; exposed mutable GlyphRuns and custom/complex cases bypass it. Width-dependent overflow is recomputed. The original formatter supplies cached metrics, bounds and ink bounds, not an approximation. See [default-line-metrics.md](default-line-metrics.md).

## 07. Native geometry and composed-frame work

`SharedGlyphRunData` leases immutable geometry, relative native bounds and retained Skia blobs across recreated layouts. Full glyph records verify hash candidates; font identity, exact size and effective font state are part of reuse. Wrapper-local borrowed caches keep warmed lookup cheap, while reference ownership prevents another layout or cache eviction from disposing a live blob. One blob is stored inline; the 18-state vector is allocated only on a second effective font state. Per-font probe backoff reduces cold-run overhead. See [shared-native-glyphs.md](shared-native-glyphs.md) and the measured counterexample in [cold-native-runs.md](cold-native-runs.md).

Existing composition dirty-subtree traversal, clipping and native text retention were audited, not unnecessarily rewritten. Exact Skia PNG comparisons and real browser light/dark/resource-change screenshots test output. The browser harness measures wheel/drag work and idle activity with the overlay both off and on; it does not change the catalog's diagnostics default.

## 01, 08 and 09. Platform-specific interpretation and evidence

C# does not contain FerroUI's generated Rust forwarding chain. Cargo optimization flags, Emscripten allocators and Rust hashers are not .NET library settings. Browser interpreter/AOT/named deployments are built and measured as application experiments instead of forcing unmeasured framework defaults.

Compile-time instrumentation has no type/storage/call sites in normal builds; an instrumented real-workload harness verifies nonzero activity for implemented paths. Native comparison uses identical harness sources on exact refs, same-head A/A calibration, five baseline/head process pairs and 24 scenarios. Browser evidence includes actual input, decoded PNG parity, dynamic resource restoration, startup/task CPU/RAF/size metadata and separate profiles from the exact build. See [counters-and-calibration.md](counters-and-calibration.md) and [browser-evidence.md](browser-evidence.md).

A complete applicable implementation is not a claim that every workload improves or every physical GPU/platform has been validated. Preserve measured regressions and footprint tradeoffs in the result record. Never infer current performance from the earlier Rust profile or earlier branch revisions.
