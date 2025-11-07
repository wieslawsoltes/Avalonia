# Avalonia Layout Invalidation With `TextBlock`

## 1. Context and Observed Symptom
- The `/Users/wieslawsoltes/GitHub/lols` benchmark that relies on rapidly updating `TextBlock` instances shows far worse throughput than an owner-drawn text benchmark. Profiling points at frequent layout passes triggered by each text update.
- Avalonia's layout system tries to minimise recomposition by caching measurement state, but invalidation pressure from `TextBlock` breaks those assumptions and forces repeated measure/arrange work for large portions of the tree.

## 2. Layout Pipeline Overview
- **Visual tree nodes** derive from `Visual`, but layout participation starts at `Layoutable` (`src/Avalonia.Base/Layout/Layoutable.cs:29`). Each `Layoutable` tracks `DesiredSize`, `Bounds`, and validity flags (`IsMeasureValid`, `IsArrangeValid`).
- **Layout manager**: every `ILayoutRoot` owns a `LayoutManager` (`src/Avalonia.Base/Layout/LayoutManager.cs:35`). It keeps two queues (`_toMeasure`, `_toArrange`) that hold layoutables with invalid measure/arrange state.
- **Layout pass**: `LayoutManager.ExecuteLayoutPass()` (`LayoutManager.cs:116`) drains `_toMeasure` first (`ExecuteMeasurePass`, `LayoutManager.cs:248`), then `_toArrange` (`ExecuteArrangePass`, `LayoutManager.cs:264`). Passes repeat (up to `MaxPasses = 10`) until the queues stay empty, or a cycle is detected.
- **Recursive ordering**: `LayoutManager.Measure()` (`LayoutManager.cs:283`) walks up the ancestors first so that parents are measured before children. Arrange works similarly (`LayoutManager.cs:316`).

## 3. Invalidation Semantics
- Calling `Layoutable.InvalidateMeasure()` (`Layoutable.cs:440`) sets both measure and arrange to invalid, queues the layoutable on the manager, and also calls `InvalidateVisual()` (so rendering is scheduled even when layout later concludes size is unchanged).
- When a child’s measured size actually changes, `Layoutable.Measure()` compares `DesiredSize` with the previous value and notifies its parent through `ChildDesiredSizeChanged()` (`Layoutable.cs:473`). That notification simply calls `InvalidateMeasure()` on the parent (unless the parent is already mid-measure), so the invalidation bubbles up the tree until a container sees no size change.
- All `AffectsMeasure<T>()` registrations ultimately invoke `InvalidateMeasure()` when related styled properties change (`Layoutable.cs:496`).
- Containers such as `StackPanel` and `Grid` must re-measure all children whenever they themselves remeasure (`src/Avalonia.Controls/StackPanel.cs:233`), so a single invalidation can trigger linear work across child collections.

## 4. `TextBlock` Lifecycle
- The control rebuilds its `TextLayout` in `CreateTextLayout()` (`src/Avalonia.Controls/TextBlock.cs:657`), parameterised by constraint, typography properties, runs and trimming.
- Property changes feed into `OnPropertyChanged` (`TextBlock.cs:824`). For typography, wrapping, alignment, padding, decorations, and the `Text` property itself it calls `InvalidateTextLayout()`.
- `InvalidateTextLayout()` (`TextBlock.cs:699`) does two things: `InvalidateVisual()` to schedule a redraw, and `InvalidateMeasure()` to force a layout pass.
- `OnMeasureInvalidated()` (`TextBlock.cs:705`) disposes the current `TextLayout` and cached runs, so a subsequent measure always rebuilds shaping data from scratch.
- `MeasureOverride()` (`TextBlock.cs:714`) reacts to the incoming constraint. When the constraint changes it:
  - Invalidates arrange to keep alignment correct (`TextBlock.cs:733`).
  - Disposes the existing layout (`TextBlock.cs:728`-`732`).
  - Regenerates runs for inline content (`TextBlock.cs:739`-`748`).
  - Recreates the `TextLayout` and returns the inflated desired size (`TextBlock.cs:751`-`755`).
- `ArrangeOverride()` (`TextBlock.cs:758`) unconditionally disposes the cached `TextLayout` again (`TextBlock.cs:771`-`774`) so that alignment is evaluated against the final bounds, recreating it immediately afterwards.
- `TextPresenter` (used by `TextBox`) follows the same pattern (`src/Avalonia.Controls/Presenters/TextPresenter.cs:612`-`633`), which means the issue is not limited to `TextBlock`.

## 5. Why Frequent Text Updates Hurt
1. **Every text mutation triggers `InvalidateMeasure()`**. Even if the glyphs happen to fit in the previous width, Avalonia must rerun measurement to discover that fact.
2. **Measure-to-parent propagation**: when the new text requires a different width/height, the parent detects `ChildDesiredSizeChanged` (`Layoutable.cs:473`) and invalidates itself. Controls like `StackPanel`, `Grid` or `DockPanel` then remeasure all of their children, so a single scoreboard update can walk entire panels.
3. **Repeated text shaping**: the cached `TextLayout` is thrown away on each invalidation and again after arrange, forcing new glyph runs every time (`TextBlock.cs:705` and `TextBlock.cs:771`).
4. **Render invalidation for free**: `InvalidateMeasure()` also calls `InvalidateVisual()` (`Layoutable.cs:452`). That means even when the layout outcome is eventually identical, the control will hit both layout and render pipelines.
5. **Layout queue churn**: dozens or hundreds of invalidating `TextBlock`s enqueue separately, so the layout pass iterates measurement/arrangement loops multiple times. Each queue item in `_toMeasure` still travels the ancestor chain (`LayoutManager.cs:293`), so the cost is amplified for deep trees.

## 6. Interaction With Common Containers
- `StackPanel` measures each visible child in order whenever it is invalid (`StackPanel.cs:259`), so a single width change forces a full scan.
- `Grid` recalculates star sizing and shared size groups during measure, multiplying the amount of work.
- Virtualizing panels (e.g., `ItemsControl` templates) cannot skip remeasurement either because a width change can affect scrolling extent and viewport calculations.
- `LayoutHelper.InvalidateSelfAndChildrenMeasure()` (`src/Avalonia.Base/Layout/LayoutHelper.cs:114`) is invoked when a control moves in the tree (`Layoutable.cs:868`), so hierarchy changes compound the invalidation storm.

## 7. Summary Of Root Causes
- `TextBlock` always invalidates both measure and arrange on text or typography changes, even before verifying whether the desired size will differ.
- Measure invalidation escalates up the tree through `ChildDesiredSizeChanged`, ensuring parents and siblings pay the cost even when only a single text node changed.
- The `TextLayout` cache is aggressively discarded, so every layout pass has to redo text shaping.
- Layout passes are batched globally by `LayoutManager`, so a burst of text updates leads to global, synchronised measure/arrange work and matching render passes.

Together, those behaviours explain why the text benchmark experiences far more layout work than owner-drawn text, which can redraw glyphs without touching measure/arrange state.
