using System;
using Avalonia.Platform;
using Avalonia.Utilities;

namespace Avalonia.Media.TextFormatting;

internal partial class TextLineImpl
{
    private TextLineMetrics GetOrCreateLineMetrics()
    {
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.TextLinesFinalized);
#endif
        if (_textRuns.Length != 1 || _textRuns[0] is not ShapedTextRun run || run.HasGlyphRun ||
            run.ShapedBuffer.SharedMetrics is not { } cache || run.BidiLevel != 0 || HasCollapsed ||
            _paragraphProperties is not GenericTextParagraphProperties paragraph ||
            paragraph.FlowDirection != FlowDirection.LeftToRight ||
            paragraph.TextAlignment is not (TextAlignment.Left or TextAlignment.Start) ||
            paragraph.TextWrapping != TextWrapping.NoWrap ||
            !(paragraph.LineHeight == 0 || double.IsNaN(paragraph.LineHeight)) ||
            paragraph.LineSpacing != 0 || paragraph.Indent != 0 || paragraph.LetterSpacing != 0 ||
            run.Properties.GetType() != typeof(GenericTextRunProperties) ||
            paragraph.DefaultTextRunProperties.GetType() != typeof(GenericTextRunProperties) ||
            run.Properties.BaselineAlignment != BaselineAlignment.Baseline ||
            !ReferenceEquals(run.Properties.CachedGlyphTypeface, run.ShapedBuffer.GlyphTypeface) ||
            !ReferenceEquals(paragraph.DefaultTextRunProperties.CachedGlyphTypeface, run.ShapedBuffer.GlyphTypeface) ||
            run.Properties.FontRenderingEmSize != run.ShapedBuffer.FontRenderingEmSize ||
            paragraph.DefaultTextRunProperties.FontRenderingEmSize != run.ShapedBuffer.FontRenderingEmSize ||
            AvaloniaLocator.Current.GetService<IPlatformRenderInterface>() is not { } renderer ||
            renderer is not IDefaultLineMetricsCacheBackend)
            return CreateLineMetrics();

        // A metrics hit must not hide disposal of the source typeface.
        _ = run.ShapedBuffer.GlyphTypeface.TextShaperTypeface;
        if (cache.TryGet(renderer, out var metrics, out _inkBounds, out _bounds))
        {
#if AVALONIA_PERF_COUNTERS
            Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.DefaultLineMetricHits);
#endif
            // Width-independent natural geometry is shared. Overflow still depends on this line.
            return metrics with { HasOverflowed = MathUtilities.GreaterThan(metrics.Width, _paragraphWidth) };
        }
        metrics = CreateLineMetrics();
        cache.Store(renderer, metrics, _inkBounds, _bounds);
        return metrics;
    }
}
