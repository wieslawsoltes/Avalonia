# Architecture and limits

## Model and history

Documents, layers and strokes are immutable. A stroke stores seven-channel samples, immutable brush attributes, a unique ID, an affine matrix and conservative bounds. Editing transforms never overwrite raw samples. Complete replacement documents are validated before state changes, including ID uniqueness and resource limits.

The editing session is thread-affine and rejects reentrant mutation from change notifications. Selection and active-layer state travel with undoable document edits; selection-only operations do not consume history. One pointer gesture is one transaction. Cancelling a gesture restores the exact previous snapshot and preserves the existing redo branch. Saved-snapshot identity prevents an asynchronous save from incorrectly marking later edits clean.

History shares immutable samples. Its point-reference budget is conservative, not an exact heap-memory measurement. Document mutations revalidate document-wide invariants, so mutation is not guaranteed constant-time. The spatial index rebuilds lazily after document replacement.

## Input

Coordinates pass through the inverse viewport and optional straightedge/grid constraints. Samples preserve pressure, X/Y tilt, twist and time. Mouse uses full pressure. Pen release with zero pressure preserves the previous contact pressure, since some drivers zero pressure when contact ends. Invalid device telemetry is sanitized; invalid externally constructed samples are rejected.

Smoothing is causal and velocity-adaptive. Stationary pressure changes and the final real endpoint are retained; no predicted positions are committed. Intermediate samples supplied by Avalonia are collected. Where Avalonia supplies only a batch timestamp, intermediate samples share that timestamp rather than inventing hardware timings.

Touch pans by default, with opt-in touch drawing. A second touch discards uncommitted touch ink and starts centroid-anchored pinch navigation. Palm rejection is a pen-active/recent-large-contact heuristic, not a trained classifier. Brush parameters are frozen at gesture start. Per-sample telemetry does not refresh every command; new ink invalidates the live overlay rather than retained document geometry.

## Rendering

The behavior-only templated canvas hosts a retained presenter and live/adornment presenter. Every stroke emits consistently wound elliptical nib footprints and connecting strips into one non-zero-filled path. Filling once prevents successive samples within a translucent highlighter stroke from darkening their overlaps. Independent strokes still composite normally; this is not multiply blending.

Full affine transforms apply to the outlines, including non-uniform resizing. The same backend-independent path sink feeds Avalonia and SVG. The retained presenter culls by viewport and caches geometry under count/point budgets. Large strokes bypass caching. These budgets measure geometry complexity, not exact GPU bytes. Rendering uses Avalonia's selected backend; the library does not replace it with a custom WebGPU renderer.

The uniform-grid broad phase uses 128-unit cells, a 4,096-cell per-stroke cutoff and a total 250,000-cell-entry budget. Large objects use a fallback list; unusually wide queries use a bounds scan. Queries preserve paint order. Grid spacing adapts at low zoom to bound dot/line work.

## Selection and erasing

Selection examines stroke segments, not only stored vertices. A sparse segment crossing a lasso is selected even when neither endpoint is inside it. Partial erasing analytically solves segment-versus-capsule intervals using two end circles and a connecting strip, so a fast sweep cannot jump over sparse ink. Fragments interpolate every sample channel, receive fresh IDs and preserve the source affine matrix.

Capsules are conservatively expanded by the maximum transformed nib radius on each segment. This avoids missing thick ink but may erase more than exact Boolean clipping of anisotropic outlines. Near-tangent numerical intervals are no-ops. Whole-stroke erasing removes editable hit strokes.

## Persistence

JSON uses the `avalonia-ink` schema, version 1. Explicit `Utf8JsonWriter`/`JsonDocument` code avoids reflection-based serialization in trimmed WebAssembly. Required fields, schema version, IDs, numeric bounds and counts are validated. Unknown extra fields are ignored; unknown schema versions are rejected.

Invalid input never partially replaces the open document. SVG uses escaped XML and pressure-aware vector outlines; no external URLs or executable content are imported. PNG renders document geometry through Avalonia offscreen rendering, not a screenshot of the editor, so selection/grid/cursors are excluded.

The sample's `IStudioStorage` boundary supports native file pickers or browser file inputs and Blob downloads. Browser download initiation cannot certify eventual on-disk retention. Recovery drafts are separate from named-file saves, local and debounced. Quotas and storage policy can prevent recovery; the sample reports the failure. Applications requiring stronger durability should supply their own transactional adapter.

## Resource policy

| Resource | Limit/default |
| --- | --- |
| Layers | 128 |
| Strokes per document | 100,000 |
| Points per stroke | 100,000 |
| Points per document | 2,000,000 |
| Raw coordinate magnitude | 10,000,000 units |
| JSON text | 64 Mi characters |
| JSON nesting | 24 levels |
| Brush width | 0.1–512 units |
| View zoom | 0.1–8 |
| History | 200 entries; 2,000,000 point references |
| Geometry cache | 1,200 strokes; 200,000 points |
| Raster export | At most 8,192 pixels per dimension and 32 million pixels total |
| Sample recovery text | 4 Mi characters |

A valid geometry document can still exceed the text serialization limit; saving then returns an explicit error rather than truncating data.

## Verification scope

The core suite covers input validation, nib/selection geometry, continuous erasing, affine channel preservation, history/cancellation, layer enforcement, serialization, SVG escaping and spatial-index correctness. It executes natively and again in WebAssembly.

The browser suite exercises the actual published Avalonia app through mouse, keyboard, synthetic pen/touch events, system clipboard, file pickers and downloads. Pixel checks inspect rendered output. The 10,000-stroke case records measured load/culling/cache observations on the named CI runner; these are not universal FPS or latency guarantees.

Additional acceptance testing is required for physical Windows/macOS/Linux digitizers, all browser engines, hardware acceleration configurations and screen readers. Handwriting recognition, OCR, collaboration protocols, Windows Ink/ISF codecs, textured brush physics and PDF import are outside this release, not silently stubbed features.
