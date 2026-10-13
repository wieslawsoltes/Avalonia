using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Moq;
using Xunit;

namespace Avalonia.Controls.UnitTests.Embedding;

public class HostedEmbeddableControlRootTests : ScopedTestBase
{
    [Fact]
    public void Hosted_Root_Inherits_Resources_Styles_And_DataContext()
    {
        using var app = UnitTestApplication.Start(TestServices.StyledWindow);
        var host = new Control { DataContext = "host context" };
        host.Resources["resource"] = "initial";
        host.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(Control.TagProperty, "host style") },
        });
        var owner = new Window { Content = host };
        owner.Show();
        var child = new Button();
        using var root = CreateRoot(host, child);

        Assert.Same(host, root.Host);
        Assert.Same(host, root.Parent);
        Assert.Contains(root, host.GetLogicalChildren());
        Assert.Equal("host context", child.DataContext);
        Assert.Equal("host style", child.Tag);
        Assert.Equal("initial", child.FindResource("resource"));
        var changed = 0;
        child.ResourcesChanged += (_, _) => ++changed;

        host.Resources["resource"] = "updated";
        host.DataContext = "new context";
        host.Styles.Clear();

        Assert.True(changed > 0);
        Assert.Equal("updated", child.FindResource("resource"));
        Assert.Equal("new context", child.DataContext);
        Assert.Null(child.Tag);
        owner.Close();
    }

    [Fact]
    public void Routed_Keyboard_Event_Crosses_Host_In_Both_Directions()
    {
        using var app = UnitTestApplication.Start(TestServices.FocusableWindow);
        var host = new Control();
        var owner = new Window { Content = host };
        owner.Show();
        var child = new Button();
        using var root = CreateRoot(host, child);
        var route = new List<string>();
        owner.AddHandler(InputElement.KeyDownEvent, (_, _) => route.Add("owner tunnel"), RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.KeyDownEvent, (_, _) => route.Add("host tunnel"), RoutingStrategies.Tunnel);
        child.AddHandler(InputElement.KeyDownEvent, (_, _) => route.Add("child"));
        host.AddHandler(InputElement.KeyDownEvent, (_, _) => route.Add("host bubble"));
        owner.AddHandler(InputElement.KeyDownEvent, (_, _) => route.Add("owner bubble"));

        child.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });

        Assert.Equal(new[] { "owner tunnel", "host tunnel", "child", "host bubble", "owner bubble" }, route);
        owner.Close();
    }

    [Fact]
    public void Native_Keyboard_Input_Resolves_Host_KeyBindings()
    {
        using var app = UnitTestApplication.Start(TestServices.FocusableWindow);
        var host = new Control();
        var owner = new Window { Content = host };
        owner.Show();
        var child = new Button();
        using var root = CreateRoot(host, child);
        var command = new Mock<ICommand>();
        command.Setup(x => x.CanExecute(It.IsAny<object?>())).Returns(true);
        host.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.A, KeyModifiers.Control), Command = command.Object });
        Assert.True(child.Focus());
        var keyboard = (KeyboardDevice)AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
        var input = new RawKeyEventArgs(keyboard, 0, root.InputRoot, RawKeyEventType.KeyDown,
            Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");

        root.PlatformImpl!.Input!(input);

        command.Verify(x => x.Execute(It.IsAny<object?>()), Times.Once);
        Assert.True(input.Handled);
        owner.Close();
    }

    [Fact]
    public void Keyboard_Focus_Within_Continues_Through_Host_And_Clears_When_Focus_Leaves()
    {
        using var app = UnitTestApplication.Start(TestServices.FocusableWindow);
        var host = new Control();
        var outside = new Button();
        var owner = new Window { Content = new StackPanel { Children = { host, outside } } };
        owner.Show();
        var child = new Button();
        using var root = CreateRoot(host, child);

        Assert.True(child.Focus());
        Assert.True(child.IsFocused);
        Assert.True(root.IsKeyboardFocusWithin);
        Assert.True(host.IsKeyboardFocusWithin);
        Assert.True(owner.IsKeyboardFocusWithin);
        Assert.Same(child, owner.FocusManager!.GetFocusedElement());

        Assert.True(outside.Focus());
        Assert.False(child.IsFocused);
        Assert.False(root.IsKeyboardFocusWithin);
        Assert.False(host.IsKeyboardFocusWithin);
        Assert.True(owner.IsKeyboardFocusWithin);
        owner.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disposal_Releases_Native_Impl_Ownership_And_Focus_Exactly_Once(bool platformClosesFirst)
    {
        using var app = UnitTestApplication.Start(TestServices.FocusableWindow);
        var host = new Control();
        var outside = new Button();
        var owner = new Window { Content = new StackPanel { Children = { host, outside } } };
        owner.Show();
        Assert.True(outside.Focus());
        var impl = MockWindowingPlatform.CreateWindowMock();
        var child = new Button();
        var root = new EmbeddableControlRoot(impl.Object, host) { Content = child };
        root.Prepare();
        Assert.True(child.Focus());
        var closed = 0;
        root.Closed += (_, _) => ++closed;

        if (platformClosesFirst)
        {
            impl.Object.LostFocus!();
            impl.Object.Closed!();
        }
        root.Dispose();
        root.Dispose();

        impl.Verify(x => x.Dispose(), platformClosesFirst ? Times.Never() : Times.Once());
        Assert.Equal(1, closed);
        Assert.Null(root.Host);
        Assert.Null(root.Parent);
        Assert.DoesNotContain(root, host.GetLogicalChildren());
        Assert.False(((ILogical)root).IsAttachedToLogicalTree);
        Assert.False(child.IsFocused);
        Assert.False(host.IsKeyboardFocusWithin);
        Assert.True(outside.IsFocused);
        owner.Close();
    }

    [Fact]
    public void Independently_Hosted_Root_Retains_Existing_Application_Scope()
    {
        using var app = UnitTestApplication.Start(TestServices.StyledWindow);
        var child = new Control();
        using var root = new EmbeddableControlRoot(MockWindowingPlatform.CreateWindowMock().Object) { Content = child };
        root.Prepare();
        Application.Current!.Resources["resource"] = "application";

        Assert.Null(root.Host);
        Assert.Null(root.Parent);
        Assert.Equal("application", child.FindResource("resource"));
    }

    private static EmbeddableControlRoot CreateRoot(Control host, Control child)
    {
        var result = new EmbeddableControlRoot(MockWindowingPlatform.CreateWindowMock().Object, host) { Content = child };
        result.Prepare();
        return result;
    }
}
