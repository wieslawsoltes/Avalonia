using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Input;

public class PrimaryMouseCaptureTests : ScopedTestBase
{
    [Fact]
    public void Capture_And_Release_Use_The_Real_Primary_Pointer()
    {
        using var app = UnitTestApplication.Start(TestServices.RealFocus);
        var child = new Control();
        var root = new TestRoot(child);
        var lost = 0;
        child.PointerCaptureLost += (_, _) => ++lost;

        Assert.True(child.CaptureMouse());
        Assert.Same(child, MouseDevice.Primary.Pointer.Captured);
        child.ReleaseMouseCapture();

        Assert.Null(MouseDevice.Primary.Pointer.Captured);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void Release_Does_Not_Steal_Another_Elements_Capture()
    {
        using var app = UnitTestApplication.Start(TestServices.RealFocus);
        var first = new Control();
        var second = new Control();
        var root = new TestRoot(new Panel { Children = { first, second } });
        Assert.True(second.CaptureMouse());

        first.ReleaseMouseCapture();

        Assert.Same(second, MouseDevice.Primary.Pointer.Captured);
        root.Child = null;
        Assert.Same(root, MouseDevice.Primary.Pointer.Captured);
        root.ReleaseMouseCapture();
        Assert.Null(MouseDevice.Primary.Pointer.Captured);
    }

    [Fact]
    public void Capturing_Primary_Mouse_Does_Not_Change_Touch_Capture()
    {
        using var app = UnitTestApplication.Start(TestServices.RealFocus);
        var first = new Control();
        var second = new Control();
        var root = new TestRoot(new Panel { Children = { first, second } });
        using var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        touch.Capture(first);

        Assert.True(second.CaptureMouse());
        Assert.Same(first, touch.Captured);
        second.ReleaseMouseCapture();
        Assert.Same(first, touch.Captured);
    }

    [Fact]
    public void Detached_Hidden_And_Disabled_Elements_Cannot_Capture()
    {
        using var app = UnitTestApplication.Start(TestServices.RealFocus);
        var child = new Control();
        Assert.False(child.CaptureMouse());
        var root = new TestRoot(child);
        child.IsVisible = false;
        Assert.False(child.CaptureMouse());
        child.IsVisible = true;
        child.IsEnabled = false;
        Assert.False(child.CaptureMouse());
        Assert.Null(MouseDevice.Primary.Pointer.Captured);
    }
}
