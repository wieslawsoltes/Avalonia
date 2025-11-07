# Plan To Reduce `TextBlock` Layout Invalidation Cost

## Objectives
- Stop routine text updates from cascading measure/arrange invalidations through the entire visual tree.
- Keep `TextBlock` sizing predictable so that typography changes do not constantly force parent containers to redo layout.
- Preserve rendering correctness (alignment, wrapping, trimming) while reducing redundant `TextLayout` rebuilds.

## Phase 1 – Measurement & Instrumentation
- Add targeted logging hooks (reuse `LayoutManager.LayoutPassTimed`, `LayoutManager.LayoutPassTimed` already exists at `LayoutManager.cs:124`) to record number of controls measured/arranged per frame in the benchmark.
- Capture traces comparing the owner-drawn benchmark and the `TextBlock` version to quantify where layout time is spent (expected: text shaping vs parent container measure).
- Gate progress on a reproducible perf benchmark checked into `samples` or `benchmarks` so regressions can be caught automatically.

## Phase 2 – App-Level Mitigations
1. Expose guidelines to app developers:
   - Reuse fixed layout constraints (e.g., constrain scoreboard cells using `Width`, `MinWidth` or a wrapping container) to stabilise desired sizes.
   - Replace individual `TextBlock` instances with a single `Control` that draws multiple strings when sizing is known ahead of time.
2. Provide a `TextBlock` helper (attached property or control option) that skips `InvalidateMeasure()` if the incoming text length matches the previous length and wrapping is disabled. This heuristically avoids layout work when width/height is unlikely to change, while still allowing explicit invalidation for corner cases.

## Phase 3 – Engine Improvements
1. **Refine `TextBlock.InvalidateTextLayout()`**
   - Cache the last measured size on the control.
   - When text changes, precompute the new `TextLayout` size (without `InvalidateMeasure()`), compare against the old size, and only invalidate measure if there is an actual delta beyond an epsilon.
   - Otherwise, call `InvalidateVisual()` only. This keeps rendering correct but bypasses parent invalidation.
2. **Avoid unconditional disposal in `ArrangeOverride()`**
   - Preserve the `TextLayout` built during measure when the constraint is unchanged and alignment does not depend on final bounds.
   - Only dispose/rebuild when alignment (center/right) or padding change the effective rendering rectangle.
3. **Incremental text layout cache**
   - Introduce an internal cache keyed by constraint + text properties so repeated measure passes with identical inputs reuse runs instead of reshaping.
   - Share the cache between `TextBlock` and `TextPresenter` to cover text editing scenarios.
4. **Fine-grained parent invalidation**
   - Extend `Layoutable.ChildDesiredSizeChanged` to pass the delta along. If the change is within a tolerance, parents can skip remeasure and only invalidate arrange, limiting the scope to layout flows that care about absolute size (e.g., `StackPanel` width).
   - Audit key panels (StackPanel, Grid, ItemsStackPanel) to ensure they short-circuit when child size changes are below thresholds.

## Phase 4 – Validation
- Re-run the `lols` benchmark and capture layout timings to confirm the reduction. Success criteria: matches or approaches owner-drawn performance, with layout counts stable across updates.
- Add automated performance checks to CI (baseline + warning threshold) so future changes to text layout code do not regress silently.
- Document the new behaviour and migration guidance in `docs/` so app developers understand when measure will still be triggered.

## Phase 5 – Optional Longer-Term Work
- Evaluate introducing a dedicated “visual-only” text primitive for high-frequency updates that intentionally opt out of layout invalidation (e.g., `GlyphRunControl`).
- Consider a broader layout invalidation redesign where nodes can flag themselves as “size-constant” for a period, allowing the manager to skip ancestor traversal entirely.
