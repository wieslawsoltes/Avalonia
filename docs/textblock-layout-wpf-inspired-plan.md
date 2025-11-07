# WPF-Inspired Improvements For Avalonia Layout And `TextBlock`

## 1. Objectives
- Reduce layout churn from frequent `TextBlock` updates by adopting proven patterns from WPF (`/Users/wieslawsoltes/GitHub/wpf`).  
- Preserve Avalonia’s rendering fidelity while minimising unnecessary measure/arrange passes and text-layout recreation.  
- Provide instrumentation comparable to WPF to guide further optimisation.

## 2. Decouple Render And Measure Invalidation
1. Modify `Layoutable.InvalidateMeasure()` to stop calling `InvalidateVisual()` directly (`src/Avalonia.Base/Layout/Layoutable.cs:451-453`).  
   - Introduce a new protected helper (`InvalidateVisualOnMeasureChange`) for controls that truly need a repaint.  
   - Document that controls should request redraws explicitly, mirroring WPF’s `UIElement.InvalidateVisual()` semantics (`src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs:306`).
2. Audit core controls (`Border`, `Panel`, `TextBlock`, `TextPresenter`) to ensure they call `InvalidateVisual()` where geometry changes affect rendering.

## 3. Cache-Friendly `TextBlock`
1. **Constraint-aware measure bypass**  
   - Port WPF’s `_referenceSize` logic to Avalonia’s `TextBlock.MeasureOverride` so equal constraints with `TextWrapping == NoWrap` or unchanged trimming skip measurement (`src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TextBlock.cs:1161-1213`).  
   - Retain the previous desired size and return early when conditions permit, preventing parent invalidation.
2. **Persistent `TextLayout`**  
   - Track the constraint used for the cached `TextLayout` and reuse it during `ArrangeOverride` when nothing changed, instead of disposing unconditionally (`src/Avalonia.Controls/TextBlock.cs:771-774`).  
   - Only rebuild shaped runs when `Text`, typography properties, or constraints differ materially.
3. **Selective measure invalidation**  
   - Replace unconditional `InvalidateMeasure()` in `InvalidateTextLayout()` with a size comparison step: preview the new layout using the existing constraint and only invalidate measure if width/height exceeds tolerance (mirrors WPF’s ability to skip measure when width unchanged).  
   - If size is stable, call `InvalidateVisual()` alone.
4. **Shared text cache**  
   - Introduce a reusable glyph/run cache keyed by constraint + font properties so repeated frames reuse formatting, similar to WPF’s `TextBlockCache` (`TextBlock.cs:1166`).  
   - Share the cache with `TextPresenter` to cover editable scenarios.

## 4. Layout Manager Instrumentation
1. Surface layout queue statistics via events comparable to WPF’s ETW hooks (`src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/LayoutManager.cs:116-189`).  
2. Record number of measured/arranged controls and total pass count per frame; expose via `LayoutPassTimed` or diagnostics provider for benchmarks.

## 5. Parent Propagation Optimisations
1. Extend `Layoutable.ChildDesiredSizeChanged` to include the previous and new desired sizes, letting parents short-circuit when deltas fall below a threshold.  
2. Update panels (`StackPanel`, `WrapPanel`, `Grid`) to compare child size deltas and skip remeasure when unaffected, emulating WPF’s reliance on cached desired sizes.

## 6. Migration & Compatibility
- Provide feature switches so existing applications can opt into the decoupled invalidation behaviour incrementally.  
- Add benchmarks mirroring the `/Users/wieslawsoltes/GitHub/lols` scenarios to validate improvement, using WPF parity as the target.

## 7. Validation Plan
1. Benchmark `TextBlock` update loops before/after each change, capturing layout queue lengths.  
2. Verify that visual output stays correct (alignment, trimming, wrapping) with measure bypass active.  
3. Exercise edit scenarios (`TextPresenter`) to ensure caret layout remains responsive.

## 8. Expected Outcomes
- Significant reduction in layout passes for stable-constraint text updates, approaching WPF’s behaviour.  
- Lower pressure on measure queues, improving UI thread throughput during high-frequency text rendering.  
- Clear diagnostics to monitor layout costs going forward.
