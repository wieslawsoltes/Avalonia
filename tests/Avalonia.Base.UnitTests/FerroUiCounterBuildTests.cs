using System;
using Xunit;
#if AVALONIA_PERF_COUNTERS
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Styling;
#endif

namespace Avalonia.Base.UnitTests;

public class FerroUiCounterBuildTests
{
    [Fact]
    public void Counter_Type_Exists_Only_In_An_Instrumented_Build()
    {
        var type = typeof(AvaloniaObject).Assembly.GetType("Avalonia.Diagnostics.PerformanceCounters");
#if AVALONIA_PERF_COUNTERS
        Assert.NotNull(type);
        PerformanceCounters.Reset();
        Parallel.For(0, 4096, _ => PerformanceCounters.Increment(PerformanceCounter.ValidationProbe));
        Assert.Equal(4096, PerformanceCounters.Snapshot()[nameof(PerformanceCounter.ValidationProbe)]);
        var selector = default(Selector).Is<Button>();
        Assert.True(selector.Match(new Button(), subscribe: false).IsMatch);
        Assert.True(PerformanceCounters.Snapshot()[nameof(PerformanceCounter.SelectorEvaluations)] > 0);
        PerformanceCounters.Reset();
        Assert.Equal(0, PerformanceCounters.Snapshot()[nameof(PerformanceCounter.ValidationProbe)]);
#else
        Assert.Null(type);
#endif
    }
}
