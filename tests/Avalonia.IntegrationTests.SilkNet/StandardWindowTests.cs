using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.IntegrationTests.SilkNet;

public abstract class StandardWindowTests : IDisposable
{
    private const int ClientWidth = 200;
    private const int ClientHeight = 200;

    private Window? _window;

    protected StandardWindowTests()
    {
        string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
        System.IO.File.AppendAllText(logPath, $"[TEST] StandardWindowTests ctor, Dispatcher={Dispatcher.UIThread.GetHashCode()}, Thread={System.Threading.Thread.CurrentThread.ManagedThreadId}\n");
    }

    private Window Window
    {
        get
        {
            Assert.NotNull(_window);
            return _window;
        }
    }

    protected abstract WindowDecorations Decorations { get; }

    protected abstract bool HasCaption { get; }

    public static MatrixTheoryData<int, WindowState, bool> States
        => new(
            Enumerable.Range(0, 1),
            Enum.GetValues<WindowState>(),
            [true, false]);

    private async Task InitWindowAsync(int screenIndex, WindowState state, bool canResize)
    {
        string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
        System.IO.File.AppendAllText(logPath, "[TEST] InitWindowAsync: asserting _window is null\n");
        Assert.Null(_window);

        System.IO.File.AppendAllText(logPath, "[TEST] InitWindowAsync: creating new Window object\n");
        _window = new Window
        {
            CanResize = canResize,
            WindowState = state,
            WindowDecorations = Decorations,
            ExtendClientAreaToDecorationsHint = false,
            Width = ClientWidth,
            Height = ClientHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border
            {
                Background = Brushes.DodgerBlue,
                BorderBrush = Brushes.Yellow,
                BorderThickness = new Thickness(1)
            }
        };

        System.IO.File.AppendAllText(logPath, "[TEST] InitWindowAsync: accessing Screens property\n");
        var screens = _window.Screens;
        System.IO.File.AppendAllText(logPath, "[TEST] InitWindowAsync: accessing Screens.All property\n");
        var allScreens = screens.All;
        System.IO.File.AppendAllText(logPath, $"[TEST] InitWindowAsync: screens count = {allScreens.Count}\n");
        var screenCenter = allScreens[screenIndex].Bounds.Center;
        System.IO.File.AppendAllText(logPath, $"[TEST] InitWindowAsync: calculated screenCenter={screenCenter}\n");
        _window.Position = new PixelPoint(screenCenter.X - ClientWidth / 2, screenCenter.Y - ClientHeight / 2);

        System.IO.File.AppendAllText(logPath, "[TEST] Calling _window.Show()\n");
        _window.Show();

        System.IO.File.AppendAllText(logPath, "[TEST] Awaiting WhenLoadedAsync()\n");
        await Window.WhenLoadedAsync();
        System.IO.File.AppendAllText(logPath, "[TEST] Awaiting WhenLoadedAsync() completed!\n");
    }

    [Theory]
    [MemberData(nameof(States))]
    public Task Maximized_State_Fills_Screen_Working_Area(int screenIndex, WindowState initialState, bool canResize)
    {
        string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
        System.IO.File.AppendAllText(logPath, $"[TEST] Maximized_State_Fills_Screen_Working_Area called for screenIndex={screenIndex}, initialState={initialState}, canResize={canResize}\n");
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            System.IO.File.AppendAllText(logPath, $"[TEST-LAMBDA] Maximized_State_Fills_Screen_Working_Area lambda running on UI thread\n");
            await InitWindowAsync(screenIndex, initialState, canResize);

            if (initialState != WindowState.Maximized)
            {
                Window.WindowState = WindowState.Maximized;
                await Task.Delay(200);
            }

            var clientSize = Window.GetSilkNetClientSize();
            var screenWorkingArea = Window.GetScreenAtIndex(screenIndex).WorkingArea;

            bool hasCaption = HasCaption || System.OperatingSystem.IsMacOS();
            if (hasCaption)
            {
                Assert.Equal(screenWorkingArea.Size.Width, clientSize.Width);
                Assert.True(clientSize.Height < screenWorkingArea.Size.Height);
            }
            else
                Assert.Equal(screenWorkingArea.Size, clientSize);
        });
    }

    [Theory]
    [MemberData(nameof(States))]
    public Task FullScreen_State_Fills_Screen(int screenIndex, WindowState initialState, bool canResize)
    {
        if (System.OperatingSystem.IsMacOS())
        {
            return Task.CompletedTask;
        }

        string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
        System.IO.File.AppendAllText(logPath, $"[TEST] FullScreen_State_Fills_Screen called for screenIndex={screenIndex}, initialState={initialState}, canResize={canResize}\n");
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            System.IO.File.AppendAllText(logPath, $"[TEST-LAMBDA] FullScreen_State_Fills_Screen lambda running on UI thread\n");
            await InitWindowAsync(screenIndex, initialState, canResize);

            if (initialState != WindowState.FullScreen)
            {
                System.IO.File.AppendAllText(logPath, $"[TEST] Changing Window.WindowState to FullScreen (currently={Window.WindowState})\n");
                Window.WindowState = WindowState.FullScreen;
                System.IO.File.AppendAllText(logPath, "[TEST] Window.WindowState changed to FullScreen successfully\n");
                await Task.Delay(200);
            }

            var clientSize = Window.GetSilkNetClientSize();
            var screenBounds = Window.GetScreenAtIndex(screenIndex).Bounds;
            Assert.Equal(screenBounds.Width, clientSize.Width);
            Assert.Equal(screenBounds.Height, clientSize.Height);

            var windowBounds = Window.GetSilkNetWindowBounds();
            Assert.Equal(screenBounds, windowBounds);
        });
    }

    public void Dispose()
    {
        string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
        System.IO.File.AppendAllText(logPath, "[TEST] Dispose started\n");
        var window = _window;
        if (window != null)
        {
            var impl = window.PlatformImpl as Avalonia.SilkNet.WindowImpl;
            System.IO.File.AppendAllText(logPath, "[TEST] Dispose: calling window.Close() on UI thread\n");
            Dispatcher.UIThread.Post(() => window.Close());
            if (impl != null)
            {
                System.IO.File.AppendAllText(logPath, "[TEST] Dispose: waiting for DisposedTask\n");
                bool completed = impl.DisposedTask.Wait(3000);
                System.IO.File.AppendAllText(logPath, $"[TEST] Dispose: DisposedTask wait completed (success={completed})\n");
            }
            _window = null;
        }
        System.Threading.Thread.Sleep(200);
        System.IO.File.AppendAllText(logPath, "[TEST] Dispose finished\n");
    }

    public sealed class DecorationsFull : StandardWindowTests
    {
        protected override WindowDecorations Decorations
            => WindowDecorations.Full;

        protected override bool HasCaption
            => true;
    }

    public sealed class DecorationsBorderOnly : StandardWindowTests
    {
        protected override WindowDecorations Decorations
            => WindowDecorations.BorderOnly;

        protected override bool HasCaption
            => false;
    }

    public sealed class DecorationsNone : StandardWindowTests
    {
        protected override WindowDecorations Decorations
            => WindowDecorations.None;

        protected override bool HasCaption
            => false;
    }
}
