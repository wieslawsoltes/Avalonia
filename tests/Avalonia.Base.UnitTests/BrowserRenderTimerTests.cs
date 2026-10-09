using System;
using System.Collections.Concurrent;
using System.Threading;
using Avalonia.Browser;
using Avalonia.Browser.Interop;
using Avalonia.Browser.Rendering;
using Xunit;

namespace Avalonia.Base.UnitTests
{
    public class BrowserRenderTimerTests : IDisposable
    {
        private readonly SynchronizationContext? _original = SynchronizationContext.Current;

        public BrowserRenderTimerTests()
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
        public void Idle_Does_Not_Start_And_Active_Transitions_Are_Idempotent()
        {
            var timer = new BrowserRenderTimer(false);
            timer.Tick = null;
            timer.StartOnThisThread();
            Assert.Equal(0, TimerHelper.Starts);
            var calls = 0;
            timer.Tick = _ => ++calls;
            timer.Tick = _ => ++calls;
            timer.StartOnThisThread();
            TimerHelper.Fire(10);
            Assert.Equal(1, calls);
            Assert.Equal(1, TimerHelper.Starts);
            timer.Tick = null;
            timer.Tick = null;
            TimerHelper.Fire(20);
            Assert.Equal(1, TimerHelper.Stops);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Timestamp_Is_Preserved_And_Replacement_Is_Visible()
        {
            var timer = new BrowserRenderTimer(false);
            var actual = TimeSpan.Zero;
            timer.Tick = _ => throw new Exception("stale");
            timer.Tick = value => actual = value;
            TimerHelper.Fire(123.125);
            Assert.Equal(TimeSpan.FromMilliseconds(123.125), actual);
        }

        [Fact]
        public void Callback_Can_Sleep_And_Wake_Without_Losing_The_New_Callback()
        {
            var timer = new BrowserRenderTimer(false);
            var calls = 0;
            timer.Tick = _ =>
            {
                timer.Tick = null;
                timer.Tick = _ => ++calls;
            };
            TimerHelper.Fire(10);
            TimerHelper.Fire(20);
            Assert.Equal(1, calls);
            Assert.Equal(2, TimerHelper.Starts);
            Assert.Equal(1, TimerHelper.Stops);
        }

        [Fact]
        public void Worker_Updates_Are_Posted_Coalesced_And_Use_Latest_State()
        {
            BrowserWindowingPlatform.IsThreadingEnabled = true;
            var context = new QueuedContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var timer = new BrowserRenderTimer(true);
            timer.StartOnThisThread();
            OnOtherThread(() => { timer.Tick = _ => { }; timer.Tick = null; timer.Tick = _ => { }; });
            Assert.Equal(0, TimerHelper.Starts);
            Assert.Equal(1, context.Count);
            context.Drain();
            Assert.Equal(1, TimerHelper.Starts);
            OnOtherThread(() => timer.Tick = null);
            Assert.Equal(0, TimerHelper.Stops);
            context.Drain();
            Assert.Equal(1, TimerHelper.Stops);
            OnOtherThread(() => timer.Tick = _ => { });
            context.Drain();
            Assert.Equal(2, TimerHelper.Starts);
            Assert.True(timer.RunsInBackground);
        }

        [Fact]
        public void Worker_Observes_Callback_Published_Before_Initialization()
        {
            BrowserWindowingPlatform.IsThreadingEnabled = true;
            SynchronizationContext.SetSynchronizationContext(new QueuedContext());
            var timer = new BrowserRenderTimer(true);
            var calls = 0;
            OnOtherThread(() => timer.Tick = _ => ++calls);
            Assert.Equal(0, TimerHelper.Starts);
            timer.StartOnThisThread();
            TimerHelper.Fire(10);
            Assert.Equal(1, TimerHelper.Starts);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Worker_Without_Posting_Context_Retains_Safe_Polling_Fallback()
        {
            BrowserWindowingPlatform.IsThreadingEnabled = true;
            var timer = new BrowserRenderTimer(true);
            timer.StartOnThisThread();
            Assert.Equal(1, TimerHelper.Starts);
            var calls = 0;
            OnOtherThread(() => timer.Tick = _ => ++calls);
            TimerHelper.Fire(10);
            OnOtherThread(() => timer.Tick = null);
            TimerHelper.Fire(20);
            Assert.Equal(1, calls);
            Assert.Equal(0, TimerHelper.Stops);
        }

        [Fact]
        public void Queued_Sleep_Cannot_Stop_A_Subsequent_Owner_Thread_Wakeup()
        {
            BrowserWindowingPlatform.IsThreadingEnabled = true;
            var context = new QueuedContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var timer = new BrowserRenderTimer(true);
            timer.StartOnThisThread();
            timer.Tick = _ => { };
            OnOtherThread(() => timer.Tick = null);
            timer.Tick = _ => { };
            context.Drain();
            Assert.Equal(1, TimerHelper.Starts);
            Assert.Equal(0, TimerHelper.Stops);
        }

        private static void OnOtherThread(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(error);
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
            public int Count => _queue.Count;
            public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
            public void Drain() { while (_queue.TryDequeue(out var item)) item.Callback(item.State); }
        }
    }
}

// Only interop and host capability are stubbed. The timer itself is linked from production source.
namespace Avalonia.Browser
{
    internal static class BrowserWindowingPlatform
    {
        public static bool IsThreadingEnabled;
    }
}
namespace Avalonia.Browser.Interop
{
    internal static class TimerHelper
    {
        public static Action<double>? AnimationFrame;
        public static int Starts;
        public static int Stops;
        public static void RunAnimationFrames() => ++Starts;
        public static void StopAnimationFrames() => ++Stops;
        public static void Fire(double timestamp) => AnimationFrame?.Invoke(timestamp);
        public static void Reset() { AnimationFrame = null; Starts = Stops = 0; }
    }
}
