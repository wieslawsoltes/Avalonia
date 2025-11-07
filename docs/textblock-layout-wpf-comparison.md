# Avalonia vs. WPF Layout Invalidation And `TextBlock` Behaviour

## 1. Overview
- Goal: understand why Avalonia’s layout/`TextBlock` pipeline incurs large costs during repeated text updates compared to the .NET WPF stack under `/Users/wieslawsoltes/GitHub/wpf`.
- Focus areas: layout managers, invalidation propagation, and `TextBlock` measurement semantics in both codebases.

## 2. Layout Scheduling And Queues
- **Avalonia**  
  - `Layoutable.InvalidateMeasure()` clears both measure/arrange flags and immediately enqueues the control plus a render invalidation (`src/Avalonia.Base/Layout/Layoutable.cs:440`).  
  - `LayoutManager` keeps two queues (`_toMeasure`, `_toArrange`) and enqueues invalid controls for processing on the next render tick via `MediaContext.BeginInvokeOnRender` (`src/Avalonia.Base/Layout/LayoutManager.cs:71` and `:318`).  
  - Measurement of a control walks up the ancestor chain so parents are remeasured before the child (`LayoutManager.cs:293`).
- **WPF**  
  - `UIElement.InvalidateMeasure()` only sets `MeasureDirty` and adds the element to `ContextLayoutManager.MeasureQueue` without touching visuals (`src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs:249`).  
  - `InvalidateVisual()` only invokes `InvalidateArrange()` (render invalidation is decoupled from measure) (`UIElement.cs:306`).  
  - `ContextLayoutManager` similarly schedules work onto `MediaContext`, but also offers `markTreeDirty` to bulk-mark subtrees for DPI changes (`LayoutManager.cs:52` and `:73`).
- **Key differences**  
  - Avalonia pairs measure invalidation with a render dirtiness, guaranteeing both layout and draw will happen; WPF lets render invalidation remain opt-in.  
  - WPF exposes `ContextLayoutManager.From(Dispatcher)` to fetch the per-thread manager, making it easier to instrument and throttle layout from external helpers. Avalonia’s manager is accessed through `ILayoutRoot`.

## 3. Propagation Of Desired Size Changes
- **Avalonia**: when a child’s desired size changes during measurement, `Layoutable.Measure()` notifies the parent via `ChildDesiredSizeChanged`, which calls `InvalidateMeasure()` unless the parent is already measuring (`src/Avalonia.Base/Layout/Layoutable.cs:473`).  
- **WPF**: `UIElement.OnChildDesiredSizeChanged` mirrors this behaviour (`src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs:313`), triggering `InvalidateMeasure()` when currently valid.  
- **Practical impact**: both systems bubble invalidations upwards. However, because WPF’s `InvalidateMeasure()` does not automatically touch rendering, the scope of additional work is narrower unless measure outcomes change.

## 4. `TextBlock` Invalidation Semantics
- **Avalonia implementation**  
  - `InvalidateTextLayout()` disposes cached `TextLayout` data and invokes both `InvalidateVisual()` and `InvalidateMeasure()` (`src/Avalonia.Controls/TextBlock.cs:699`), guaranteeing a full new layout pass.  
  - `OnMeasureInvalidated()` disposes shaped runs before delegating to the base (`TextBlock.cs:705`).  
  - `MeasureOverride()` forces an arrange invalidation whenever the measured constraint changes and clears all caches (`TextBlock.cs:724-735`).  
  - `ArrangeOverride()` also disposes and rebuilds `TextLayout` even if the constraint matches (`TextBlock.cs:771-777`).
- **WPF implementation**  
  - The `Text` property metadata flags the property as affecting measure and render (`FrameworkPropertyMetadataOptions.AffectsMeasure | AffectsRender`) so normal property change propagation schedules work (`src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TextBlock.cs:559-569`).  
  - `TextBlock.MeasureOverride` aggressively caches and can skip the entire measure pass when constraints and trimming/wrapping conditions make the previous desired size still valid (`TextBlock.cs:1161-1213`).  
  - Cached `LineMetrics`, `InlineObjects`, and `_referenceSize` are retained until a constraint or content change requires recomputation (`TextBlock.cs:1204-1238`).  
  - Arrange does **not** discard layout results unless alignment requires it; caches persist across arrange cycles.
- **Key differences**  
  - Avalonia disposes text layout data on every arrange and measure invalidation, forcing re-shaping on subsequent passes; WPF reuses computed lines until constraints actually change.  
  - Avalonia’s `TextBlock` unconditionally invalidates arrange on constraint changes; WPF tracks `_referenceSize` to determine whether the previous arrangement remains valid.  
  - WPF avoids redundant `InvalidateMeasure()` by short-circuiting the measure pipeline when `TextWrapping` is `NoWrap` and the width is unchanged; Avalonia currently has no similar bypass.

## 5. Layout Manager Behaviour
- **Avalonia**  
  - Executes up to ten passes per frame before declaring a cycle (`LayoutManager.cs:234`).  
  - After each measure pass, invalidated controls are automatically enqueued for arrange (`LayoutManager.cs:260-261`).  
  - Layout passes always finish by raising `LayoutUpdated` (`LayoutManager.cs:136`).
- **WPF**  
  - Tracks recursion depth separately for measure and arrange (`ContextLayoutManager.cs:55-92`).  
  - Supports `UpdateLayout()` entry point to synchronously drain queues (Avalonia only has `ExecuteLayoutPass()` on the root).  
  - Maintains telemetry hooks (ETW and CLR profiler logging) around layout passes (`ContextLayoutManager.cs:116-189`), providing richer diagnostics for tuning.

## 6. Observed Advantages In WPF
- Property metadata differentiates between measure and render dirtiness so text updates that do not affect geometry avoid redraw coupling.  
- `TextBlock` caches both size and formatted line data, allowing full measure bypass when constraints are stable, which reduces layout churn for frequent text changes.  
- Arrange does not force layout invalidation of descendants unless necessary, avoiding cascading re-measure in parent panels.  
- Diagnostics/telemetry hooks make it easier to reason about layout cost in production.

## 7. Opportunities For Avalonia
- Decouple render invalidation from measure invalidation for text-centric controls, following the WPF `InvalidateVisual()` pattern.  
- Port WPF’s measure bypass heuristics (constraint equality + wrapping/trimming checks) into Avalonia’s `TextBlock.MeasureOverride`.  
- Retain `TextLayout` instances across arrange passes when size/alignment constraints are unchanged to avoid repeat shaping.  
- Introduce explicit telemetry in `LayoutManager` comparable to WPF’s ETW hooks to monitor queue lengths and pass counts.

## 8. Conclusion
- WPF’s layout engine takes extra steps to bypass redundant work by caching text layout data, separating render invalidation from measure invalidation, and instrumenting layout passes.  
- Avalonia’s current approach eagerly invalidates both layout phases and throws away caches, which matches the observed benchmark regressions.  
- Incorporating the identified WPF strategies should substantially reduce the layout cost of rapidly-updated `TextBlock` instances in Avalonia.
