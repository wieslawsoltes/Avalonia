using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Utilities;

namespace Avalonia.Diagnostics;

internal static partial class Diagnostic
{
    private static Histogram<double>? s_compositorRender;
    private static Histogram<double>? s_compositorUpdate;
    private static Histogram<double>? s_layoutMeasure;
    private static Histogram<double>? s_layoutArrange;
    private static Histogram<double>? s_layoutRender;
    private static Histogram<double>? s_layoutInput;

    public static void InitMetrics()
    {
        var meter = new Meter("Avalonia.Diagnostic.Meter");
        s_compositorRender = meter.CreateHistogram<double>(Meters.CompositorRenderPassName,
            Meters.MillisecondsUnit, Meters.CompositorRenderPassDescription);
        s_compositorUpdate = meter.CreateHistogram<double>(Meters.CompositorUpdatePassName,
            Meters.MillisecondsUnit, Meters.CompositorUpdatePassDescription);
        s_layoutMeasure = meter.CreateHistogram<double>(Meters.LayoutMeasurePassName,
            Meters.MillisecondsUnit, Meters.LayoutMeasurePassDescription);
        s_layoutArrange = meter.CreateHistogram<double>(Meters.LayoutArrangePassName,
            Meters.MillisecondsUnit, Meters.LayoutArrangePassDescription);
        s_layoutRender = meter.CreateHistogram<double>(Meters.LayoutRenderPassName,
            Meters.MillisecondsUnit, Meters.LayoutRenderPassDescription);
        s_layoutInput = meter.CreateHistogram<double>(Meters.LayoutInputPassName,
            Meters.MillisecondsUnit, Meters.LayoutInputPassDescription);
        meter.CreateObservableUpDownCounter(Meters.TotalEventHandleCountName,
            () => Interactive.TotalHandlersCount, Meters.TotalEventHandleCountUnit, Meters.TotalEventHandleCountDescription);
        meter.CreateObservableUpDownCounter(Meters.TotalVisualCountName,
            () => Visual.RootedVisualChildrenCount, Meters.TotalVisualCountUnit, Meters.TotalVisualCountDescription);
        meter.CreateObservableUpDownCounter(Meters.TotalDispatcherTimerCountName,
            () => DispatcherTimer.ActiveTimersCount, Meters.TotalDispatcherTimerCountUnit, Meters.TotalDispatcherTimerCountDescription);
    }

    public static HistogramReportDisposable BeginCompositorRenderPass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.CompositorRenderPasses);
#endif
        return Begin(s_compositorRender);
    }
    public static HistogramReportDisposable BeginCompositorUpdatePass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.CompositorUpdatePasses);
#endif
        return Begin(s_compositorUpdate);
    }
    public static HistogramReportDisposable BeginLayoutMeasurePass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.LayoutMeasurePasses);
#endif
        return Begin(s_layoutMeasure);
    }
    public static HistogramReportDisposable BeginLayoutArrangePass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.LayoutArrangePasses);
#endif
        return Begin(s_layoutArrange);
    }
    public static HistogramReportDisposable BeginLayoutInputPass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.LayoutInputPasses);
#endif
        return Begin(s_layoutInput);
    }
    public static HistogramReportDisposable BeginLayoutRenderPass()
    {
#if AVALONIA_PERF_COUNTERS
        PerformanceCounters.Increment(PerformanceCounter.LayoutRenderPasses);
#endif
        return Begin(s_layoutRender);
    }

    private static HistogramReportDisposable Begin(Histogram<double>? histogram) => histogram is not null ? new(histogram) : default;

    internal readonly ref struct HistogramReportDisposable
    {
        private readonly Histogram<double> _histogram;
        private readonly long _timestamp;
        public HistogramReportDisposable(Histogram<double> histogram)
        {
            _histogram = histogram;
            if (histogram.Enabled) _timestamp = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (_timestamp > 0) _histogram.Record(StopwatchHelper.GetElapsedTimeMs(_timestamp));
        }
    }
}
