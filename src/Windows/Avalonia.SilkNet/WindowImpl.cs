using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using ProGPU.Backend;

namespace Avalonia.SilkNet
{
    public class WindowImpl : IWindowImpl
    {
        private Silk.NET.Windowing.IWindow _silkWindow;
        private readonly IMouseDevice _mouseDevice;
        private IInputContext? _inputContext;
        private IInputRoot? _owner;
        private double _scaling = 1.0;
        private Size _clientSize = new Size(1280, 800);
        private string? _title = "Avalonia Silk.NET Window";
        private PixelPoint _position = new PixelPoint(100, 100);
        private Avalonia.Controls.WindowState _windowState = Avalonia.Controls.WindowState.Normal;
        private SilkNetFramebufferManager _framebuffer;
        private bool _isShown;
        private WindowBorder? _restoredBorder;
        private bool _paintQueued;

        public WindowImpl()
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Constructor started\n");
            _mouseDevice = new MouseDevice();
            
            _scaling = 1.0;

            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Setting WindowOptions\n");
            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>((int)_clientSize.Width, (int)_clientSize.Height);
            options.Title = _title ?? "Avalonia Silk.NET Window";
            options.API = GraphicsAPI.None; // We use WebGPU manually
            options.VSync = false;
            options.Position = new Vector2D<int>((int)(_position.X / _scaling), (int)(_position.Y / _scaling));
            options.WindowBorder = WindowBorder.Resizable;

            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Creating SilkWindow via Window.Create\n");
            _silkWindow = Silk.NET.Windowing.Window.Create(options);
            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] SilkWindow created successfully\n");
            _silkWindow.Load += OnLoad;
            _silkWindow.Render += OnRender;
            _silkWindow.Resize += OnResize;
            _silkWindow.Move += OnMove;
            _silkWindow.Closing += OnClosing;

            _framebuffer = new SilkNetFramebufferManager(_silkWindow);
            
            // Set up platform handle
            Handle = new PlatformHandle(IntPtr.Zero, "SilkWindow");

            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Registering window with SilkNetPlatform\n");
            SilkNetPlatform.Instance.RegisterWindow(this);
            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Constructor finished\n");
        }

        public Silk.NET.Windowing.IWindow SilkWindow => _silkWindow;
        public IInputRoot Owner => _owner ?? throw new InvalidOperationException("Owner not set");

        public void SetInputRoot(IInputRoot inputRoot)
        {
            _owner = inputRoot;
        }

        private void OnLoad()
        {
            var oldScaling = _scaling;
            _scaling = GetWindowScaling();
            if (oldScaling != _scaling)
            {
                ScalingChanged?.Invoke(_scaling);
                _clientSize = new Size(_silkWindow.Size.X, _silkWindow.Size.Y);
                Resized?.Invoke(_clientSize, WindowResizeReason.Layout);
            }

            var wgpuContext = new WgpuContext();
            wgpuContext.Initialize(_silkWindow);

            _inputContext = _silkWindow.CreateInput();
            foreach (var keyboard in _inputContext.Keyboards)
            {
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
                keyboard.KeyChar += OnKeyChar;
            }
            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseMove += OnMouseMove;
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.Scroll += OnMouseScroll;
            }
        }

        private void OnRender(double delta)
        {
            Paint?.Invoke(new Rect(0, 0, ClientSize.Width, ClientSize.Height));
        }

        private void OnResize(Vector2D<int> size)
        {
            _clientSize = new Size(size.X, size.Y);
            Resized?.Invoke(_clientSize, WindowResizeReason.Layout);
        }

        private void OnMove(Vector2D<int> position)
        {
            var oldScaling = _scaling;
            _scaling = GetWindowScaling();
            _position = new PixelPoint((int)(position.X * _scaling), (int)(position.Y * _scaling));
            PositionChanged?.Invoke(_position);
            if (oldScaling != _scaling)
            {
                ScalingChanged?.Invoke(_scaling);
                _clientSize = new Size(_silkWindow.Size.X, _silkWindow.Size.Y);
                Resized?.Invoke(_clientSize, WindowResizeReason.Layout);
            }
        }

        private void OnClosing()
        {
            Closed?.Invoke();
            SilkNetPlatform.Instance.UnregisterWindow(this);
        }

        private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 pos)
        {
            var p = new Point(pos.X, pos.Y);
            var args = new RawPointerEventArgs(
                _mouseDevice,
                (ulong)DateTime.Now.Ticks,
                Owner,
                RawPointerEventType.Move,
                p,
                RawInputModifiers.None
            );
            Input?.Invoke(args);
        }

        private void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button)
        {
            var pos = mouse.Position;
            var p = new Point(pos.X, pos.Y);
            var type = button switch {
                Silk.NET.Input.MouseButton.Left => RawPointerEventType.LeftButtonDown,
                Silk.NET.Input.MouseButton.Right => RawPointerEventType.RightButtonDown,
                Silk.NET.Input.MouseButton.Middle => RawPointerEventType.MiddleButtonDown,
                _ => RawPointerEventType.LeftButtonDown
            };
            var args = new RawPointerEventArgs(
                _mouseDevice,
                (ulong)DateTime.Now.Ticks,
                Owner,
                type,
                p,
                RawInputModifiers.None
            );
            Input?.Invoke(args);
        }

        private void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button)
        {
            var pos = mouse.Position;
            var p = new Point(pos.X, pos.Y);
            var type = button switch {
                Silk.NET.Input.MouseButton.Left => RawPointerEventType.LeftButtonUp,
                Silk.NET.Input.MouseButton.Right => RawPointerEventType.RightButtonUp,
                Silk.NET.Input.MouseButton.Middle => RawPointerEventType.MiddleButtonUp,
                _ => RawPointerEventType.LeftButtonUp
            };
            var args = new RawPointerEventArgs(
                _mouseDevice,
                (ulong)DateTime.Now.Ticks,
                Owner,
                type,
                p,
                RawInputModifiers.None
            );
            Input?.Invoke(args);
        }

        private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
        {
            var pos = mouse.Position;
            var p = new Point(pos.X, pos.Y);
            var args = new RawMouseWheelEventArgs(
                _mouseDevice,
                (ulong)DateTime.Now.Ticks,
                Owner,
                p,
                new Avalonia.Vector(scroll.X, scroll.Y),
                RawInputModifiers.None
            );
            Input?.Invoke(args);
        }

        private void OnKeyDown(IKeyboard keyboard, Silk.NET.Input.Key key, int keyCode)
        {
            var avKey = MapKey(key);
            var args = new RawKeyEventArgs(
                SilkNetKeyboardDevice.Instance,
                (ulong)DateTime.Now.Ticks,
                Owner,
                RawKeyEventType.KeyDown,
                avKey,
                RawInputModifiers.None,
                PhysicalKey.None,
                null
            );
            Input?.Invoke(args);
        }

        private void OnKeyUp(IKeyboard keyboard, Silk.NET.Input.Key key, int keyCode)
        {
            var avKey = MapKey(key);
            var args = new RawKeyEventArgs(
                SilkNetKeyboardDevice.Instance,
                (ulong)DateTime.Now.Ticks,
                Owner,
                RawKeyEventType.KeyUp,
                avKey,
                RawInputModifiers.None,
                PhysicalKey.None,
                null
            );
            Input?.Invoke(args);
        }

        private void OnKeyChar(IKeyboard keyboard, char character)
        {
            var args = new RawTextInputEventArgs(
                SilkNetKeyboardDevice.Instance,
                (ulong)DateTime.Now.Ticks,
                Owner,
                character.ToString()
            );
            Input?.Invoke(args);
        }

        private Avalonia.Input.Key MapKey(Silk.NET.Input.Key key)
        {
            return key switch {
                Silk.NET.Input.Key.A => Avalonia.Input.Key.A,
                Silk.NET.Input.Key.B => Avalonia.Input.Key.B,
                Silk.NET.Input.Key.C => Avalonia.Input.Key.C,
                Silk.NET.Input.Key.D => Avalonia.Input.Key.D,
                Silk.NET.Input.Key.E => Avalonia.Input.Key.E,
                Silk.NET.Input.Key.F => Avalonia.Input.Key.F,
                Silk.NET.Input.Key.G => Avalonia.Input.Key.G,
                Silk.NET.Input.Key.H => Avalonia.Input.Key.H,
                Silk.NET.Input.Key.I => Avalonia.Input.Key.I,
                Silk.NET.Input.Key.J => Avalonia.Input.Key.J,
                Silk.NET.Input.Key.K => Avalonia.Input.Key.K,
                Silk.NET.Input.Key.L => Avalonia.Input.Key.L,
                Silk.NET.Input.Key.M => Avalonia.Input.Key.M,
                Silk.NET.Input.Key.N => Avalonia.Input.Key.N,
                Silk.NET.Input.Key.O => Avalonia.Input.Key.O,
                Silk.NET.Input.Key.P => Avalonia.Input.Key.P,
                Silk.NET.Input.Key.Q => Avalonia.Input.Key.Q,
                Silk.NET.Input.Key.R => Avalonia.Input.Key.R,
                Silk.NET.Input.Key.S => Avalonia.Input.Key.S,
                Silk.NET.Input.Key.T => Avalonia.Input.Key.T,
                Silk.NET.Input.Key.U => Avalonia.Input.Key.U,
                Silk.NET.Input.Key.V => Avalonia.Input.Key.V,
                Silk.NET.Input.Key.W => Avalonia.Input.Key.W,
                Silk.NET.Input.Key.X => Avalonia.Input.Key.X,
                Silk.NET.Input.Key.Y => Avalonia.Input.Key.Y,
                Silk.NET.Input.Key.Z => Avalonia.Input.Key.Z,
                Silk.NET.Input.Key.Number0 => Avalonia.Input.Key.D0,
                Silk.NET.Input.Key.Number1 => Avalonia.Input.Key.D1,
                Silk.NET.Input.Key.Number2 => Avalonia.Input.Key.D2,
                Silk.NET.Input.Key.Number3 => Avalonia.Input.Key.D3,
                Silk.NET.Input.Key.Number4 => Avalonia.Input.Key.D4,
                Silk.NET.Input.Key.Number5 => Avalonia.Input.Key.D5,
                Silk.NET.Input.Key.Number6 => Avalonia.Input.Key.D6,
                Silk.NET.Input.Key.Number7 => Avalonia.Input.Key.D7,
                Silk.NET.Input.Key.Number8 => Avalonia.Input.Key.D8,
                Silk.NET.Input.Key.Number9 => Avalonia.Input.Key.D9,
                Silk.NET.Input.Key.Enter => Avalonia.Input.Key.Enter,
                Silk.NET.Input.Key.Escape => Avalonia.Input.Key.Escape,
                Silk.NET.Input.Key.Backspace => Avalonia.Input.Key.Back,
                Silk.NET.Input.Key.Tab => Avalonia.Input.Key.Tab,
                Silk.NET.Input.Key.Space => Avalonia.Input.Key.Space,
                Silk.NET.Input.Key.Left => Avalonia.Input.Key.Left,
                Silk.NET.Input.Key.Up => Avalonia.Input.Key.Up,
                Silk.NET.Input.Key.Right => Avalonia.Input.Key.Right,
                Silk.NET.Input.Key.Down => Avalonia.Input.Key.Down,
                _ => Avalonia.Input.Key.None
            };
        }

        public Size ClientSize => _clientSize;
        public Size? FrameSize => _clientSize;
        public double RenderScaling => _scaling;
        public double DesktopScaling => _scaling;
        public IPlatformHandle Handle { get; }
        public Size MaxAutoSizeHint => new Size(1920, 1080);
        public IMouseDevice MouseDevice => _mouseDevice;

        public Avalonia.Controls.WindowState WindowState
        {
            get => _windowState;
            set
            {
                string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
                System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] WindowState setter called with value={value}, current _windowState={_windowState}\n");
                _windowState = value;
                if (_silkWindow != null)
                {
                    var targetState = value switch {
                        Avalonia.Controls.WindowState.Maximized => Silk.NET.Windowing.WindowState.Maximized,
                        Avalonia.Controls.WindowState.Minimized => Silk.NET.Windowing.WindowState.Minimized,
                        Avalonia.Controls.WindowState.FullScreen => Silk.NET.Windowing.WindowState.Fullscreen,
                        _ => Silk.NET.Windowing.WindowState.Normal
                    };

                    if (value == Avalonia.Controls.WindowState.Maximized ||
                        value == Avalonia.Controls.WindowState.FullScreen)
                    {
                        // Stash and set to Resizable before maximizing or fullscreening
                        if (!_restoredBorder.HasValue && _silkWindow.WindowBorder != WindowBorder.Resizable)
                        {
                            _restoredBorder = _silkWindow.WindowBorder;
                            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Stashing border {_restoredBorder.Value} and setting to Resizable\n");
                            _silkWindow.WindowBorder = WindowBorder.Resizable;
                        }
                    }
                    else
                    {
                        // Restore original border style if exiting maximized/fullscreen state
                        if (_restoredBorder.HasValue)
                        {
                            var borderToRestore = _restoredBorder.Value;
                            _restoredBorder = null;
                            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Restoring stashed border to {borderToRestore}\n");
                            _silkWindow.WindowBorder = borderToRestore;
                        }
                    }

                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Setting _silkWindow.WindowState = {targetState}\n");
                    _silkWindow.WindowState = targetState;
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] _silkWindow.WindowState set succeeded\n");
                }
            }
        }

        public WindowTransparencyLevel TransparencyLevel => WindowTransparencyLevel.None;

        public IPlatformRenderSurface[] Surfaces => new IPlatformRenderSurface[] { _framebuffer };

        public PixelPoint Position
        {
            get => _position;
            set
            {
                _position = value;
                if (_silkWindow != null)
                {
                    _silkWindow.Position = new Vector2D<int>((int)(value.X / _scaling), (int)(value.Y / _scaling));
                }
            }
        }

        public Action? Activated { get; set; }
        public Action? Deactivated { get; set; }
        public Func<WindowCloseReason, bool>? Closing { get; set; }
        public Action? Closed { get; set; }
        public Action<RawInputEventArgs>? Input { get; set; }
        public Action<Rect>? Paint { get; set; }
        public Action<Size, WindowResizeReason>? Resized { get; set; }
        public Action<double>? ScalingChanged { get; set; }
        public Action<PixelPoint>? PositionChanged { get; set; }
        public Action? LostFocus { get; set; }
        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

        public void Activate()
        {
            if (_silkWindow != null && _silkWindow.IsInitialized)
            {
                _silkWindow.Focus();
            }
            Activated?.Invoke();
        }

        public void Show(bool activate, bool isDialog)
        {
            if (!_isShown)
            {
                _isShown = true;
                _silkWindow.Initialize();
            }
            if (activate)
            {
                Activate();
            }
        }

        public void Hide()
        {
        }

        public void Close()
        {
            _silkWindow.Close();
        }

        public void SetTitle(string? title)
        {
            _title = title;
            if (_silkWindow != null)
            {
                _silkWindow.Title = title ?? "Avalonia Silk.NET Window";
            }
        }

        public void SetCursor(ICursorImpl? cursor)
        {
        }

        public void SetIcon(IWindowIconImpl? icon)
        {
        }

        public void Invalidate(Rect rect)
        {
            if (_paintQueued) return;
            _paintQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _paintQueued = false;
                Paint?.Invoke(new Rect(0, 0, ClientSize.Width, ClientSize.Height));
            }, DispatcherPriority.Render);
        }

        public Point PointToClient(PixelPoint point)
        {
            return new Point(point.X - Position.X, point.Y - Position.Y) / _scaling;
        }

        public Point PointToClient(Point point)
        {
            var posLogical = new Point(Position.X / _scaling, Position.Y / _scaling);
            return point - posLogical;
        }

        public PixelPoint PointToScreen(Point point)
        {
            var p = point * _scaling;
            return new PixelPoint(Position.X + (int)p.X, Position.Y + (int)p.Y);
        }

        public void SetEnabled(bool enable)
        {
        }

        public void SetTopmost(bool value)
        {
        }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
        }

        public void SetCanMinimize(bool value)
        {
        }

        public void SetCanMaximize(bool value)
        {
        }

        public void CanResize(bool value)
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] CanResize called with value={value}\n");
            if (_silkWindow != null)
            {
                var border = value ? WindowBorder.Resizable : WindowBorder.Fixed;
                if (_windowState == Avalonia.Controls.WindowState.Maximized ||
                    _windowState == Avalonia.Controls.WindowState.FullScreen)
                {
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Stashing border change to {border} (currently maximized/fullscreen)\n");
                    _restoredBorder = border;
                }
                else
                {
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Setting _silkWindow.WindowBorder = {border}\n");
                    _silkWindow.WindowBorder = border;
                }
            }
        }

        public void SetWindowDecorations(WindowDecorations value)
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] SetWindowDecorations called with value={value}\n");
            if (_silkWindow != null)
            {
                var border = value switch {
                    WindowDecorations.None => WindowBorder.Hidden,
                    WindowDecorations.BorderOnly => WindowBorder.Fixed,
                    _ => WindowBorder.Resizable
                };
                if (_windowState == Avalonia.Controls.WindowState.Maximized ||
                    _windowState == Avalonia.Controls.WindowState.FullScreen)
                {
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Stashing border change to {border} (currently maximized/fullscreen)\n");
                    _restoredBorder = border;
                }
                else
                {
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Setting _silkWindow.WindowBorder = {border}\n");
                    _silkWindow.WindowBorder = border;
                }
            }
        }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
        }

        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
        }

        public IPopupImpl? CreatePopup() => null;

        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
        {
        }

        public object? TryGetFeature(Type featureType)
        {
            if (featureType == typeof(IScreenImpl))
            {
                return AvaloniaLocator.Current.GetService<IScreenImpl>();
            }
            return null;
        }

        private readonly TaskCompletionSource _disposedTcs = new();
        public Task DisposedTask => _disposedTcs.Task;
        private bool _disposed;
        public bool IsDisposed => _disposed;

        public void Dispose()
        {
            string logPath = "/Users/wieslawsoltes/.gemini/antigravity/brain/a7990822-ca50-4be5-96d8-941456e6d9e6/test_run.log";
            if (_disposed) return;
            _disposed = true;
            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL] Dispose started for Window={GetHashCode()}\n");
            SilkNetPlatform.Instance.UnregisterWindow(this);

            // Unsubscribe window events to prevent any callbacks during disposal/destruction
            try
            {
                _silkWindow.Load -= OnLoad;
                _silkWindow.Render -= OnRender;
                _silkWindow.Resize -= OnResize;
                _silkWindow.Move -= OnMove;
                _silkWindow.Closing -= OnClosing;
            }
            catch {}
            
            var windowToDispose = _silkWindow;
            var inputContextToDispose = _inputContext;
            _inputContext = null;

            var tcs = _disposedTcs;
            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL] Posting async disposal work to UI thread\n");
            Dispatcher.UIThread.Post(async () =>
            {
                System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Async disposal work running on UI thread\n");
                try
                {
                    bool transitionNeeded = false;
                    try
                    {
                        if (windowToDispose.WindowState == Silk.NET.Windowing.WindowState.Fullscreen ||
                            windowToDispose.WindowState == Silk.NET.Windowing.WindowState.Maximized)
                        {
                            System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] Window state is {windowToDispose.WindowState}, restoring to Normal before disposing\n");
                            // Ensure the window has valid windowed bounds before exiting Fullscreen/Maximized
                            try
                            {
                                windowToDispose.Size = new Vector2D<int>(1280, 800);
                                windowToDispose.Position = new Vector2D<int>(100, 100);
                            }
                            catch {}
                            windowToDispose.WindowState = Silk.NET.Windowing.WindowState.Normal;
                            transitionNeeded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] Error transitioning state: {ex.Message}\n");
                    }

                    if (transitionNeeded)
                    {
                        System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Waiting for state transition to complete\n");
                        try
                        {
                            var glfw = Silk.NET.GLFW.Glfw.GetApi();
                            glfw.PollEvents();
                        }
                        catch {}
                        // Yield once for 300ms to allow Cocoa fullscreen transition animations to complete
                        await Task.Delay(300);
                        try
                        {
                            var glfw = Silk.NET.GLFW.Glfw.GetApi();
                            glfw.PollEvents();
                        }
                        catch {}
                    }

                    // Dispose WgpuContext first while the native window and its views/handles are still fully valid.
                    try
                    {
                        var context = WgpuContext.ActiveContexts.FirstOrDefault(c => c.Window == windowToDispose);
                        if (context != null)
                        {
                            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Disposing WgpuContext\n");
                            context.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] WgpuContext dispose exception: {ex.Message}\n");
                    }

                    try
                    {
                        if (inputContextToDispose != null)
                        {
                            System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Disposing input context\n");
                            inputContextToDispose.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] Input context dispose exception: {ex.Message}\n");
                    }
                    
                    try
                    {
                        System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Disposing _silkWindow\n");
                        windowToDispose.Dispose();
                        System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] _silkWindow disposed\n");
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] _silkWindow dispose exception: {ex.Message}\n");
                    }
                    
                    try
                    {
                        var glfw = Silk.NET.GLFW.Glfw.GetApi();
                        glfw.PollEvents();
                        // Yield once for 50ms to flush remaining native window events
                        await Task.Delay(50);
                        glfw.PollEvents();
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] PollEvents exception: {ex.Message}\n");
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(logPath, $"[WINDOWIMPL-DISPOSE] Global exception: {ex.Message}\n");
                }
                finally
                {
                    System.IO.File.AppendAllText(logPath, "[WINDOWIMPL-DISPOSE] Setting DisposedTask result\n");
                    tcs.TrySetResult();
                }
            });
        }

        // Missing interface members of IWindowImpl and ITopLevelImpl
        public void SetParent(IWindowImpl? parent) {}
        public void ShowTaskbarIcon(bool value) {}
        public void Resize(Size value, WindowResizeReason reason)
        {
            _clientSize = value;
            if (_silkWindow != null)
            {
                _silkWindow.Size = new Vector2D<int>((int)value.Width, (int)value.Height);
            }
        }
        public void Move(PixelPoint point) => Position = point;
        private bool _isClientAreaExtended;

        public void SetExtendClientAreaToDecorationsHint(bool extend)
        {
            _isClientAreaExtended = extend;
            ExtendClientAreaToDecorationsChanged?.Invoke(extend);
        }
        public void SetExtendClientAreaTitleBarHeightHint(double slope) {}
        public void SetFrameThemeVariant(PlatformThemeVariant themeVariant) {}
        public bool WindowStateGetterIsUsable => true;
        public Action<Avalonia.Controls.WindowState>? WindowStateChanged { get; set; }
        public Action? GotInputWhenDisabled { get; set; }
        public bool IsClientAreaExtendedToDecorations => _isClientAreaExtended;
        public Action<bool>? ExtendClientAreaToDecorationsChanged { get; set; }
        public bool NeedsManagedDecorations => _isClientAreaExtended;
        public Thickness ExtendedMargins => new Thickness();
        public Thickness OffScreenMargin => new Thickness();
        public Avalonia.Controls.Platform.PlatformRequestedDrawnDecoration RequestedDrawnDecorations => Avalonia.Controls.Platform.PlatformRequestedDrawnDecoration.None;
        public Avalonia.Rendering.Composition.Compositor Compositor => SilkNetPlatform.Compositor;
        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => new AcrylicPlatformCompensationLevels(1.0, 1.0, 1.0);

        private double GetPrimaryMonitorScale()
        {
            try
            {
                var glfw = Silk.NET.GLFW.Glfw.GetApi();
                unsafe
                {
                    bool initialized = glfw.Init();
                    if (initialized)
                    {
                        var monitors = glfw.GetMonitors(out int count);
                        if (count > 0)
                        {
                            float xscale, yscale;
                            glfw.GetMonitorContentScale(monitors[0], out xscale, out yscale);
                            return xscale;
                        }
                    }
                }
            }
            catch {}
            return 1.0;
        }

        private double GetWindowScaling()
        {
            try
            {
                var glfw = Silk.NET.GLFW.Glfw.GetApi();
                unsafe
                {
                    var monitors = glfw.GetMonitors(out int count);
                    if (count > 0)
                    {
                        var winX = _silkWindow.Position.X;
                        var winY = _silkWindow.Position.Y;
                        
                        var bestMonitor = monitors[0];
                        var minDistanceSq = double.MaxValue;
                        
                        for (int i = 0; i < count; i++)
                        {
                            var m = monitors[i];
                            glfw.GetMonitorPos(m, out int mx, out int my);
                            var vm = glfw.GetVideoMode(m);
                            if (vm != null)
                            {
                                int mw = vm->Width;
                                int mh = vm->Height;
                                
                                if (winX >= mx && winX < mx + mw && winY >= my && winY < my + mh)
                                {
                                    float xscale, yscale;
                                    glfw.GetMonitorContentScale(m, out xscale, out yscale);
                                    return xscale;
                                }
                                
                                var cx = mx + mw / 2.0;
                                var cy = my + mh / 2.0;
                                var dx = winX - cx;
                                var dy = winY - cy;
                                var distSq = dx * dx + dy * dy;
                                if (distSq < minDistanceSq)
                                {
                                    minDistanceSq = distSq;
                                    bestMonitor = m;
                                }
                            }
                        }
                        
                        float bxscale, byscale;
                        glfw.GetMonitorContentScale(bestMonitor, out bxscale, out byscale);
                        return bxscale;
                    }
                }
            }
            catch {}
            return 1.0;
        }
    }

    internal sealed class SilkNetKeyboardDevice : KeyboardDevice
    {
        public static SilkNetKeyboardDevice Instance { get; } = new();
        private SilkNetKeyboardDevice() {}
    }
}
