# Design 06 follow-up: scan-resistant shaping admission

## Evidence that motivated the change

The five alternating, fresh-process pairs in [run 37847755233](https://github.com/wieslawsoltes/Avalonia/actions/runs/37847755233) compared baseline `a9429a328057befa287ffb5e981f58b86a86eda0` with `a59460a82e5e74b92317addb7b3df92c76c5b8fc`. Repeated short-run shaping improved by a median paired 64.8%, but the 4,096-string scan regressed 23.5% and allocated 2,183.3 rather than 336.0 managed bytes per operation. This is a measured regression, not acceptable evidence of a universal text improvement. The run used Linux, Skia/HarfBuzz and disabled tiered compilation; it did not measure browser or GPU presentation.

## Implementation

`ShapedRunCache.Add` now requires a recently seen fingerprint before allocating a glyph snapshot, LRU node or entry. Its direct-mapped admission table has 1,024 slots and stores only scalar fingerprints: no one-off text, font, culture or buffer references. The 8 KiB table is charged against the existing 256 KiB retained-data allowance. Entry count and glyph-count limits remain unchanged.

A first occurrence is shaped normally and only updates the admission slot. A second eligible occurrence can install an immutable snapshot; subsequent hits continue returning independent buffers. The hint is approximate: a hash collision may admit a cold entry, and a slot conflict may delay admission. Neither can return incorrect glyphs because retrieval still compares the complete original key. No equality, shaping, culture, direction, tab, spacing or font-lifetime semantics change.

The two-touch policy also prevents an ordinary one-off scan from evicting repeatedly used labels. It deliberately spends one extra shape during warm-up rather than allocating a large snapshot for every unique string.

## Regression coverage

`ShapedRunCacheTests` now executes a genuine post-admission cache hit for every text/options equivalence case, tests zero managed allocation in 4,096 cold admission operations (with distinct fingerprints selected before measurement), verifies that a one-off scan retains a hot run, and accounts for admission storage in the byte limit. Existing independent-buffer, concurrent-read, LRU, excluded-input and culture tests remain.

## Validation status

The parent revision passed all six configured unit-test suites and the .NET 8 library builds in [run 37847755100](https://github.com/wieslawsoltes/Avalonia/actions/runs/37847755100). That is not validation of this follow-up. Its tests and paired performance measurements must be read from the run for the new commit; no post-change speedup is claimed here before those results exist.
