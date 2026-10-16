using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Browser;
using Avalonia.Browser.Interop;
using Avalonia.Browser.Rendering;
using Avalonia.Rendering;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class BrowserRenderLoopIntegrationTests : IDisposable
{
    private readonly SynchronizationContext? _original = SynchronizationContext.Current;

    public BrowserRenderLoopIntegrationTests()
    {
        TimerHelper.Reset();
        BrowserWindowingPlatform.IsThreadingEnabled = false;
        SynchronizationContext.SetSynchronizationContext(null);
    }

    public void Dispose()
    {
        TimerHelper.Reset();
        BrowserWindowingPlatform.IsThreadingEnabled = false;
        SynchronizationContext.SetSynchronizationContext(_original);
    }

    [Fact]
    public void Base_Synchronization_Context_Uses_Owner_Thread_Polling()
    {
        BrowserWindowingPlatform.IsThreadingEnabled = true;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        var timer = new BrowserRenderTimer(true);
        timer.StartOnThisThread();
        Assert.Equal(1, TimerHelper.Starts);
        var calls = 0;
        Assert.Null(OnOtherThread(() => timer.Tick = _ => ++calls));
        TimerHelper.Fire(1);
        Assert.Equal(1, calls);
        Assert.Null(OnOtherThread(() => timer.Tick = null));
        TimerHelper.Fire(2);
        Assert.Equal(1, calls);
        Assert.Equal(0, TimerHelper.Stops);
        Assert.Equal(1, TimerHelper.Starts);
    }

    [Fact]
    public void Failed_Post_Propagates_And_Does_Not_Poison_A_Later_Update()
    {
        BrowserWindowingPlatform.IsThreadingEnabled = true;
        var context = new RetryContext();
        SynchronizationContext.SetSynchronizationContext(context);
        var timer = new BrowserRenderTimer(true);
        timer.StartOnThisThread();
        var error = OnOtherThread(() => timer.Tick = _ => { });
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, TimerHelper.Starts);
        Assert.Null(OnOtherThread(() => timer.Tick = _ => { }));
        context.Drain();
        Assert.Equal(1, TimerHelper.Starts);
        Assert.Null(OnOtherThread(() => timer.Tick = null));
        context.Drain();
        Assert.Equal(1, TimerHelper.Stops);
        Assert.Equal(3, context.Posts);
    }

    [Fact]
    public void Real_Render_Loop_Runs_Continuous_Work_Then_Sleeps_And_Wakes()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var timer = new BrowserRenderTimer(false);
        var loop = RenderLoop.FromTimer(timer);
        var task = new RenderTask { WantsNext = true };
        loop.Add(task);
        try
        {
            for (var i = 0; i < 20; ++i) TimerHelper.Fire(i * 16.5);
            Assert.Equal(20, task.Calls);
            Assert.Equal(1, TimerHelper.Starts);
            Assert.Equal(0, TimerHelper.Stops);
            task.WantsNext = false;
            TimerHelper.Fire(340);
            Assert.Equal(21, task.Calls);
            Assert.Equal(1, TimerHelper.Stops);
            TimerHelper.Fire(350); // A stale, dispatched RAF must not run a sleeping task.
            Assert.Equal(21, task.Calls);
            loop.Wakeup();
            Assert.Equal(2, TimerHelper.Starts);
            TimerHelper.Fire(360);
            Assert.Equal(22, task.Calls);
            Assert.Equal(2, TimerHelper.Stops);
        }
        finally { loop.Remove(task); }
    }

    [Fact]
    public void Wakeup_During_Render_Is_Not_Lost_When_Task_Returns_False()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var timer = new BrowserRenderTimer(false);
        var loop = RenderLoop.FromTimer(timer);
        var task = new RenderTask { DuringRender = loop.Wakeup };
        loop.Add(task);
        try
        {
            TimerHelper.Fire(10);
            Assert.Equal(1, task.Calls);
            Assert.Equal(0, TimerHelper.Stops);
            task.DuringRender = null;
            TimerHelper.Fire(20);
            Assert.Equal(2, task.Calls);
            Assert.Equal(1, TimerHelper.Stops);
        }
        finally { loop.Remove(task); }
    }

    [Fact]
    public void Removing_Last_Task_Stops_And_Readding_Restarts_The_Chain()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var timer = new BrowserRenderTimer(false);
        var loop = RenderLoop.FromTimer(timer);
        var task = new RenderTask { WantsNext = true };
        for (var i = 0; i < 20; ++i)
        {
            loop.Add(task);
            TimerHelper.Fire(i);
            loop.Remove(task);
            TimerHelper.Fire(i + 0.25);
        }
        Assert.Equal(20, task.Calls);
        Assert.Equal(20, TimerHelper.Starts);
        Assert.Equal(20, TimerHelper.Stops);
    }

    private static Exception? OnOtherThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        return error;
    }

    private sealed class RenderTask : IRenderLoopTask
    {
        public int Calls;
        public bool WantsNext;
        public Action? DuringRender;
        public bool Render() { ++Calls; DuringRender?.Invoke(); return WantsNext; }
    }

    private sealed class RetryContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (++Posts == 1) throw new InvalidOperationException("post failed");
            _queue.Enqueue((callback, state));
        }
        // Every producer is joined before drain; the queue is never accessed concurrently.
        public void Drain() { while (_queue.Count > 0) { var item = _queue.Dequeue(); item.Callback(item.State); } }
    }
}
