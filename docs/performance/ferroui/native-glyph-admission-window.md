# Expiring native glyph admission hints and removing cold-run lock allocation

## Measured counterexample

At `795aa01751c97fcf938c8839c541d44cf20b35d8`, the Linux optimized-JIT comparison flagged `native-glyph-unique`: +10.4% paired time, 632 -> 659.3 managed bytes/op. The native cache still retained unaged two-touch hints and reset the per-font miss streak when it admitted an entry, even if that entry was never hit. Repeated oversized corpora could therefore produce unhelpful snapshots and reset backoff, the same issue already addressed in the shaping cache.

Evidence: https://github.com/wieslawsoltes/Avalonia/actions/runs/37913033093/job/113762483161

## Admission change

The existing 1,024-slot, 8 KiB fingerprint table now packs a 32-bit fingerprint and a 32-bit per-font age. A second unsuccessful touch must be within 128 unsuccessful requests. Skipped probes advance the same font's clock so sampling does not keep an old hint artificially young. A real cache hit adds no clock increment. Unrelated fonts do not advance this clock, and the existing per-font probe policy remains in place.

Creating a new entry grants one immediate probe but does not reset the unproductive-miss streak. Only reuse of existing geometry resets it, including the concurrent-caller deduplication case. The 67-request probe interval permits a newly recurring run to be admitted and hit again within the admission window.

The table is an admission hint, not an identity map. Fingerprint collisions, cross-font collisions or an ancient modular-clock alias can only admit a cold entry: complete keys and exact glyph equality still govern returned geometry. There are no additional hint arrays or retained cold font/glyph references; a per-font integer clock does add bookkeeping.

## Owned-storage monitor

`SharedGlyphRunData` formerly allocated a separate `object` solely to lock its lazy blob state. Its existing positions array is private, freshly allocated by the constructor, readonly in identity, never pooled and never exposed to callers. `SetPositions` copies the elements into native blob storage; it does not publish the managed array. Both blob publication and final release now lock that same owned array instead. This removes one object and its field per cold geometry object without removing synchronization or changing lock order. Array contents are fully initialized before any instance is published.

This is specifically an owned, non-escaping storage object, not a recommendation to lock caller-supplied or pooled arrays. All bounds calculations, raster algorithms, text-option states, native handle ownership, live leases, eviction and disposed-font checks are unchanged.

## Tests

`NativeGlyphAdmissionWindowTests` covers fingerprint/age boundaries and unsigned wrap, admission without a real hit, aging during bypasses and recovery after repeated oversized scans. The integrated scan chooses distinct hint slots and excludes previously recorded fingerprints to make its no-admission assertion deterministic. Recovery verifies shared geometry/blob identity, bounds and exact raster equality with the uncached path.

`SharedGlyphRunTests` now creates an actual hit for every pressure entry and asserts full capacity and loss of the old cached identity. This proves that the live-blob lifetime test really evicts its entry instead of merely exercising admission backoff. `SharedGlyphBlobLifetimeTests` covers changing all effective font-state combinations and concurrent wrappers holding borrowed blobs while others are disposed. Those same tests exercise the owned-storage monitor.

Fresh native measurements are required before claiming this removes the observed timing regression. The separate `resource-deep-insert-remove` counterexample (+10.3% at the same revision) is not addressed by these native-glyph changes. Cache/lease metadata and genuine structural invalidation still have costs and must be reported alongside repeated-run benefits.
