# Repeated resource revalidation without dictionary churn

The first live-traversal refinement improved structural-mutation work but the default-runtime comparison still showed overhead. Every insert/remove lookup rebuilt a weak location and rewrote the same cache key after correctly invalidating it.

ResourceLookupCache now mirrors one immutable string key and theme identity inline. Repeated invalidation and revalidation of that key reads the epoch and updates the existing weak handle/state without hashing or rewriting the secondary dictionary. Before a different key is installed, any dirty primary state is flushed. Capacity clearing also clears the mirror; it does not create a 129th entry. Type keys retain their dictionary route, avoiding new assumptions about arbitrary Type subclass callbacks.

Invalidation itself is unchanged. An old epoch can never produce a hit, found-null remains distinct from missing, live values/factories are still read at the recorded location, and the inline entry holds only a weak dictionary reference. Tests cover key switches, alternating found/missing answers, independent variants and epochs, capacity, zero-allocation revalidation and collection of a dirty primary's resource graph.

The same original 26 scenarios are used to evaluate this change. No slower case is removed, renamed or assigned a looser threshold.
