# Implementation notes

Avalonia baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. The source study is [FerroUI's nine performance designs](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance). These are C# implementations and source audits, not a claim that Rust profile percentages apply to Avalonia.

## 02. Notification allocation and observer ownership

`AvaloniaProperty` lazily retains one immutable `PropertyChangedEventArgs` containing its name. `AvaloniaObject.RaisePropertyChanged<T>` uses it only when an INPC listener exists. Previously each such effective change allocated another name-only argument. Sender-specific typed Avalonia arguments are still created per change: they carry old/new values and cannot safely be shared across reentrant notifications. The virtual override, property handlers, Avalonia instance handlers and INPC handlers retain their original order. Neither subclass callbacks nor effective changes are suppressed.

The existing `LightweightObservableBase` already avoids pooled snapshots for zero through three listeners. For larger snapshots, its array is now returned with references cleared in a `finally` block, including when an observer throws. Callbacks still use the subscription-order snapshot, outside the lock, and an exception still interrupts that publication.

Tests: `FerroUiNotificationTests` covers sender/name argument reuse, reentrant old/new values and ordering, subscription mutation at 1/2/3/8 observers, and recovery after a throwing observer. The allocation saving is one name-only argument per warm INPC notification; elapsed-time benefit is not assumed.

## 03. Inheritance pool ownership without reordered notifications

`ValueStore.SetInheritanceParent` resolves and compares the inheritance ancestors before renting its comparison dictionary. The previous unchanged-ancestor return consumed an existing pool rental without returning it. After a real ancestor change the dictionary is now returned in `finally`, even if an inheritance callback throws.

Old/new ancestor traversal, the specialized property-ID dictionary, effective-value identity checks, local-value subtree pruning and property-major notification ordering are unchanged. Detach and reattach remain separate, observable operations. The proposed object-major traversal is deliberately not used: it changes cross-object event ordering.

Tests: `FerroUiInheritanceTests` checks pool ownership on no-op and throwing paths, exact property-major ordering, local pruning and the detached default value. The tests inspect the private pool without adding a production API.

## 04. Immutable selector type-constraint caching

`TypeNameAndClassSelector` caches the last `IsAssignableFrom` result for its own immutable type constraint and the actual `StyleKey` read from the current target. It does not cache class/name activation or a complete selector result. Exact-type comparisons retain their simple comparison path. Constraints inherited from a previous selector retain their existing evaluation because that previous selector can involve mutable alternatives or nesting.

The cache contains only one type/result pair per selector, so it cannot grow with the number of control types. StyleKey is still read at its original point; changing it, classes, selector alternatives, resources, parents or theme variants is not hidden by a stale whole-selector match.

Tests: `FerroUiSelectorTests` covers a changing StyleKey, read count, class changes, mutable Or alternatives and selector replacement. This is a narrow type-check cache, not a new per-host theme cache or complete selector index. Existing per-element implicit-theme caching remains in `StyledElement.GetEffectiveTheme`; resource lookup/invalidation semantics have not been rewritten.

## 05. Same-type template-binding conversion fast path

`TemplateBindingExpression.PublishValue` skips the general target converter only for a source property whose declared value type equals the target type and has no converter. Parameterless template bindings, differing types, user converters and the UnsetValue sentinel retain the general path. Publication, errors, binding priority, disposal, templated-parent subscription changes and two-way writes still use the original binding expression and value store.

Tests: `FerroUiTemplateBindingTests` covers clear/detach/reattach publication, same-type custom converters, differing-type conversion and two-way writeback. This removes redundant conversion dispatch; it does not claim an entirely unboxed typed binding pipeline. The existing object-valued binding sink remains.

## 06. Bounded immutable short-run shaping cache

`HarfBuzzTextShaper` owns a bounded LRU of completed glyph-data snapshots. An eligible key contains the full string, a unique scalar HarfBuzz typeface identity, size, bidi level, effective culture name/LCID, tab width and letter spacing. No native font or GlyphTypeface object is stored in an entry. Font disposal bypasses the cache. A cache hit creates an independent pooled `ShapedBuffer` and copies glyph indices, clusters, advances and offsets; mutations, splitting, bidi processing and disposal therefore cannot corrupt another layout.

Eligibility is deliberately conservative: at most 128 characters, a whole immutable string, no explicit font features. Substrings retain HarfBuzz's original surrounding context and are never cached by content alone. Mutable arrays, memory managers and custom feature lists use the original path. Results with over 256 glyphs are not admitted. Capacity is limited to 256 entries and a 256 KiB conservative retained-byte allowance; entries are evicted least-recently-used. Locks protect the cache, while shaping remains outside the cache lock. Snapshot reads remain valid during eviction.

Tests: `ShapedRunCacheTests` compares cached output exactly with the uncached mutable-memory path for Latin, combining marks, tabs, breaks and RTL; covers every eligible option, distinct typeface identities, CurrentCulture, independent mutation/disposal, excluded inputs, LRU/byte bounds and concurrent warm reads. Whole-line formatting is not bypassed, so paragraph metrics, trimming, wrapping and hit testing retain their existing algorithms. Cross-layout native glyph-run sharing and a default-paragraph line-metrics shortcut are not implemented by this cache.

## 07. Allocation-free retained Skia text-blob lookup

`TwoLevelCache` gains an internal stateful factory overload. `GlyphRunImpl.GetTextBlob` passes its owner as state to a static factory instead of creating a captured closure before every lookup, including hits. Font/blob construction stays on misses, and the text-blob builder rental is returned in `finally`. Existing TextOptions normalization (including edging), primary/secondary cache policy and eviction disposal remain intact.

Tests: `TwoLevelCacheStateTests` covers both hit levels, exact factory state, no factory invocation on hits, eviction/disposal, throwing factories and zero managed allocation in warmed stateful lookups. The cache already retained SKTextBlob instances; this branch does not claim to introduce that existing feature. Composition already has dirty-subgraph traversal and dirty-rectangle clipping; those correctness-sensitive algorithms remain unchanged.

## 01, 08 and remaining design boundaries

C# virtual dispatch does not contain the Rust macro forwarding chains in design 01. No virtual override registration bitset or callback skipping is introduced. Typed lazy event values and small observer fast paths were already present in Avalonia.

Cargo optimization levels, Emscripten allocation settings and Rust hashers in designs 04/08 are not Avalonia build settings. No AOT, JIT, SIMD or diagnostics-overlay default is changed without target-specific size/startup/scroll measurements. Browser/GPU, cold-start, long unique-text, native screenshot and cross-layout glyph-sharing validation must not be inferred from managed unit tests or native hot-loop measurements.

The README tracks the remaining proposals explicitly. This implementation is not presented as full completion of every proposal in the nine design documents.
