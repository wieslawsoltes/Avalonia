using System;
using System.Threading;
using Avalonia.Platform;

namespace Avalonia.Media.TextFormatting;

// Opt-in only for built-in backends whose glyph geometry is immutable for a typeface/run.
internal interface IDefaultLineMetricsCacheBackend { }

internal sealed class DefaultTextLineMetricsCache
{
    private Entry? _entry;

    internal bool TryGet(IPlatformRenderInterface renderer, out TextLineMetrics metrics,
        out Rect inkBounds, out Rect bounds)
    {
        var entry = Volatile.Read(ref _entry);
        if (entry is not null && entry.Renderer.TryGetTarget(out var owner) && ReferenceEquals(owner, renderer))
        {
            metrics = entry.Metrics;
            inkBounds = entry.InkBounds;
            bounds = entry.Bounds;
            return true;
        }
        metrics = default;
        inkBounds = bounds = default;
        return false;
    }

    internal void Store(IPlatformRenderInterface renderer, TextLineMetrics metrics, Rect inkBounds, Rect bounds) =>
        Volatile.Write(ref _entry, new Entry(new WeakReference<IPlatformRenderInterface>(renderer), metrics, inkBounds, bounds));

    private sealed record Entry(WeakReference<IPlatformRenderInterface> Renderer,
        TextLineMetrics Metrics, Rect InkBounds, Rect Bounds);
}
