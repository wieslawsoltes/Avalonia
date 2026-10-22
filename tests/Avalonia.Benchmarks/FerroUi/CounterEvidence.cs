#if AVALONIA_PERF_COUNTERS
#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Avalonia.Diagnostics;

namespace Avalonia.Benchmarks.FerroUi;

internal static class CounterEvidence
{
    public static int Main(string[] args)
    {
        PerformanceCounters.Reset();
        var result = ExtendedPerformanceProgram.Main(args);
        if (result != 0) return result;
        var snapshot = PerformanceCounters.Snapshot();
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(args[0] + ".counters.json", json);
        Console.WriteLine(json);
        foreach (var required in new[]
        {
            PerformanceCounter.PropertyNotifications, PerformanceCounter.BindingExpressionsCreated,
            PerformanceCounter.SelectorEvaluations, PerformanceCounter.ResourceCacheHits,
            PerformanceCounter.ShapeRequests, PerformanceCounter.ShapeHits, PerformanceCounter.ShapeProbeSkips,
            PerformanceCounter.NativeGlyphRequests, PerformanceCounter.NativeGlyphHits,
            PerformanceCounter.NativeTextBlobsCreated, PerformanceCounter.TextLinesFinalized,
            PerformanceCounter.DefaultLineMetricHits
        })
        {
            if (snapshot[required.ToString()] == 0)
            {
                Console.Error.WriteLine($"The real workload did not exercise {required}.");
                return 1;
            }
        }
        return 0;
    }
}
#endif
