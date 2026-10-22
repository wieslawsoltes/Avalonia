# Structural-mutation regression: cheaper live resource traversal

The `e8c4cb2` comparison flagged `resource-deep-insert-remove` in both Linux modes and macOS runtime-default. That workload deliberately invalidates resource locations on every operation. Rebuilding the same location is not a cache hit, and preserving stale locations would be incorrect.

This revision keeps all invalidation and the original lookup order. It removes avoidable work from the fallback itself:

- Ordinary leaf dictionaries are checked directly in the inlinable probe, rather than entering a second recursive method just to find that no children exist.
- The last remaining merged dictionary is a tail call. A loop replaces that call only after higher-priority themes and siblings have missed. Other branches and custom providers retain their existing dispatch.
- Deferred construction is moved out of the small `TryGetValue` path. Shared/non-shared factories, recursion guards, exception behavior, epoch updates and found-null semantics are unchanged.

This is a live traversal, not a flattened graph snapshot. Children are inspected after local equality/factory callbacks, and the pending sibling is read only when its turn arrives. Tests cover a 512-level graph with insert/remove cycles, ordered non-tail/custom probes, replacement of the pending tail during a callback, children added on a comparer miss, theme precedence, and recovery from throwing factories. Existing resource invalidation/lifetime tests remain required.

The original 26 benchmark scenarios are unchanged. Improvement must be read from the new revision's measurements; the earlier regression is not erased and no post-change result is asserted before that run completes.
