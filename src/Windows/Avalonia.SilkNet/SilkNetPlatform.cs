using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;

namespace Avalonia.SilkNet
{
    public class SilkNetPlatform : IWindowingPlatform, IPlatformIconLoader
    {
        private static readonly SilkNetPlatform s_instance = new();
        public static SilkNetPlatform Instance => s_instance;

        private readonly List<WindowImpl> _windows = new();
        private SilkNetDispatcherImpl? _dispatcher;
        private static Compositor? s_compositor;

        public static Compositor Compositor => s_compositor ?? throw new InvalidOperationException($"{nameof(SilkNetPlatform)} hasn't been initialized");

        public static void Initialize()
        {
            s_instance._dispatcher = new SilkNetDispatcherImpl();
            Avalonia.Threading.Dispatcher.InitializeUIThreadDispatcher(s_instance._dispatcher);

            var clipboardImpl = new SilkNetClipboardImpl();
            var clipboard = new Clipboard(clipboardImpl);

            var renderTimer = new UiThreadRenderTimer(60);
            var renderLoop = RenderLoop.FromTimer(renderTimer);
            AvaloniaLocator.CurrentMutable.Bind<IRenderLoop>().ToConstant(renderLoop);

            Console.WriteLine($"[DIAG] Current: {AvaloniaLocator.Current.GetHashCode()}");
            Console.WriteLine($"[DIAG] CurrentMutable: {AvaloniaLocator.CurrentMutable.GetHashCode()}");
            Console.WriteLine($"[DIAG] Service check: {AvaloniaLocator.Current.GetService<IRenderLoop>() != null}");

            var platformGraphics = AvaloniaLocator.Current.GetService<IPlatformGraphics>();
            s_compositor = new Compositor(platformGraphics);

            AvaloniaLocator.CurrentMutable
                .Bind<Compositor>().ToConstant(s_compositor)
                .Bind<IWindowingPlatform>().ToConstant(s_instance)
                .Bind<IPlatformIconLoader>().ToConstant(s_instance)
                .Bind<ICursorFactory>().ToConstant(new SilkNetCursorFactory())
                .Bind<IKeyboardDevice>().ToConstant(new KeyboardDevice())
                .Bind<IPlatformSettings>().ToConstant(new SilkNetPlatformSettings())
                .Bind<IClipboardImpl>().ToConstant(clipboardImpl)
                .Bind<IClipboard>().ToConstant(clipboard)
                .Bind<IScreenImpl>().ToConstant(new SilkNetScreenImpl());
        }

        public void RegisterWindow(WindowImpl window)
        {
            lock (_windows)
            {
                _windows.Add(window);
            }
        }

        public void UnregisterWindow(WindowImpl window)
        {
            lock (_windows)
            {
                _windows.Remove(window);
            }
        }

        public void DoEvents()
        {
            try
            {
                var glfw = Silk.NET.GLFW.Glfw.GetApi();
                glfw.PollEvents();
            }
            catch {}

            WindowImpl[] windowsToProcess;
            lock (_windows)
            {
                windowsToProcess = _windows.ToArray();
            }

            foreach (var win in windowsToProcess)
            {
                if (!win.IsDisposed && win.SilkWindow != null && win.SilkWindow.IsInitialized)
                {
                    try
                    {
                        win.SilkWindow.DoEvents();
                    }
                    catch {}
                }
            }
        }

        public ITrayIconImpl CreateTrayIcon() => null!;

        public IWindowImpl CreateWindow()
        {
            return new WindowImpl();
        }

        public ITopLevelImpl CreateEmbeddableTopLevel()
        {
            return new WindowImpl();
        }

        public IWindowImpl CreateEmbeddableWindow()
        {
            var embedded = new WindowImpl();
            embedded.Show(false, false);
            return embedded;
        }

        public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> zOrder)
        {
            for (int i = 0; i < windows.Length; i++)
            {
                zOrder[i] = i;
            }
        }

        public IWindowIconImpl LoadIcon(IBitmapImpl bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream);
                return LoadIcon(stream);
            }
        }

        public IWindowIconImpl LoadIcon(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new SilkNetIconStub(ms);
        }

        public IWindowIconImpl LoadIcon(string fileName)
        {
            using (var file = File.Open(fileName, FileMode.Open, FileAccess.Read))
                return LoadIcon(file);
        }
    }

    internal class SilkNetIconStub : IWindowIconImpl
    {
        private readonly MemoryStream _ms;

        public SilkNetIconStub(MemoryStream stream)
        {
            _ms = stream;
        }

        public void Save(Stream outputStream)
        {
            _ms.Position = 0;
            _ms.CopyTo(outputStream);
        }
    }

    public class SilkNetCursorFactory : ICursorFactory
    {
        public ICursorImpl GetCursor(StandardCursorType cursorType) => null!;
        public ICursorImpl CreateCursor(Avalonia.Media.Imaging.Bitmap cursor, PixelPoint hotSpot) => null!;
    }

    public class SilkNetPlatformSettings : IPlatformSettings
    {
        public Size GetTapSize(PointerType type) => new Size(4, 4);
        public Size GetDoubleTapSize(PointerType type) => new Size(4, 4);
        public TimeSpan GetDoubleTapTime(PointerType type) => TimeSpan.FromMilliseconds(500);
        public TimeSpan HoldWaitDuration => TimeSpan.FromMilliseconds(500);
        
        public PlatformHotkeyConfiguration HotkeyConfiguration { get; } = new(KeyModifiers.Control);

        public PlatformColorValues GetColorValues()
        {
            return new PlatformColorValues
            {
                ThemeVariant = PlatformThemeVariant.Light
            };
        }

        public event EventHandler<PlatformColorValues>? ColorValuesChanged;
    }
}

namespace Avalonia
{
    public static class SilkNetApplicationExtensions
    {
        public static AppBuilder UseSilkNet(this AppBuilder builder)
        {
            return builder
                .UseStandardRuntimePlatformSubsystem()
                .UseWindowingSubsystem(() => SilkNet.SilkNetPlatform.Initialize(), "SilkNet");
        }
    }
}
