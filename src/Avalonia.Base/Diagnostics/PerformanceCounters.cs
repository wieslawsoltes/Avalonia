#if AVALONIA_PERF_COUNTERS
using System;
using System.Collections.Generic;
using System.Threading;

namespace Avalonia.Diagnostics;

internal enum PerformanceCounter
{
    PropertyNotifications,
    BindingExpressionsCreated,
    SelectorMatches,
    SelectorEvaluations,
    SelectorSuccessfulMatches,
    ResourceCacheProbes,
    ResourceCacheHits,
    ResourceCacheMisses,
    ResourceInvalidations,
    ShapeRequests,
    ShapeHits,
    ShapeProbeSkips,
    ShapeAdmissions,
    NativeGlyphRequests,
    NativeGlyphHits,
    NativeTextBlobsCreated,
    CompositorRenderPasses,
    CompositorUpdatePasses,
    LayoutMeasurePasses,
    LayoutArrangePasses,
    LayoutRenderPasses,
    LayoutInputPasses,
    TextLinesFinalized,
    DefaultLineMetricHits,
    ValidationProbe,
    Count
}

/// <summary>
/// Build-time-only counters. Normal assemblies contain neither this type nor calls/storage.
/// Snapshots are individually atomic, not a stop-the-world transaction across rendering threads.
/// </summary>
internal static class PerformanceCounters
{
    private static readonly long[] s_counts = new long[(int)PerformanceCounter.Count];

    internal static void Increment(PerformanceCounter counter) =>
        Interlocked.Increment(ref s_counts[(int)counter]);

    internal static Dictionary<string, long> Snapshot()
    {
        var result = new Dictionary<string, long>(s_counts.Length, StringComparer.Ordinal);
        for (var i = 0; i < s_counts.Length; ++i)
            result.Add(((PerformanceCounter)i).ToString(), Interlocked.Read(ref s_counts[i]));
        return result;
    }

    internal static void Reset()
    {
        for (var i = 0; i < s_counts.Length; ++i)
            Interlocked.Exchange(ref s_counts[i], 0);
    }
}
#endif
