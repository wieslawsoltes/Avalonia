# Callback-free local resource indexer writes

The original `resource-local-replacement` scenario uses the indexer for **both** writing and reading; it does not call graph resolution. At `2c2dd26` Linux non-tiered measured -6.2%, but runtime-default still measured +8.9% against the original baseline. That is retained as an intermediate observation rather than discarded because the heuristic screen was clear.

The indexer now isolates the common local string write from callback-sensitive bookkeeping. Its guard requires that the dictionary has never become a lookup dependency, has no opaque stored resource/theme keys, and the requested key is a string. Under those conditions default dictionary hashing/equality cannot invoke user code during the write, so a second post-write dependency check is unnecessary. The owner notification still occurs after the value is written, even for an equal value or null. A callback establishing a dependency at that point sees the newly committed value; future writes use the dependency-aware path.

Opaque stored keys, custom requested keys, deferred-key callbacks and previously tracked supplying dictionaries retain the original callback-sensitive route and post-write invalidation recheck. This is not a removal of cache invalidation or notification. Neither thread-safety guarantees nor cache admission rules change. The small public indexer getter is also marked inlinable.

Three new tests cover owner notification order with reentrant parent lookup, equality callbacks establishing a dependency during a string write, and mixed runtime-Type/string keys. Existing deferred, null, reparenting, mutable-key/theme and resource-precedence suites remain required. Original benchmarks, warm-up and thresholds are unchanged.
