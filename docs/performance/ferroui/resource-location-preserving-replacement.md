# Location-preserving resource replacements

## Counterexamples

The `58504a1` Linux optimized-JIT report still flagged `resource-deep-mutation` (+17.2%) after weak-handle reuse restored allocations to 24 B/op. Each indexer replacement invalidated every resolution location and forced another walk through the same graph, although that walk could only find the same dictionary.

At `ecd05d0`, deep replacement improved to -92.0% versus baseline and allocations stayed at 24 B/op. The added local setter and genuine insert/remove scenarios exposed +47.1% and +15.5% paired median overhead respectively. Although the heuristic screen did not flag a consistent regression beyond its observed envelope, these positive measurements were not dismissed. They motivated the dependency-aware cold path and reduced miss traversal below.

## Implementation

A dictionary starts on a small, original single-lookup local setter. Every dictionary visited by a potentially cached resolution is marked as a lookup dependency before its values/providers are read. This includes higher-priority misses and every node of a negative lookup, not just the winning dictionary. A cache also marks any directly admitted location. The sticky bit stores no graph reference and never resets on eviction, so it cannot prematurely stop invalidating a surviving cache.

An ordinary mutation in a dictionary that has never participated in lookup caching need not flush caches elsewhere. Owner notifications are still delivered. Dependency state is checked after dictionary operations, because a user key callback could perform the first cached traversal during hashing. Merged/theme collection changes remain globally conservative.

For dependency dictionaries on .NET 6 and newer, the indexer uses `CollectionsMarshal.GetValueRefOrAddDefault` to distinguish insertion from replacement in one lookup. When an existing entry and its replacement are both non-deferred, the dictionary retains its location epoch. The actual value is written before notifying the owner through the unchanged `ResourceProvider.RaiseResourcesChanged` implementation. Cache hits read the live entry, so null, changed values and implicit-theme Type keys remain live.

No user code executes between borrowing the dictionary value ref and assigning through it. The ref is no longer used when host notifications run, so callbacks may resize the dictionary, change resources, throw or reenter. A graph epoch sampled before the dictionary operation rejects location preservation if a key's hashing/equality callback performed a location-invalidating mutation. The setter retains a single hash/equality lookup rather than adding a preflight `TryGetValue` with extra callbacks.

Older targets retain the original single-lookup setter and conservative dependency invalidation. Insertions, removals, clears, merged/theme collection changes, deferred transitions/materialization and bulk updates still invalidate dependent locations. No ancestor-host walk or custom-provider probe is cached away. No notifications are suppressed, even for replacement with the same object.

The uncached walk now carries one `ref bool cacheable` accumulator, rather than initializing/copying/ANDing a separate result at every child. Encountering any custom provider makes the entire resolution uncacheable, including when a later ordinary dictionary supplies the result. This changes bookkeeping, not the order or number of provider probes.

API contract: https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.collectionsmarshal.getvaluereforadddefault

## Tests and measurement

`FerroUiResourceReplacementTests` covers string and Type keys, live found-null values, negative entries, variant isolation/replacement, owner notifications, resize/reentrancy, higher-priority insertion, throwing callbacks, shared/non-shared deferred transitions, conservative bulk mutation, custom-key hash-call count/graph mutation, unrelated local mutations and first dependency registration during a key callback.

The native harness has 26 scenarios. In addition to deep value replacement, `resource-local-replacement` measures setter overhead without a deep lookup benefit and `resource-deep-insert-remove` measures genuinely location-changing mutations. Baseline and head receive identical harness sources. Fresh results, not earlier checkpoints, determine performance acceptance. The dependency marker is a per-dictionary field, not a claim of zero metadata cost.
