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
        private char? _pendingHighSurrogate;
        private SilkNetCursorImpl? _cursor;

        public WindowImpl()
        {
            _mouseDevice = new MouseDevice();
            
            _scaling = 1.0;

            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>((int)_clientSize.Width, (int)_clientSize.Height);
            options.Title = _title ?? "Avalonia Silk.NET Window";
            options.API = GraphicsAPI.None; // We use WebGPU manually
            options.VSync = false;
            options.Position = new Vector2D<int>((int)(_position.X / _scaling), (int)(_position.Y / _scaling));
            options.WindowBorder = WindowBorder.Resizable;

            _silkWindow = Silk.NET.Windowing.Window.Create(options);
            _silkWindow.Load += OnLoad;
            _silkWindow.Render += OnRender;
            _silkWindow.Resize += OnResize;
            _silkWindow.Move += OnMove;
            _silkWindow.Closing += OnClosing;
            _silkWindow.FocusChanged += OnFocusChanged;

            _framebuffer = new SilkNetFramebufferManager(_silkWindow);
            
            // Set up platform handle
            Handle = new PlatformHandle(IntPtr.Zero, "SilkWindow");

            SilkNetPlatform.Instance.RegisterWindow(this);
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
                ApplyCursor(mouse.Cursor, _cursor);
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

        private void OnFocusChanged(bool focused)
        {
            if (focused)
            {
                Activated?.Invoke();
            }
            else
            {
                Deactivated?.Invoke();
                LostFocus?.Invoke();
            }
        }

        private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 pos)
        {
            var p = new Point(pos.X, pos.Y);
            var args = new RawPointerEventArgs(
                _mouseDevice,
                GetTimestamp(),
                Owner,
                RawPointerEventType.Move,
                p,
                SilkNetInputMappings.GetPointerModifiers(_inputContext, mouse)
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
                Silk.NET.Input.MouseButton.Button4 => RawPointerEventType.XButton1Down,
                Silk.NET.Input.MouseButton.Button5 => RawPointerEventType.XButton2Down,
                _ => (RawPointerEventType?)null
            };
            if (type == null) return;
            var args = new RawPointerEventArgs(
                _mouseDevice,
                GetTimestamp(),
                Owner,
                type.Value,
                p,
                SilkNetInputMappings.GetPointerModifiers(_inputContext, mouse, button, eventButtonIsDown: true)
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
                Silk.NET.Input.MouseButton.Button4 => RawPointerEventType.XButton1Up,
                Silk.NET.Input.MouseButton.Button5 => RawPointerEventType.XButton2Up,
                _ => (RawPointerEventType?)null
            };
            if (type == null) return;
            var args = new RawPointerEventArgs(
                _mouseDevice,
                GetTimestamp(),
                Owner,
                type.Value,
                p,
                SilkNetInputMappings.GetPointerModifiers(_inputContext, mouse, button)
            );
            Input?.Invoke(args);
        }

        private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
        {
            var pos = mouse.Position;
            var p = new Point(pos.X, pos.Y);
            var args = new RawMouseWheelEventArgs(
                _mouseDevice,
                GetTimestamp(),
                Owner,
                p,
                new Avalonia.Vector(scroll.X, scroll.Y),
                SilkNetInputMappings.GetPointerModifiers(_inputContext, mouse)
            );
            Input?.Invoke(args);
        }

        private void OnKeyDown(IKeyboard keyboard, Silk.NET.Input.Key key, int keyCode)
        {
            var mapping = SilkNetInputMappings.MapKey(key);
            var args = new RawKeyEventArgs(
                SilkNetKeyboardDevice.Instance,
                GetTimestamp(),
                Owner,
                RawKeyEventType.KeyDown,
                mapping.Key,
                SilkNetInputMappings.GetKeyboardModifiers(keyboard, key, eventKeyIsDown: true),
                mapping.PhysicalKey,
                null
            );
            Input?.Invoke(args);
        }

        private void OnKeyUp(IKeyboard keyboard, Silk.NET.Input.Key key, int keyCode)
        {
            var mapping = SilkNetInputMappings.MapKey(key);
            var args = new RawKeyEventArgs(
                SilkNetKeyboardDevice.Instance,
                GetTimestamp(),
                Owner,
                RawKeyEventType.KeyUp,
                mapping.Key,
                SilkNetInputMappings.GetKeyboardModifiers(keyboard),
                mapping.PhysicalKey,
                null
            );
            Input?.Invoke(args);
        }

        private void OnKeyChar(IKeyboard keyboard, char character)
        {
            if (char.IsHighSurrogate(character))
            {
                FlushPendingHighSurrogate();
                _pendingHighSurrogate = character;
                return;
            }

            string text;
            if (char.IsLowSurrogate(character) && _pendingHighSurrogate.HasValue)
            {
                text = string.Concat(_pendingHighSurrogate.Value, character);
                _pendingHighSurrogate = null;
            }
            else
            {
                FlushPendingHighSurrogate();
                text = character.ToString();
            }

            RaiseTextInput(text);
        }

        private void FlushPendingHighSurrogate()
        {
            if (_pendingHighSurrogate is not { } highSurrogate)
            {
                return;
            }

            _pendingHighSurrogate = null;
            RaiseTextInput(highSurrogate.ToString());
        }

        private void RaiseTextInput(string text)
        {
            var args = new RawTextInputEventArgs(
                SilkNetKeyboardDevice.Instance,
                GetTimestamp(),
                Owner,
                text
            );
            Input?.Invoke(args);
        }

        private static ulong GetTimestamp() => unchecked((ulong)Environment.TickCount64);

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
                            _silkWindow.WindowBorder = borderToRestore;
                        }
                    }

                    _silkWindow.WindowState = targetState;
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
            _cursor = cursor as SilkNetCursorImpl;

            if (_inputContext is null)
            {
                return;
            }

            foreach (var mouse in _inputContext.Mice)
            {
                ApplyCursor(mouse.Cursor, _cursor);
            }
        }

        internal static void ApplyCursor(Silk.NET.Input.ICursor cursor, SilkNetCursorImpl? requestedCursor)
        {
            requestedCursor ??= new SilkNetCursorImpl(StandardCursorType.Arrow);

            var mode = requestedCursor.CursorMode;
            cursor.CursorMode = cursor.IsSupported(mode) ? mode : CursorMode.Normal;
            if (mode == CursorMode.Hidden)
            {
                return;
            }

            if (requestedCursor.CursorType == CursorType.Custom && requestedCursor.Image is { } image)
            {
                cursor.HotspotX = requestedCursor.HotSpot.X;
                cursor.HotspotY = requestedCursor.HotSpot.Y;
                cursor.Image = image;
                cursor.Type = CursorType.Custom;
                return;
            }

            var standardCursor = requestedCursor.StandardCursor;
            if (!cursor.IsSupported(standardCursor))
            {
                standardCursor = cursor.IsSupported(StandardCursor.Arrow)
                    ? StandardCursor.Arrow
                    : StandardCursor.Default;
            }

            cursor.StandardCursor = standardCursor;
            cursor.Type = CursorType.Standard;
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
            if (_silkWindow != null)
            {
                var border = value ? WindowBorder.Resizable : WindowBorder.Fixed;
                if (_windowState == Avalonia.Controls.WindowState.Maximized ||
                    _windowState == Avalonia.Controls.WindowState.FullScreen)
                {
                    _restoredBorder = border;
                }
                else
                {
                    _silkWindow.WindowBorder = border;
                }
            }
        }

        public void SetWindowDecorations(WindowDecorations value)
        {
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
                    _restoredBorder = border;
                }
                else
                {
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
            if (_disposed) return;
            _disposed = true;
            SilkNetPlatform.Instance.UnregisterWindow(this);

            try
            {
                _silkWindow.Load -= OnLoad;
                _silkWindow.Render -= OnRender;
                _silkWindow.Resize -= OnResize;
                _silkWindow.Move -= OnMove;
                _silkWindow.Closing -= OnClosing;
                _silkWindow.FocusChanged -= OnFocusChanged;
            }
            catch {}
            
            var windowToDispose = _silkWindow;
            var inputContextToDispose = _inputContext;
            _inputContext = null;

            var tcs = _disposedTcs;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    bool transitionNeeded = false;
                    try
                    {
                        if (windowToDispose.WindowState == Silk.NET.Windowing.WindowState.Fullscreen ||
                            windowToDispose.WindowState == Silk.NET.Windowing.WindowState.Maximized)
                        {
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
                    catch {}

                    if (transitionNeeded)
                    {
                        try
                        {
                            var glfw = Silk.NET.GLFW.Glfw.GetApi();
                            glfw.PollEvents();
                        }
                        catch {}
                        await Task.Delay(300);
                        try
                        {
                            var glfw = Silk.NET.GLFW.Glfw.GetApi();
                            glfw.PollEvents();
                        }
                        catch {}
                    }

                    try
                    {
                        var context = WgpuContext.ActiveContexts.FirstOrDefault(c => c.Window == windowToDispose);
                        if (context != null)
                        {
                            context.Dispose();
                        }
                    }
                    catch {}

                    try
                    {
                        if (inputContextToDispose != null)
                        {
                            inputContextToDispose.Dispose();
                        }
                    }
                    catch {}
                    
                    try
                    {
                        windowToDispose.Dispose();
                    }
                    catch {}
                    
                    try
                    {
                        var glfw = Silk.NET.GLFW.Glfw.GetApi();
                        glfw.PollEvents();
                        await Task.Delay(50);
                        glfw.PollEvents();
                    }
                    catch {}
                }
                catch {}
                finally
                {
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
        public new static SilkNetKeyboardDevice Instance { get; } = new();
        private SilkNetKeyboardDevice() {}
    }
}
