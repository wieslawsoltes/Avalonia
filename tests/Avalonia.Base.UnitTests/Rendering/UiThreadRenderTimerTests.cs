using System;
using System.Reflection;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Rendering;

public class UiThreadRenderTimerTests
{
    [Fact]
    public void Starts_With_Configured_Frame_Interval()
    {
        using var application = UnitTestApplication.Start();
        var timer = new UiThreadRenderTimer(60);
        var startCore = typeof(UiThreadRenderTimer).GetMethod(
            "StartCore",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        using var subscription = (IDisposable)startCore.Invoke(
            timer,
            new object[] { (Action<TimeSpan>)(_ => { }) })!;

        var dispatcherTimer = (DispatcherTimer)subscription.GetType()
            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(subscription)!;

        Assert.Equal(TimeSpan.FromSeconds(1.0 / 60), dispatcherTimer.Interval);
    }
}
