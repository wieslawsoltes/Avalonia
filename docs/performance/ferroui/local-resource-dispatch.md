# Keep local resource dispatch independent of graph resolution

Follow-up to the positive `resource-local-replacement` observation at `fa8bb8c`.

`TryGetResource` now contains only the local lookup, the post-callback child-presence check and a call to `TryGetNonLocalResource`. Cache eligibility, theme stability checks, epoch reads, weak-location validation and graph traversal live in the nonlocal method. The small entry point and indexer setter are marked inlinable; no application-wide JIT configuration or tiering bypass is used.

This is a code-layout change, not a weakening of invalidation. A found-null local value still wins. Children are inspected after local hashing/equality/deferred callbacks, so a callback can introduce a child before fallback. The setter still rechecks dependency state after arbitrary hashing, and cached supplying dictionaries still expose their current value. Tests exercise those cases; the existing arbitrary-key/theme/reentrancy suites remain required.

Original benchmark workloads and thresholds remain unchanged. New timing results, not the inlining annotation alone, determine performance acceptance.
