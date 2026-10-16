# Raw native blob buffers: remove the cold-path run-view allocation

## Motivation and implementation

`SharedGlyphRunData.CreateTextBlob` previously called `SKTextBlobBuilder.AllocatePositionedRun`. In the pinned SkiaSharp implementation this creates an `SKPositionedRunBuffer` managed object wrapping the native builder storage. Retained blob hits avoid it, but every new blob (including unique runs and a new font-option combination on a shared run) still paid for that wrapper.

Use `AllocateRawPositionedRun` instead. Its `SKRawRunBuffer<SKPoint>` is a value-type view of the same native memory. Copy the private positions and glyph-index arrays directly into the two spans, then call `Build`. Neither span escapes, the positions array is not exposed, and builder rental remains protected by `finally`. Reference-counted blob ownership, the 18 font-option combinations and cache admission are unchanged. This is not a new native allocation strategy and does not remove the native blob or font allocation.

Primary implementation references: [SkiaSharp v3.119.4 builder](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKTextBlob.cs), [run views](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKRunBuffer.cs), and [API contract](https://learn.microsoft.com/dotnet/api/skiasharp.sktextblobbuilder.allocaterawpositionedrun).

## Tests and evidence

`RawGlyphBlobTests` compares the new production path to an independent copy of the previous managed-wrapper construction for every rendering/hinting/baseline option combination. Bounds and raster bytes must match. A warmed allocation comparison constructs all geometry outside its measured regions and verifies that first-blob construction allocates less than the old path; it uses a lower bound rather than assuming one CLR object size across architectures.

Existing shared-blob lifetime, concurrent-wrapper and actual-eviction tests remain required. The native `native-glyph-unique` scenario measures the complete cold path, not just this isolated allocation. Browser interpreter/AOT validation also exercises the production path. Test definitions and API reasoning are not measurements: consult the checks for the exact commit before making a timing claim.
