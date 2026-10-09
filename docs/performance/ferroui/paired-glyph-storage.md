# Paired glyph storage: remove duplicated ownership allocations

## Regression and original cost

At `ad96c386f5753545143c8844a76fb257e2ad1d5a`, the unchanged native harness still measured about 351-352 managed bytes per repeated shaped run against 336 in the original baseline. Optional metrics accounted for the remaining per-hit growth. Software browser list-wheel CPU also had a positive median; its three-pair diagnostic was inconclusive, so it is not attributed to this allocation without a new measurement.

Every pooled `ShapedBuffer` allocated two holders, two counters and two finalizable references: one set for `GlyphInfo[]`, another for the parallel `ushort[]`. Yet every non-empty split/bidi view always retains both arrays together. They do not need independent reference counts.

## Implementation

`PooledGlyphArray` owns both arrays behind the existing `IRef<PooledArray<GlyphInfo>>`. The direct slices remain unchanged for hot reads. Both ordinary and metrics-bearing constructors use that paired owner; all split and bidi constructors clone its one reference. The redundant glyph-index reference field, its clones and its disposal are removed. Prefix/cluster cache references remain independent and lazy.

The existing reference-counting implementation and its finalizer are unchanged. The owner's override returns both arrays exactly once; failure to rent the second array returns the first. Every independent run still has independent mutable arrays. Copying an immutable shaping snapshot remains a copy, not shared mutable storage. `CloneWritable` retains its caller-owned deep-copy path.

Metrics stay reference-local. `Clone` intentionally drops view metadata, while explicit metadata replacement preserves the same paired storage and current generation. The generic item type is specified explicitly at construction so `CloneWithState` retains the existing exact `Ref<PooledArray<GlyphInfo>>` contract. Public writes still update both arrays and invalidate shared generations. No shaping, wrapping, trimming, bidi, cluster lookup, font or glyph arithmetic algorithm is changed.

This removes allocation/ownership work rather than dropping metrics caching or moving state to a global table. It introduces no cache capacity, native retention or public API change.

## Tests and evidence

`PairedGlyphStorageTests` adds 15 cases: empty/single/large independent arrays, LTR/RTL split disposal orders, metadata replacement and alias mutation, concurrent release of distinct aliases, writable-copy independence, abandoned-reference cleanup, and warm allocation comparisons for cold and metadata buffers. The allocation reference reconstructs the previous two-owner topology using real holders/references and the now-smaller caller-storage buffer; it does not pad allocations to create a saving.

Existing exact glyph/layout/raster, cache eviction, mutation and font-lifetime suites remain required. The original 26 native scenarios, operation counts, warm-up, baseline and screen thresholds are unchanged. New elapsed-time and aggregate bytes-per-operation results must come from this commit's checks. Earlier runs do not validate this code; no all-regression or browser-speedup claim is made before those results are available.
