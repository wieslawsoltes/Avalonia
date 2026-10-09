# Expiring cold shaping-admission hints

## Counterexample

At `58504a1152caccfd8b86ed227fececea20ada77c`, the 24-scenario Linux optimized-JIT comparison still flagged `shape-unique-short`: +6.1% time, 336 -> 437.3 managed bytes/op. Two-touch hints had no age limit. A second pass over an oversized corpus could turn old hints into admissions, reset the miss streak, and repeatedly allocate snapshots without producing cache hits.

## Change

Keep the same 1,024-slot, 8 KiB hint table, packing a 32-bit hash and a 32-bit age stamp into each slot. A second touch must occur within 256 unsuccessful requests. Sampled misses advance the clock in `Add`; bypassed requests advance it in `ShouldProbe`, so backoff cannot artificially extend hint lifetime. Cache hits perform no additional atomic increment.

Admission alone no longer resets the unproductive-miss streak. It permits one immediate probe so a newly hot string can produce a hit and recover promptly. Only a real dictionary hit resets the streak. All existing complete-key checks, bounds, immutable snapshots, independent buffer ownership, culture/typeface checks and conservative fallback paths remain unchanged.

The table is a performance hint, not an identity cache. Hash collisions or a very old modular-clock alias may admit a cold entry but cannot return incorrect glyphs. There are no retained cold strings/font references and no additional hint arrays. Successful hits do not age cold hints; the window measures unsuccessful work, not wall time.

## Regression tests

`ShapingAdmissionWindowTests` checks allocation-free repeated oversized scans with collision-free hint slots, hint expiration during bypasses, admission without a subsequent hit, and recovery to a previously unseen hot string with exact uncached-glyph comparison. The LRU test now performs a real hit after each admission: it tests eviction of useful entries rather than attempting to disable backoff with admission-only work.

This change needs fresh native and browser comparisons before claiming a timing improvement. Tests are deterministic; timing remains report-only with the existing A/A-calibrated screen.
