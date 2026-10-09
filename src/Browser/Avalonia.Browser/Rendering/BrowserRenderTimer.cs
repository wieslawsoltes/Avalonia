using System;
using System.Threading;
using Avalonia.Browser.Interop;
using Avalonia.Rendering;

namespace Avalonia.Browser.Rendering;

internal class BrowserRenderTimer : IRenderTimer
{
    private Action<TimeSpan>? _tick;
    private bool _started;
    private bool _active;
    private SynchronizationContext? _context;
    private int _timerThreadId;
    private int _updateQueued;
    private bool _pollingFallback;

    public BrowserRenderTimer(bool isBackground) => RunsInBackground = isBackground;

    public bool RunsInBackground { get; }

    public Action<TimeSpan>? Tick
    {
        set
        {
            // Publish before starting RAF so a synchronous wakeup cannot observe the old callback.
            Volatile.Write(ref _tick, value);
            if (!BrowserWindowingPlatform.IsThreadingEnabled)
                StartOnThisThread();
            if (Volatile.Read(ref _started)) RequestTimerUpdate();
        }
        get => Volatile.Read(ref _tick);
    }

    public void StartOnThisThread()
    {
        if (_started) return;
        _timerThreadId = Environment.CurrentManagedThreadId;
        _context = SynchronizationContext.Current;
        // JS workers normally provide their event-loop context. Keep the previous polling
        // behavior for an unusual host without one rather than lose cross-thread wakeups.
        _pollingFallback = BrowserWindowingPlatform.IsThreadingEnabled && _context is null;
        TimerHelper.AnimationFrame += RenderFrameCallback;
        Volatile.Write(ref _started, true);
        UpdateTimer();
    }

    private void RequestTimerUpdate()
    {
        if (Environment.CurrentManagedThreadId == _timerThreadId)
            UpdateTimer();
        else if (_context is { } context && Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            // Never synchronously wait for the JS thread while the render-loop lock is held.
            context.Post(static state =>
            {
                var timer = (BrowserRenderTimer)state!;
                Volatile.Write(ref timer._updateQueued, 0);
                timer.UpdateTimer();
            }, this);
        }
    }

    private void UpdateTimer()
    {
        var active = _pollingFallback || Volatile.Read(ref _tick) is not null;
        if (_active == active) return;
        _active = active;
        if (active) TimerHelper.RunAnimationFrames();
        else TimerHelper.StopAnimationFrames();
    }

    private void RenderFrameCallback(double timestamp) =>
        Volatile.Read(ref _tick)?.Invoke(TimeSpan.FromMilliseconds(timestamp));
}
