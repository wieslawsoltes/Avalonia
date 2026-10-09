# Guarded single-change resource revalidation

The structural insert/remove scenario remained slower in default-runtime measurements after reducing traversal and dictionary bookkeeping. This change avoids re-search only when the result follows from the preceding completed search and exactly one known mutation. Every mutation still advances the global epoch and every public notification still runs.

## Proof and fallback

After removing a previously resolved key, a **full search** must first establish that no lower-priority fallback exists. If that removal was the only intervening invalidation, the cache may retain the old dictionary as a weak, still-reachable candidate: the graph itself has not changed. An immediately following ordinary insertion of the same string key into exactly that dictionary can then advance the negative entry to a positive location. Since the preceding search proved absence, the new location is known to be the sole candidate. Removing it as the next sole mutation can restore the negative answer.

Advancement requires an exact one-epoch delta, the same key, the same weak dictionary identity and the expected mutation kind. Graph/theme changes, more than one mutation, another dictionary, deferred insertion, bulk updates and unknown changes fall back to the full live traversal. A normal positive result is not presumed unique. A candidate is discarded after a graph change, so an old detached dictionary cannot reappear through this path. Found-null and missing remain separate, and positive hits still read the current value at the location.

A single bounded global mutation description uses a reused weak owner handle and at most one short string; it owns no resource graph. Publication and snapshotting are serialized without holding the gate across user callbacks. Generic invalidations invalidate the description. This is not an unbounded dependency index or a cache of live resource values.

## Arbitrary key callbacks

Resource dictionaries can contain custom object keys whose equality changes or runs callbacks without a resource event, even when the lookup key is a string. Such visited dictionaries now conservatively disable location caching. String and actual runtime-Type keys have stable machinery; arbitrary Type subclasses also retain live lookup. A sticky non-owning flag records introduction of unstable stored keys through setters, Add, bulk population and deferred materialization. This strengthens correctness rather than treating arbitrary custom equality as immutable during revalidation.

Tests cover actual alternating insert/remove shortcuts, lower-priority fallback, graph detach, unrelated owners, multiple epochs, found-null, deferred insertion, mutable stored equality before/after initial caching, TypeDelegator exclusion and zero allocation in warmed unboxed mutation cycles. Existing notification, precedence, deferred, custom-provider and lifetime suites remain required. Original benchmark scenarios and thresholds are unchanged.
