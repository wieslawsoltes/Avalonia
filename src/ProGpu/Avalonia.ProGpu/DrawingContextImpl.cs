using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Media;
using Avalonia.Platform;
using Silk.NET.WebGPU;
using Silk.NET.Core.Native;
using ProGPU.Backend;
using ProGPU.Vector;
using ProGPU.Scene;

namespace Avalonia.ProGpu
{
    internal partial class DrawingContextImpl : IDrawingContextImpl,
        IDrawingContextWithAcrylicLikeSupport,
        IDrawingContextImplWithEffects
    {
        private readonly IDisposable?[]? _disposables;
        private readonly ILockedFramebuffer? _framebuffer;
        private readonly OffscreenTextureCache _offscreenCache;
        internal readonly PixelSize _size;
        private Matrix _currentTransform = Matrix.Identity;
        private double _currentOpacity = 1.0;
        private Vector4 _clearColor = new Vector4(1f, 1f, 1f, 1f);
        private readonly Stack<double> _opacityStack = new();
        private readonly Stack<Avalonia.Media.RenderOptions> _renderOptionsStack = new();
        private readonly Stack<Avalonia.Media.TextOptions> _textOptionsStack = new();

        public Avalonia.Media.RenderOptions RenderOptions { get; private set; }
        public Avalonia.Media.TextOptions TextOptions { get; private set; }

        public ProGPU.Scene.DrawingContext DrawingContext { get; } = new();
        public Vector Dpi { get; }

        public struct CreateInfo
        {
            public PixelSize? Size;
            public Vector Dpi;
            public bool ScaleDrawingToDpi;
            public bool DisableSubpixelTextRendering;
            public object? GrContext;
            public object? Surface;
            public object? Gpu;
            public object? CurrentSession;
            public object? CacheHolder;
        }

        public DrawingContextImpl(CreateInfo createInfo, params IDisposable?[]? disposables)
        {
            Dpi = createInfo.Dpi;
            _disposables = disposables;
            _offscreenCache = (createInfo.CacheHolder as OffscreenTextureCache) ?? GetFallbackCache();

            if (disposables != null)
            {
                foreach (var d in disposables)
                {
                    if (d is ILockedFramebuffer fb)
                    {
                        _framebuffer = fb;
                        break;
                    }
                }
            }

            if (createInfo.Size.HasValue)
            {
                _size = createInfo.Size.Value;
            }
            else if (_framebuffer != null)
            {
                _size = _framebuffer.Size;
            }
            else
            {
                _size = default;
            }

            var preferredFormat = TextureFormat.Bgra8Unorm;
            if (_framebuffer != null)
            {
                if (_framebuffer.Format == PixelFormats.Rgba8888)
                {
                    preferredFormat = TextureFormat.Rgba8Unorm;
                }
            }
            else
            {
                var currentContext = WgpuContext.Current;
                if (currentContext != null)
                {
                    preferredFormat = currentContext.SwapChainFormat;
                }
            }
            EnsureGpuContext(_framebuffer, preferredFormat);
        }

        public void Reset()
        {
            _currentTransform = Matrix.Identity;
            _currentOpacity = 1.0;
            _opacityStack.Clear();
            _renderOptionsStack.Clear();
            _textOptionsStack.Clear();
            DrawingContext.Clear();
        }

        public void Clear(Avalonia.Media.Color color)
        {
            _clearColor = new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
            var pBrush = new ProGPU.Vector.SolidColorBrush(_clearColor);
            DrawingContext.PushBlendMode(GpuBlendMode.Src);
            DrawingContext.DrawRectangle(pBrush, null, new ProGPU.Scene.Rect(0, 0, _size.Width, _size.Height));
            DrawingContext.PopBlendMode();
        }

        public void DrawBitmap(IBitmapImpl source, double opacity, Avalonia.Rect sourceRect, Avalonia.Rect destRect)
        {
            if (source is IDrawingContextLayerImpl layer && layer.CanBlit)
            {
                layer.Blit(this);
                return;
            }

            if (source is IDrawableBitmapImpl drawable)
            {
                if (drawable.Texture == null)
                {
                    drawable.UploadToGpu();
                }
                if (drawable.Texture != null)
                {
                    DrawingContext.DrawTexture(drawable.Texture, ToProGpuRect(destRect));
                }
            }
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Avalonia.Rect opacityMaskRect, Avalonia.Rect destRect)
        {
            DrawBitmap(source, 1.0, new Avalonia.Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height), destRect);
        }

        public void DrawLine(IPen? pen, Avalonia.Point p1, Avalonia.Point p2)
        {
            var pPen = ConvertPen(pen);
            if (pPen != null)
            {
                DrawingContext.DrawLine(pPen, TransformPoint(p1), TransformPoint(p2));
            }
        }

        public void DrawGeometry(IBrush? brush, IPen? pen, IGeometryImpl geometry)
        {
            if (geometry is GeometryImpl geomImpl)
            {
                var pBrush = ConvertBrush(brush);
                var pPen = ConvertPen(pen);
                DrawingContext.DrawPath(pBrush, pPen, geomImpl.Path, ToMatrix4x4(_currentTransform));
            }
        }

        public void DrawRectangle(IExperimentalAcrylicMaterial? material, RoundedRect rect)
        {
            var pBrush = new ProGPU.Vector.SolidColorBrush(new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
            float radius = (float)(rect.RadiiTopLeft.X * Math.Abs(_currentTransform.M11));
            DrawingContext.DrawRoundedRectangle(pBrush, null, ToProGpuRect(rect.Rect), radius);
        }

        public void DrawRectangle(IBrush? brush, IPen? pen, RoundedRect rect, BoxShadows boxShadows = default)
        {
            var pBrush = ConvertBrush(brush);
            var pPen = ConvertPen(pen);
            var proGpuRect = ToProGpuRect(rect.Rect);
            if (rect.IsRounded)
            {
                float radius = (float)(rect.RadiiTopLeft.X * Math.Abs(_currentTransform.M11));
                DrawingContext.DrawRoundedRectangle(pBrush, pPen, proGpuRect, radius);
            }
            else
            {
                DrawingContext.DrawRectangle(pBrush, pPen, proGpuRect);
            }
        }

        public void DrawRegion(IBrush? brush, IPen? pen, IPlatformRenderInterfaceRegion region)
        {
            var pBrush = ConvertBrush(brush);
            var pPen = ConvertPen(pen);
            var bounds = region.Bounds;
            DrawingContext.DrawRectangle(pBrush, pPen, new ProGPU.Scene.Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }

        public void DrawEllipse(IBrush? brush, IPen? pen, Avalonia.Rect rect)
        {
            var pBrush = ConvertBrush(brush);
            var pPen = ConvertPen(pen);
            var center = TransformPoint(rect.Center);
            float radiusX = (float)(rect.Width / 2.0 * _currentTransform.M11);
            float radiusY = (float)(rect.Height / 2.0 * _currentTransform.M22);
            DrawingContext.DrawEllipse(pBrush, pPen, center, radiusX, radiusY);
        }

        public void DrawGlyphRun(IBrush? foreground, IGlyphRunImpl glyphRun)
        {
            if (glyphRun is GlyphRunImpl run)
            {
                var pBrush = ConvertBrush(foreground);
                if (pBrush == null) return;

                double scale = run.FontRenderingEmSize / run.Typeface.Font.UnitsPerEm;

                for (int i = 0; i < run.GlyphIndices.Length; i++)
                {
                    ushort glyphIndex = run.GlyphIndices[i];
                    var pos = run.GlyphPositions[i];
                    var origin = run.BaselineOrigin + new Vector(pos.X, pos.Y);
                    var screenOrigin = origin * _currentTransform;
                    double snappedOriginX = Math.Round(screenOrigin.X * 4.0) / 4.0;
                    double snappedOriginY = Math.Round(screenOrigin.Y * 4.0) / 4.0;

                    var outline = run.Typeface.Font.GetFlippedGlyphOutline(glyphIndex);
                    if (outline != null)
                    {
                        double scaleX = Math.Abs(_currentTransform.M11) > 0.0001 ? _currentTransform.M11 : 1.0;
                        double scaleY = Math.Abs(_currentTransform.M22) > 0.0001 ? _currentTransform.M22 : 1.0;

                        var finalMatrix = System.Numerics.Matrix4x4.CreateScale((float)(scale * scaleX), (float)(scale * scaleY), 1f) *
                                          System.Numerics.Matrix4x4.CreateTranslation((float)snappedOriginX, (float)snappedOriginY, 0f);

                        DrawingContext.DrawPath(pBrush, null, outline, finalMatrix);
                    }
                }
            }
        }

        public IDrawingContextLayerImpl CreateLayer(PixelSize size)
        {
            PixelFormat? format = _framebuffer?.Format;
            if (format == null)
            {
                var currentContext = WgpuContext.Current;
                if (currentContext != null)
                {
                    format = currentContext.SwapChainFormat == TextureFormat.Rgba8Unorm
                        ? PixelFormats.Rgba8888
                        : PixelFormats.Bgra8888;
                }
            }
            var createInfo = new SurfaceRenderTarget.CreateInfo
            {
                Width = size.Width,
                Height = size.Height,
                Dpi = Dpi,
                UseScaledDrawing = true,
                Format = format
            };
            return new SurfaceRenderTarget(createInfo);
        }

        public void PushClip(Avalonia.Rect clip)
        {
            var r = ToProGpuRect(clip);
            DrawingContext.PushClip(r);
        }
        public void PushClip(RoundedRect clip)
        {
            var r = ToProGpuRect(clip.Rect);
            DrawingContext.PushClip(r);
        }
        public void PushClip(IPlatformRenderInterfaceRegion region)
        {
            var bounds = region.Bounds;
            var r = new ProGPU.Scene.Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            DrawingContext.PushClip(r);
        }
        public void PopClip()
        {
            DrawingContext.PopClip();
        }

        public void PushLayer(Avalonia.Rect bounds)
        {
            var r = ToProGpuRect(bounds);
            DrawingContext.PushClip(r);
        }
        public void PopLayer()
        {
            DrawingContext.PopClip();
        }

        public void PushOpacity(double opacity, Avalonia.Rect? bounds)
        {
            _opacityStack.Push(_currentOpacity);
            _currentOpacity *= opacity;
            DrawingContext.PushOpacity((float)opacity);
        }

        public void PopOpacity()
        {
            if (_opacityStack.Count > 0)
            {
                _currentOpacity = _opacityStack.Pop();
                DrawingContext.PopOpacity();
            }
        }

        public void PushGeometryClip(IGeometryImpl clip)
        {
            if (clip is GeometryImpl geomImpl)
            {
                DrawingContext.PushGeometryClip(geomImpl.Path);
            }
        }
        public void PopGeometryClip()
        {
            DrawingContext.PopGeometryClip();
        }

        public void PushOpacityMask(IBrush mask, Avalonia.Rect bounds)
        {
            var pBrush = ConvertBrush(mask);
            if (pBrush != null)
            {
                DrawingContext.PushOpacityMask(pBrush, ToProGpuRect(bounds));
            }
        }

        public void PopOpacityMask()
        {
            DrawingContext.PopOpacityMask();
        }

        public void PushRenderOptions(Avalonia.Media.RenderOptions renderOptions)
        {
            _renderOptionsStack.Push(RenderOptions);
            RenderOptions = RenderOptions.MergeWith(renderOptions);
        }

        public void PopRenderOptions()
        {
            RenderOptions = _renderOptionsStack.Pop();
        }

        public void PushTextOptions(Avalonia.Media.TextOptions textOptions)
        {
            _textOptionsStack.Push(TextOptions);
            TextOptions = TextOptions.MergeWith(textOptions);
        }

        public void PopTextOptions()
        {
            TextOptions = _textOptionsStack.Pop();
        }

        public Matrix Transform
        {
            get => _currentTransform;
            set => _currentTransform = value;
        }

        public object? GetFeature(Type featureType) => null;

        [ThreadStatic]
        private static WgpuContext? s_wgpuContext;
        private static readonly object s_initLock = new();
        private static readonly Dictionary<WgpuContext, Dictionary<TextureFormat, Compositor>> s_compositors = new();

        private static Compositor GetCompositor(WgpuContext context, TextureFormat format)
        {
            lock (s_initLock)
            {
                if (!s_compositors.TryGetValue(context, out var dict))
                {
                    dict = new Dictionary<TextureFormat, Compositor>();
                    s_compositors[context] = dict;
                }

                if (!dict.TryGetValue(format, out var compositor))
                {
                    compositor = new Compositor(context, format);
                    dict[format] = compositor;
                }

                return compositor;
            }
        }

        [ThreadStatic]
        private static OffscreenTextureCache? s_fallbackCache;

        private static OffscreenTextureCache GetFallbackCache()
        {
            return s_fallbackCache ??= new OffscreenTextureCache();
        }

        private static readonly PfnBufferMapCallback s_bufferMapCallback;
        [ThreadStatic]
        private static bool s_isMappingPending;

        static unsafe DrawingContextImpl()
        {
            s_bufferMapCallback = PfnBufferMapCallback.From(OnBufferMapped);
            WgpuContext.Disposing += InvalidateForContext;
        }

        private static unsafe void OnBufferMapped(BufferMapAsyncStatus status, void* userData)
        {
            s_isMappingPending = false;
        }

        private static unsafe void InvalidateCachedResources()
        {
            s_fallbackCache?.Invalidate(s_wgpuContext);
        }

        public static unsafe void InvalidateForContext(WgpuContext context)
        {
            lock (context.RenderLock)
            {
                Dictionary<TextureFormat, Compositor>? dictToDispose = null;

                lock (s_initLock)
                {
                    if (s_compositors.TryGetValue(context, out var dict))
                    {
                        dictToDispose = dict;
                        s_compositors.Remove(context);
                    }

                    if (s_wgpuContext == context)
                    {
                        s_wgpuContext = null;
                    }
                }

                if (dictToDispose != null)
                {
                    foreach (var compositor in dictToDispose.Values)
                    {
                        try { compositor.Dispose(); } catch {}
                    }
                }

                s_fallbackCache?.Invalidate(context);
            }
        }

        private static unsafe WgpuContext? ResolveContext(ILockedFramebuffer? framebuffer)
        {
            if (framebuffer is IGpuLockedFramebuffer gpuFb)
            {
                var surfacePtr = gpuFb.SurfacePointer;
                if (surfacePtr != IntPtr.Zero)
                {
                    lock (s_initLock)
                    {
                        foreach (var context in WgpuContext.ActiveContexts)
                        {
                            if ((IntPtr)context.Surface == surfacePtr)
                            {
                                return context;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static unsafe void EnsureGpuContext(ILockedFramebuffer? framebuffer, TextureFormat? preferredFormat = null)
        {
            lock (s_initLock)
            {
                var current = ResolveContext(framebuffer);
                if (current == null)
                {
                    current = WgpuContext.Current;
                    if (current == null)
                    {
                        var activeContexts = WgpuContext.ActiveContexts;
                        if (activeContexts.Count > 0)
                        {
                            current = activeContexts[0];
                        }
                    }
                }

                if (current == null)
                {
                    if (s_wgpuContext == null)
                    {
                        s_wgpuContext = new WgpuContext();
                        s_wgpuContext.Initialize(null);
                    }
                }
                else
                {
                    s_wgpuContext = current;
                }

                WgpuContext.Current = s_wgpuContext;
            }
        }

        private static unsafe (GpuTexture texture, IntPtr stagingBuffer, uint bytesPerRow, uint stagingBufferSize) GetOffscreenResources(
            OffscreenTextureCache cache, WgpuContext context, uint width, uint height, TextureFormat format)
        {
            if (cache.CachedTexture != null && 
                cache.CachedWidth == width && 
                cache.CachedHeight == height && 
                cache.CachedTexture.Format == format &&
                cache.CachedTexture.Context == context)
            {
                return (cache.CachedTexture, cache.CachedStagingBuffer, cache.CachedBytesPerRow, cache.CachedStagingBufferSize);
            }

            cache.Invalidate(context);

            cache.CachedWidth = width;
            cache.CachedHeight = height;

            cache.CachedTexture = new GpuTexture(
                context,
                width,
                height,
                format,
                Silk.NET.WebGPU.TextureUsage.RenderAttachment | Silk.NET.WebGPU.TextureUsage.CopySrc | Silk.NET.WebGPU.TextureUsage.TextureBinding,
                "Avalonia offscreen target"
            );

            uint bytesPerPixel = 4;
            uint unalignedBytesPerRow = width * bytesPerPixel;
            cache.CachedBytesPerRow = (unalignedBytesPerRow + 255) & ~255u;
            cache.CachedStagingBufferSize = cache.CachedBytesPerRow * height;

            var bufferDesc = new BufferDescriptor
            {
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                Size = cache.CachedStagingBufferSize,
                MappedAtCreation = false
            };
            cache.CachedStagingBuffer = (IntPtr)context.Wgpu.DeviceCreateBuffer(context.Device, &bufferDesc);

            return (cache.CachedTexture, cache.CachedStagingBuffer, cache.CachedBytesPerRow, cache.CachedStagingBufferSize);
        }

        [DllImport("wgpu_native", EntryPoint = "wgpuDevicePoll")]
        private static extern unsafe bool wgpuDevicePoll(Silk.NET.WebGPU.Device* device, bool wait, void* wrappedSubmissionIndex);

        private unsafe void FlushToFramebuffer()
        {
            if (_framebuffer == null) return;
            if (DrawingContext.Commands.Count == 0) return;

            uint width = (uint)_framebuffer.Size.Width;
            uint height = (uint)_framebuffer.Size.Height;
            if (width == 0 || height == 0) return;

            var preferredFormat = TextureFormat.Bgra8Unorm;
            if (_framebuffer.Format == PixelFormats.Rgba8888)
            {
                preferredFormat = TextureFormat.Rgba8Unorm;
            }

            EnsureGpuContext(_framebuffer, preferredFormat);
            var context = s_wgpuContext!;
            lock (context.RenderLock)
            {
                if (context.IsDisposed) return;

                var compositor = GetCompositor(context, preferredFormat);

                var (texture, stagingBuffer, bytesPerRow, stagingBufferSize) = GetOffscreenResources(_offscreenCache, context, width, height, preferredFormat);

                var drawingVisual = new DrawingVisual();
                drawingVisual.Size = new Vector2(width, height);
                drawingVisual.Context.Append(DrawingContext);

                bool loadExisting = !_offscreenCache.IsTextureFresh;
                _offscreenCache.IsTextureFresh = false;

                compositor.RenderOffscreen(
                    drawingVisual,
                    width,
                    height,
                    texture,
                    0.0f,
                    1.0f,
                    _clearColor,
                    loadExistingContents: loadExisting
                );

                if (_framebuffer is Avalonia.Platform.IGpuLockedFramebuffer gpuFb)
                {
                    context.ReconfigureIfNeeded(width, height);
                    var surfaceTexture = new SurfaceTexture();
                    context.Wgpu.SurfaceGetCurrentTexture((Surface*)gpuFb.SurfacePointer, &surfaceTexture);

                    if (surfaceTexture.Status == SurfaceGetCurrentTextureStatus.Success)
                    {
                        var viewDesc = new TextureViewDescriptor
                        {
                            Format = context.SwapChainFormat,
                            Dimension = TextureViewDimension.Dimension2D,
                            BaseMipLevel = 0,
                            MipLevelCount = 1,
                            BaseArrayLayer = 0,
                            ArrayLayerCount = 1,
                            Aspect = TextureAspect.All
                        };
                        var targetView = context.Wgpu.TextureCreateView(surfaceTexture.Texture, &viewDesc);

                        if (targetView != null)
                        {
                            var presentVisual = new DrawingVisual();
                            presentVisual.Size = new Vector2(width, height);
                            var rect = new ProGPU.Scene.Rect(0, 0, width, height);
                            presentVisual.Context.DrawTexture(texture, rect);

                            compositor.RenderScene(presentVisual, width, height, targetView);

                            context.Wgpu.SurfacePresent((Surface*)gpuFb.SurfacePointer);
                            context.Wgpu.TextureViewRelease(targetView);
                        }
                    }
                    return;
                }

                var encoderDesc = new CommandEncoderDescriptor();
                var encoder = context.Wgpu.DeviceCreateCommandEncoder(context.Device, &encoderDesc);

                var copySrc = new ImageCopyTexture
                {
                    Texture = texture.TexturePtr,
                    MipLevel = 0,
                    Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                    Aspect = TextureAspect.All
                };

                var copyDst = new ImageCopyBuffer
                {
                    Buffer = (Silk.NET.WebGPU.Buffer*)stagingBuffer,
                    Layout = new TextureDataLayout
                    {
                        Offset = 0,
                        BytesPerRow = bytesPerRow,
                        RowsPerImage = height
                    }
                };

                var copySize = new Extent3D
                {
                    Width = width,
                    Height = height,
                    DepthOrArrayLayers = 1
                };

                context.Wgpu.CommandEncoderCopyTextureToBuffer(encoder, &copySrc, &copyDst, &copySize);

                var cmdBufferDesc = new CommandBufferDescriptor();
                var cmdBuffer = context.Wgpu.CommandEncoderFinish(encoder, &cmdBufferDesc);

                context.Wgpu.QueueSubmit(context.Queue, 1, &cmdBuffer);
                context.Wgpu.CommandBufferRelease(cmdBuffer);
                context.Wgpu.CommandEncoderRelease(encoder);

                s_isMappingPending = true;
                context.Wgpu.BufferMapAsync((Silk.NET.WebGPU.Buffer*)stagingBuffer, MapMode.Read, 0, (nuint)stagingBufferSize, s_bufferMapCallback, null);

                while (s_isMappingPending)
                {
                    wgpuDevicePoll(context.Device, false, null);
                    Thread.Sleep(1);
                }

                void* mappedPtr = context.Wgpu.BufferGetConstMappedRange((Silk.NET.WebGPU.Buffer*)stagingBuffer, 0, (nuint)stagingBufferSize);
                if (mappedPtr != null)
                {
                    byte* srcBytes = (byte*)mappedPtr;
                    byte* dstBytes = (byte*)_framebuffer.Address;
                    uint rowBytes = width * 4;

                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = srcBytes + (y * bytesPerRow);
                        byte* dstRow = dstBytes + (y * (uint)_framebuffer.RowBytes);
                        System.Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                    }

                    context.Wgpu.BufferUnmap((Silk.NET.WebGPU.Buffer*)stagingBuffer);
                }
                context.CleanupPendingResources();
            }
        }

        public void Dispose()
        {
            FlushToFramebuffer();

            if (_disposables != null)
            {
                foreach (var disposable in _disposables)
                {
                    disposable?.Dispose();
                }
            }
        }

        private Vector2 TransformPoint(Point pt)
        {
            var p = pt * _currentTransform;
            return new Vector2((float)p.X, (float)p.Y);
        }

        internal ProGPU.Scene.Rect ToProGpuRect(Avalonia.Rect r)
        {
            var topLeft = r.TopLeft * _currentTransform;
            var bottomRight = r.BottomRight * _currentTransform;
            float x = (float)Math.Min(topLeft.X, bottomRight.X);
            float y = (float)Math.Min(topLeft.Y, bottomRight.Y);
            float w = (float)Math.Abs(bottomRight.X - topLeft.X);
            float h = (float)Math.Abs(bottomRight.Y - topLeft.Y);
            return new ProGPU.Scene.Rect(x, y, w, h);
        }

        private ProGPU.Vector.Brush? ConvertBrush(IBrush? avaloniaBrush)
        {
            if (avaloniaBrush == null) return null;
            
            float opacity = (float)avaloniaBrush.Opacity;
            
            if (avaloniaBrush is ISolidColorBrush solid)
            {
                var c = solid.Color;
                var vecColor = new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);
                return new ProGPU.Vector.SolidColorBrush(vecColor) { Opacity = opacity };
            }
            else if (avaloniaBrush is ILinearGradientBrush linear)
            {
                var start = TransformPoint(linear.StartPoint.Point);
                var end = TransformPoint(linear.EndPoint.Point);
                var stops = new ProGPU.Vector.GradientStop[linear.GradientStops.Count];
                for (int i = 0; i < stops.Length; i++)
                {
                    var st = linear.GradientStops[i];
                    var c = st.Color;
                    stops[i] = new ProGPU.Vector.GradientStop(
                        new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f),
                        (float)st.Offset
                    );
                }
                return new ProGPU.Vector.LinearGradientBrush(start, end, stops) { Opacity = opacity };
            }
            else if (avaloniaBrush is IRadialGradientBrush radial)
            {
                var center = TransformPoint(radial.Center.Point);
                float radius = (float)radial.RadiusX.Scalar;
                var stops = new ProGPU.Vector.GradientStop[radial.GradientStops.Count];
                for (int i = 0; i < stops.Length; i++)
                {
                    var st = radial.GradientStops[i];
                    var c = st.Color;
                    stops[i] = new ProGPU.Vector.GradientStop(
                        new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f),
                        (float)st.Offset
                    );
                }
                return new ProGPU.Vector.RadialGradientBrush(center, radius, stops) { Opacity = opacity };
            }
            
            return new ProGPU.Vector.SolidColorBrush(Vector4.One) { Opacity = opacity };
        }

        private ProGPU.Vector.Pen? ConvertPen(IPen? avaloniaPen)
        {
            if (avaloniaPen == null) return null;
            var brush = ConvertBrush(avaloniaPen.Brush);
            if (brush == null) return null;
            return new ProGPU.Vector.Pen(brush, (float)avaloniaPen.Thickness);
        }

        private static System.Numerics.Matrix4x4 ToMatrix4x4(Avalonia.Matrix m)
        {
            return new System.Numerics.Matrix4x4(
                (float)m.M11, (float)m.M12, 0f, 0f,
                (float)m.M21, (float)m.M22, 0f, 0f,
                0f,           0f,           1f, 0f,
                (float)m.M31, (float)m.M32, 0f, 1f
            );
        }

        internal static unsafe void RenderToTexture(ProGPU.Scene.DrawingContext sourceContext, GpuTexture texture, Vector dpi, bool isTextureFresh = false)
        {
            var context = texture.Context;
            lock (context.RenderLock)
            {
                if (context.IsDisposed) return;
                WgpuContext.Current = context;
                s_wgpuContext = context;
                var compositor = GetCompositor(context, texture.Format);

                var drawingVisual = new DrawingVisual();
                drawingVisual.Size = new Vector2(texture.Width, texture.Height);
                drawingVisual.Context.Append(sourceContext);

                compositor.RenderOffscreen(
                    drawingVisual,
                    texture.Width,
                    texture.Height,
                    texture,
                    0.0f,
                    1.0f,
                    new Vector4(0f, 0f, 0f, 0f), // Transparent clear color for layers
                    loadExistingContents: !isTextureFresh
                );
            }
        }
    }
}
