# Keep ineligible shaping outside adaptive-cache bookkeeping

The previous shaper called `ShouldProbe` before knowing whether a request could ever use the glyph cache. During backoff this meant atomic countdown/age updates even for context-sensitive slices, mutable memory, explicit font features and long text. The native miss path also separately rediscovered the surrounding memory after the key builder inspected it.

`HarfBuzzTextShaper` now resolves the backing memory once. That same result supplies the complete immutable string identity, when available, and the surrounding memory plus slice offset/length needed by HarfBuzz. Eligibility is checked before entering the adaptive policy; culture identity/key construction stays after a positive probe decision. `ShapedRunCache` exposes internal option validation and key construction shared with its independently tested `TryCreateKey` helper.

## Compatibility and tradeoffs

String/array/MemoryManager slices retain their surrounding characters and relative glyph clusters. The cache still only accepts whole short immutable strings with eligible options. Font validation and disposed-font rejection precede both paths. Returned buffers remain independent, and the 16-byte metrics-metadata cost is not removed by this change.

Only **eligible** requests advance the admission/backoff clock. Eligible bypasses still age hints, preventing over-capacity scans from keeping stale hints alive. Ineligible requests neither consume a recovery burst nor postpone the next eligible probe. The hint table retains only bounded numeric fingerprints, not ineligible text or fonts. These are policy changes, not changes to glyph output or cache ownership.

Eligibility checks now occur on every eligible cold request, rather than only sampled requests. Removing redundant backing-memory discovery offsets some work, but net elapsed-time improvement must be measured; no universal speedup is assumed.

## Tests

`UncacheableShapingPolicyTests` checks unchanged policy state for long strings, string slices, array slices, MemoryManager slices and explicit features while the cache is in backoff. It compares glyphs across backing-memory representations with identical Arabic surrounding context and checks disposed-font rejection on an ineligible slice. Existing full key, cultural, independent-buffer, mixed hot/cold, over-capacity and cyclic recovery tests remain required, as do native checksum comparisons and browser raster checks at the exact revision.
