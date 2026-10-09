# Implementation notes

Avalonia baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. Source: [FerroUI's nine performance designs](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance). These are C# implementations and source audits, not a claim that Rust profile percentages apply to Avalonia. The [design matrix](README.md) separates implemented work, existing mechanisms, rejected changes and remaining work.

## 02. Notification allocation and observer ownership

`AvaloniaProperty` lazily retains one immutable `PropertyChangedEventArgs` containing its name. `AvaloniaObject.RaisePropertyChanged<T>` uses it only when an INPC listener exists. Previously each such effective change allocated another name-only argument. Sender-specific typed Avalonia arguments are still created per change: they carry old/new values and cannot safely be shared across reentrant notifications. The virtual override, property handlers, Avalonia instance handlers and INPC handlers retain their original order. Neither subclass callbacks nor effective changes are suppressed.

The existing `LightweightObservableBase` already avoids pooled snapshots for zero through three listeners. For larger snapshots, its array is now returned with references cleared in `finally`, including when an observer throws. Callbacks still use the subscription-order snapshot outside the lock, and an exception still interrupts that publication.

`FerroUiNotificationTests` covers sender/name argument reuse, reentrant old/new values and ordering, subscription mutation at 1/2/3/8 observers, and recovery after a throwing observer. The expected allocation saving is one name-only argument per warm INPC notification; elapsed-time benefit is measured separately.

## 03. Inheritance pool ownership without reordered notifications

`ValueStore.SetInheritanceParent` resolves and compares inheritance ancestors before renting its comparison dictionary. The previous unchanged-ancestor return consumed a pool rental without returning it. After a real ancestor change the dictionary is now returned in `finally`, even if an inheritance callback throws.

Old/new traversal, the specialized property-ID dictionary, effective-value identity checks, local-value subtree pruning and property-major notification ordering are unchanged. Detach and reattach remain separate observable operations. The proposed object-major traversal is deliberately not used because it changes cross-object event ordering.

`FerroUiInheritanceTests` checks pool ownership on no-op and throwing paths, exact property-major ordering, local pruning and the detached default value. The tests inspect the private pool without adding a production API.

## 04. Immutable selector type-constraint caching

`TypeNameAndClassSelector` caches the last `IsAssignableFrom` result for its own immutable type constraint and the actual `StyleKey` read from the current target. It does not cache class/name activation or a complete selector result. Exact-type comparisons retain their simple comparison path. Constraints inherited from a previous selector retain their existing evaluation because that previous selector can involve mutable alternatives or nesting.

The cache contains only one type/result pair per selector, so it cannot grow with the number of control types. StyleKey is still read at its original point. Changing it, classes, selector alternatives, resources, parents or theme variants is not hidden by a cached whole-selector match.

`FerroUiSelectorTests` covers a changing StyleKey, read count, class changes, mutable Or alternatives and selector replacement. This is not a new per-host theme cache or complete selector index. Existing per-element implicit-theme caching remains in `StyledElement.GetEffectiveTheme`; resource precedence/invalidation has not been rewritten.

## 05. Binding publication and construction

`TemplateBindingExpression.PublishValue` skips the general target converter only for a source property whose declared value type equals the target type and has no converter. Parameterless template bindings, differing types, user converters and the UnsetValue sentinel retain the general path. Publication, errors, priority, disposal, templated-parent subscription changes and two-way writes use the original expression and value store.

`FerroUiTemplateBindingTests` covers clear/detach/reattach publication, same-type custom converters, differing-type conversion and two-way writeback. The object-valued binding sink remains; this is not an entirely unboxed pipeline.

`ReflectionBinding.CreateInstance` now separates owned syntax from per-target evaluation state. One snapshot is reused on the description; a bounded pure-syntax cache also serves new descriptions with recurring paths. Fresh observer nodes, current type resolution, current name scopes and source selection are still built per instance. An owned snapshot also prevents reentrant type-resolution callbacks from overwriting the grammar's shared scratch list. [Detailed bounds, tradeoffs and tests](reflection-binding-path.md) include the initial first-use allocation regression and its follow-up.

## 06. Bounded immutable short-run shaping cache

`HarfBuzzTextShaper` owns a bounded LRU of completed glyph-data snapshots. An eligible key contains the full string, a unique scalar HarfBuzz typeface identity, size, bidi level, effective culture name/LCID, tab width and letter spacing. No native font or GlyphTypeface object is stored in an entry. Disposed typefaces are rejected before cache lookup. A hit creates an independent pooled `ShapedBuffer` and copies glyph indices, clusters, advances and offsets. Mutations, splitting, bidi processing and disposal cannot corrupt another layout's snapshot.

Eligibility is conservative: at most 128 characters, a whole immutable string, no explicit font features. Substrings retain original surrounding context and are never cached by content alone. Mutable arrays, memory managers and custom feature lists use the original path. Results over 256 glyphs are not admitted. Capacity is limited to 256 entries and a 256 KiB conservative retained-data allowance, including the fixed 8 KiB admission table; LRU evicts completed entries. This is an accounting allowance, not an exact total-heap-size measurement.

[Two-touch admission](shaping-admission.md) avoids allocating snapshots for ordinary one-off strings and protects hot entries against scans. Admission fingerprints are only hints; full keys always determine glyph retrieval. Fingerprint collisions may admit an extra entry but cannot return another key's glyphs. Locks protect metadata, shaping stays outside the cache lock, and immutable arrays survive concurrent eviction.

`ShapedRunCacheTests` compares cached output exactly with the uncached mutable-memory path for Latin, combining marks, tabs, breaks and RTL; covers options, typefaces, cultures, independent mutation/disposal, excluded inputs, LRU/bounds, concurrent reads and cold-admission allocations. `FerroUiGlyphTypefaceLifetimeTests` now warms admission and verifies disposal against a populated cache. Whole-line formatting is not bypassed: paragraph metrics, wrapping, trimming and hit testing retain their algorithms. Native cross-layout glyph sharing and a line-metrics shortcut remain unimplemented.

## 07. Allocation-free retained Skia text-blob lookup

`TwoLevelCache` gains an internal stateful factory overload. `GlyphRunImpl.GetTextBlob` passes its owner as state to a static factory instead of creating a captured closure before every lookup, including hits. Font/blob construction remains on misses, and the text-blob builder rental is returned in `finally`. Existing TextOptions normalization, including edging, primary/secondary cache policy and eviction disposal remain intact.

`TwoLevelCacheStateTests` covers both hit levels, exact factory state, no factory invocation on hits, eviction/disposal, throwing factories and zero managed allocation in warmed lookups. The cache already retained SKTextBlob instances. Composition already has dirty-subgraph traversal and dirty-rectangle clipping; those correctness-sensitive algorithms are unchanged.

## 01, 08 and 09. Runtime-specific boundaries and evidence

C# virtual dispatch does not contain the Rust macro forwarding chains in design 01. No callback-skipping bitset is introduced. Typed lazy event values and small-observer fast paths were already present in Avalonia.

Cargo optimization levels, Emscripten allocator settings and Rust hashers are not Avalonia build settings. [Native measurement modes](measurement.md) distinguish non-tiered JIT from normal runtime configuration on Linux/macOS, without changing application deployment policy. The harness checks logical output and managed allocations in 17 scenarios; Python tests validate the comparison reports themselves. Temporary offline SDK-packaging scaffolding has been removed; the reproducible validation and measurement workflows remain.

No browser AOT, SIMD or diagnostics-overlay default is changed without target-specific evidence. Browser/GPU output, cold startup, native screenshot parity and cross-layout glyph sharing must not be inferred from managed unit tests or hot-loop measurements. This branch is not presented as complete implementation of every proposal in the nine documents.
