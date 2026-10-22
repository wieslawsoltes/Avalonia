# Bounded recovery from cold scans to cyclic working sets

## Counterexample

At `cfc81d4026456901092ad1bd839bc34c98e1e08b`, the Linux optimized-JIT comparison measured unique native glyph allocation below baseline (624 vs 632 B/op), but wrapped layout allocation rose to 11,594.4 vs baseline 10,890.4 B/op. That workload follows the large unique native-glyph scan. Inspecting the interaction between hint expiry and isolated probes exposed a recovery gap: for two alternating native runs, a 67-request sample stride revisits each key after 134 requests, beyond the 128-request admission window. It can therefore keep missing indefinitely despite a tiny stable working set. Four alternating shaped strings similarly repeat their sampled touches after 268 requests, beyond the shaping window of 256.

This is a policy counterexample, not justification to disable the wrapping benchmark or assume its allocation regression is noise.

## Recovery bursts

Keep ordinary isolated probes every 67 unsuccessful requests for fast recovery to a single hot key. After 32 such sampled probes without a hit, allow a bounded consecutive probe burst: `2 * admissionWindow + 1` requests (257 native / 513 shaping). This gives a cyclic cache-sized working set an opportunity for first touch, admission and actual reuse without fixed-stride aliasing. A real hit immediately clears recovery state and restores normal probing. Admission alone still does not reset the miss streak.

Hints keep aging on all unsuccessful eligible native requests and on shaping bypasses. No cold fingerprints are made artificially young, no snapshots are admitted solely because a burst starts, and capacity/byte limits and complete key/glyph checks remain unchanged. Pure oversized scans retain the existing no-admission tests. The burst and sample counters are per shaping cache and per native typeface, respectively; they retain no caller objects. Under contention the exact number of speculative probes can vary, but returned data and lifetime correctness remain protected by the existing cache locks.

Periodic bursts add lookup work on truly cold streams. They are intentionally infrequent, not a claim of zero miss overhead. Hint collisions and byte limits can still prevent admission of some working sets; these remain performance hints, never correctness keys.

## Tests

`ShapingWorkingSetRecoveryTests` covers cyclic sets of 4, 32 and 256 strings after forced cold backoff, requiring every key to become a cache hit and comparing exact glyphs with the uncached path. `NativeGlyphWorkingSetRecoveryTests` covers 2, 32 and 128 runs and counts actual shared identities during the cyclic workload, then verifies shared blob identity. The tests choose distinct hint slots to isolate recovery policy from probabilistic admission collisions. Existing oversized-scan, single-hot recovery, eviction, font lifetime, concurrent blob and raster tests remain enabled.

Re-run the 26-scenario native matrix and browser validation at the exact new head. The prior `cfc81d4` results are evidence for the counterexample, not measurements of this refinement.
