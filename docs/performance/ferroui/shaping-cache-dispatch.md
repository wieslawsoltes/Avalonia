# Separate uncacheable shaping from cache-key lifetime

The previous `ShapeText` method combined cache probing, a large key local and all native shaping/output locals in one method. Extract native shaping into `ShapeTextCore`, and key construction/lookup/admission into `ShapeTextWithCache`. Ineligible context slices and bypassed cold probes never enter the cache-key method. Options are passed by readonly reference between private helpers; the public signature is unchanged.

Backing memory is still resolved once, with the original surrounding string/array/MemoryManager context, start and length. Culture is captured at the same point; typeface validation and disposal checks precede either route. Probe/admission state, shaping arithmetic, glyph order, features, tabs, break merging, cache bounds and independent output ownership are unchanged. No native calls or glyph validation are suppressed.

New interleaving tests compare cacheable complete strings and uncacheable string slices with independently shaped array inputs, including tabs, CR/LF and RTL slices, and mutate each output before the next call. Existing context/MemoryManager/disposal/allocation tests and exact Skia raster tests remain required. Performance is measured using the unchanged original native and browser workloads.
