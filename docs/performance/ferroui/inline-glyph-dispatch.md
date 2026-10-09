# Inline glyph dispatch and borrowed blob cache

`GlyphRunImpl` no longer allocates a separate `TwoLevelCache<TextOptions, SKTextBlob>`
for every cold, unique or recreated native run. The first borrowed blob and its options
are inline. Three insertion-ordered alternatives are allocated only after a second
option is requested. This preserves the previous first-entry/three-alternative policy,
lock-free wrapper-local hits and shared native ownership. Eviction and wrapper disposal
never dispose a blob still owned by another live wrapper or the bounded run cache.
The general TwoLevelCache utility and its tests remain; production text no longer roots
its generic factory/comparer path in trimmed/AOT apps.

Native geometry initialization and cache hashing/equality/snapshot copying use borrowed
spans for the sealed ShapedBuffer type and GlyphInfo arrays. Sliced buffers use their
actual start/length, not their entire pooled backing array. Snapshots remain independent;
no span is retained across the synchronous call. Hash fields, Equals semantics, admission
policy, cache limits, native font options and floating-point arithmetic order are unchanged.
Arbitrary IReadOnlyList implementations stay on the callback-preserving general path.

Tests cover zero-allocation warm lookup, lazy alternative storage, all 18 canonical text
options, eviction/disposal lifetime, LTR/RTL split views, exact raster/bounds agreement,
mutation after construction, empty/long spans and user indexer callbacks.

Measure the unchanged 26-scenario native harness against the previous exact runtime and
original PR baseline. Removing these costs does not itself certify arbitrary-host timing,
physical GPU performance or browser startup. Deployment bytes and CPU timings are reported
separately from managed current-thread allocations.
