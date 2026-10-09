# Location-preserving resource replacements

## Counterexample

The `58504a1` Linux optimized-JIT report still flagged `resource-deep-mutation` (+17.2%) after weak-handle reuse restored allocations to 24 B/op. Each indexer replacement invalidated every resolution location and forced another walk through the same graph, although that walk could only find the same dictionary.

## Implementation

On .NET 6 and newer, the indexer uses `CollectionsMarshal.GetValueRefOrAddDefault` to distinguish insertion from replacement in one lookup. When an existing entry and its replacement are both non-deferred, the dictionary retains its location epoch. The actual value is written before notifying the owner through the unchanged `ResourceProvider.RaiseResourcesChanged` implementation. Cache hits read the live entry, so null, changed values and implicit-theme Type keys remain live.

No user code executes between borrowing the dictionary value ref and assigning through it. The ref is no longer used when host notifications run, so callbacks may resize the dictionary, change resources, throw or reenter. A graph epoch sampled before the dictionary operation rejects location preservation if a key's hashing/equality callback performed a location-invalidating mutation. The setter retains a single hash/equality lookup rather than adding a preflight `TryGetValue` with extra callbacks.

Older targets retain the original single-lookup setter and conservative invalidation. Insertions, removals, clears, merged/theme collection changes, deferred transitions/materialization and bulk updates still invalidate. No ancestor-host walk or custom-provider probe is cached away. No notifications are suppressed, even for replacement with the same object.

API contract: https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.collectionsmarshal.getvaluereforadddefault

## Tests and measurement

`FerroUiResourceReplacementTests` covers string and Type keys, live found-null values, negative entries, variant isolation/replacement, owner notifications, resize/reentrancy, higher-priority insertion, throwing callbacks, shared/non-shared deferred transitions, conservative bulk mutation and custom-key hash-call count/graph mutation.

The native harness now has 26 scenarios. In addition to the existing deep value-replacement case, `resource-local-replacement` measures setter overhead without a deep lookup benefit and `resource-deep-insert-remove` measures genuinely location-changing mutations. Baseline and head receive identical harness sources. Fresh results, not the pre-change report, determine performance acceptance.
