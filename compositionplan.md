# Connected Animation & Composition Parity Plan

## Current State Snapshot
- Avalonia exposes `ElementComposition` to reach backing `CompositionVisual` instances, `ImplicitAnimationCollection`, keyframe animations (scalar/vector/color), `CompositionCustomVisual`, and `CompositionSurfaceVisual`.
- Composition visuals can be added beneath any `Visual` via `ElementComposition.SetElementChildVisual`, enabling custom drawing/animation like the control catalog sample.
- There is no public helper for capturing a visual into a reusable composition `Surface` or brush; snapshots are only exposed as `Compositor.CreateCompositionVisualSnapshot(...)` returning a `Bitmap`.
- No connected-animation-style coordination layer exists (no equivalent to WinUI `ConnectedAnimationService`, `ConnectedAnimation`, or configuration types).
- Composition batching, linking animations to navigation, and automatic z-layer overlays are left to application code; there is no first-class framework support.

## Gap Analysis vs WinUI/UWP Composition
- **Connected Animations:** Missing service API, animation coordinator, and element lifecycle hooks (prepare/start/cancel) necessary to bridge source and destination visuals across navigation events.
- **Visual Capture Pipeline:** Lacks lightweight way to turn an existing `Visual` into a composition surface/brush without going through GPU interop APIs. WinUI relies on `CompositionVisualSurface`/`SpriteVisual` with content brush; Avalonia needs analogous capabilities to show a snapshot while elements are hidden.
- **Scoped Batching & Completion Events:** WinUI uses `CompositionScopedBatch` to know when connected animations finish. Avalonia has internal batching for commits but no public scoped batch helper.
- **Navigation/Transition Integration:** No built-in handshake between `TransitioningContentControl`, `Frame`, or `NavigationView` and composition animations.
- **Tooling Support:** No sample, documentation, or reusable helpers to demonstrate connected animations in `ControlCatalog` or docs.

## Implementation Milestones

### Milestone 0 – Composition Infrastructure Enhancements
1. **Expose Composition Surface Helpers**
   - Extend `Compositor` with helpers to convert a `Bitmap` (from `CreateCompositionVisualSnapshot`, `RenderTargetBitmap`, or streamed pixels) into a `CompositionSurface` instance (e.g., `Compositor.CreateBitmapSurface(Bitmap)` or `CompositionDrawingSurface.UpdateFromBitmap`).
   - Introduce a lightweight `CompositionSurfaceBrush` analogue so a surface can be painted on a `CompositionSurfaceVisual` without custom rendering code.
   - Ensure surfaces respect DPI/render scaling and support async disposal on render thread.
2. **Public Scoped Batch Wrapper**
   - Add a managed `CompositionScopedBatch` class with `Completed` events, delegating to existing compositor batch infrastructure. This is needed to know when to show/hide destination elements.
3. **Visual Coordinate Utilities**
   - Provide helpers to fetch the global transform and bounds for a `Visual`/`CompositionVisual` (e.g., `CompositionVisual.TryGetGlobalBounds()`) so connected animations can position overlay visuals correctly across nested transforms.
   - Handle multi-monitor scaling and `TopLevel.RenderScaling`.

### Milestone 1 – Connected Animation Core
1. **ConnectedAnimationService API**
   - Add `ConnectedAnimationService` attached to each `TopLevel` (similar to WinUI) with `PrepareAnimation(key, Control element, ConnectedAnimationConfiguration? config = null)` and `StartAnimationAsync(key, Control destination, IDictionary<string, object>? hints = null)`.
   - Maintain a dictionary of pending animations with captured metadata (visual snapshot surface, initial bounds, configuration).
2. **ConnectedAnimation Runtime Implementation**
   - When preparing: capture snapshot surface (Milestone 0), hide source element after capture, create overlay `CompositionSurfaceVisual` hosted in a new overlay layer on the window compositor.
   - When starting: position overlay at destination, animate translation/scale/opacity using `Vector3KeyFrameAnimation` and `ScalarKeyFrameAnimation`. Utilize scoped batch to await completion.
   - Support cancellation fallback: if destination is missing or layout not ready, fade out the snapshot and clean up gracefully.
   - Provide configuration for easing/duration and optional `ConnectedAnimationConfiguration` presets (default, `BounceConnectedAnimationConfiguration`, etc.) for parity.
3. **Overlay Layer Management**
   - Add an internal `ConnectedAnimationLayer` to each `TopLevel` that is inserted above normal content (similar to popup/adorner layers) to host temporary composition visuals.
   - Ensure overlay participates in hit-testing appropriately (non-interactive) and handles window resizing or DPI changes by updating overlay transforms.

### Milestone 2 – Framework Integration & Sample
1. **Navigation Helpers**
   - Provide extension methods or built-in integration with `TransitioningContentControl` (used by `Frame`/`NavigationView`) to automatically prepare and start connected animations based on element names/keys during navigation events.
   - Offer simple attached property (e.g., `ConnectedAnimation.Key="HeaderImage"`) to mark source/destination elements.
2. **ControlCatalog Sample**
   - Create a new sample page demonstrating List → Details navigation with connected animation of thumbnail → hero image plus optional text fade.
   - Show toggles for precise dirty rects, durations, and fallback to demonstrate debugging.
3. **Documentation & Guidance**
   - Update `/docs` with a new guide (and link from ControlCatalog) explaining setup, API usage, limitations, and platform notes.
   - Include comparison table vs WinUI to help adopters port code.

### Milestone 3 – Optional Parity Enhancements
1. **Implicit Entrance/Exit Connected Animations**
   - Support simultaneous multiple connected animations and chaining (matching WinUI `SetListDataItemForNextConnectedAnimation`).
2. **Brush-based Animations**
   - Expose new APIs for animating `CompositionBrush` content (e.g., gradient brush, preloaded image brush) to enable more advanced transitions.
3. **Compiler/Analyzer Support**
   - Provide Roslyn analyzer or diagnostics that warn when `PrepareAnimation` is called but `StartAnimationAsync` is never invoked.
4. **Unit & Visual Regression Tests**
   - Add Playwright-based UI tests verifying connected animations run and complete under different DPI/platform backends.

## Technical Considerations
- Ensure new APIs stay in `Avalonia.Rendering.Composition` to mirror WinUI naming while remaining Avalonia-friendly.
- Connected animations must work on all backends (Direct2D, Skia, OpenGL, Metal, software) — plan tests for each.
- Snapshot capture should be GPU-friendly; for GPU backends consider zero-copy paths via `ICompositionGpuInterop` while falling back to CPU copies if unavailable.
- Handle async navigation: keep prepared animation alive across layout passes and cancel if the source element is disposed.
- Provide thread-safe cleanup to avoid leaking `CompositionSurface` instances or overlay visuals.

## Testing Strategy
- Unit tests for service state machine (prepare/start/cancel).
- Integration tests in `Avalonia.Controls.Tests` simulating navigation and asserting overlay visual lifecycle.
- Automated visual regression (Playwright) capturing before/after frames to verify animation completes and final layout matches expectation.
- Manual test checklist for multi-window scenarios, resizing during animation, and high-DPI monitors.

## Deliverables
- New public APIs (`ConnectedAnimationService`, configuration types, helper methods) with XML docs.
- Composition infrastructure extensions (surface helpers, scoped batch). Covered by unit tests.
- ControlCatalog demo page and updated docs.
- Migration guidance for developers familiar with WinUI connected animations.
