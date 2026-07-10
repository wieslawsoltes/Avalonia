using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Utilities;
using Silk.NET.WebGPU;
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
        private readonly bool _preserveRecordedCommandsOnDispose;
        private readonly OffscreenTextureCache _offscreenCache;
        private readonly Matrix? _postTransform;
        internal readonly PixelSize _size;
        private Matrix _currentTransform = Matrix.Identity;
        private double _currentOpacity = 1.0;
        private Vector4 _clearColor = new Vector4(1f, 1f, 1f, 1f);
        private readonly Stack<double> _opacityStack = new();
        private int _opacityMaskDepth;
        private readonly Stack<ClipKind> _clipStack = new();
        private readonly Stack<Avalonia.Media.RenderOptions> _renderOptionsStack = new();
        private readonly Stack<Avalonia.Media.TextOptions> _textOptionsStack = new();

        private enum ClipKind
        {
            Rectangle,
            Geometry
        }

        public Avalonia.Media.RenderOptions RenderOptions { get; private set; }
        public Avalonia.Media.TextOptions TextOptions { get; private set; }

        public ProGPU.Scene.DrawingContext DrawingContext { get; private set; } = new();
        public Vector Dpi { get; }

        public struct CreateInfo
        {
            public PixelSize? Size;
            public Vector Dpi;
            public bool ScaleDrawingToDpi;
            public bool DisableSubpixelTextRendering;
            public bool PreserveRecordedCommandsOnDispose;
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
            _preserveRecordedCommandsOnDispose = createInfo.PreserveRecordedCommandsOnDispose;
            _offscreenCache = (createInfo.CacheHolder as OffscreenTextureCache) ?? GetFallbackCache();
            if (createInfo.ScaleDrawingToDpi &&
                TryGetDpiScale(createInfo.Dpi, out double scaleX, out double scaleY) &&
                (!NearlyEqual(scaleX, 1.0) || !NearlyEqual(scaleY, 1.0)))
            {
                _postTransform = Matrix.CreateScale(scaleX, scaleY);
            }

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

        private Matrix RenderTransform => _postTransform.HasValue
            ? _currentTransform * _postTransform.Value
            : _currentTransform;

        public void Reset()
        {
            _currentTransform = Matrix.Identity;
            _currentOpacity = 1.0;
            _opacityStack.Clear();
            _opacityMaskDepth = 0;
            _clipStack.Clear();
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
                    if (!NearlyEqual(opacity, 1.0))
                    {
                        DrawingContext.PushOpacity((float)opacity);
                    }

                    DrawingContext.DrawTexture(
                        drawable.Texture,
                        ToLocalProGpuRect(destRect),
                        ToLocalProGpuRect(sourceRect),
                        ToMatrix4x4(RenderTransform));

                    if (!NearlyEqual(opacity, 1.0))
                    {
                        DrawingContext.PopOpacity();
                    }
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
                var bounds = geomImpl.Bounds;
                var pPen = ConvertPen(pen, bounds);
                if (TryDrawSceneBrush(brush, bounds, geomImpl.Path) ||
                    TryDrawImageBrush(brush, bounds, geomImpl.Path))
                {
                    if (pPen != null)
                    {
                        DrawingContext.DrawPath(null, pPen, geomImpl.Path, ToMatrix4x4(RenderTransform));
                    }
                    return;
                }

                var pBrush = ConvertBrush(brush, bounds);
                DrawingContext.DrawPath(pBrush, pPen, geomImpl.Path, ToMatrix4x4(RenderTransform));
            }
        }

        public void DrawRectangle(IExperimentalAcrylicMaterial? material, RoundedRect rect)
        {
            if (material == null || rect.Rect.Width <= 0 || rect.Rect.Height <= 0)
            {
                return;
            }

            var tintColor = material.TintColor;
            var luminosityColor = material.MaterialColor;
            var fallbackColor = material.FallbackColor;
            var parameters = new BackdropMaterialParams
            {
                Rect = ToLocalProGpuRect(rect.Rect),
                CornerRadiiX = new Vector4(
                    (float)rect.RadiiTopLeft.X,
                    (float)rect.RadiiTopRight.X,
                    (float)rect.RadiiBottomRight.X,
                    (float)rect.RadiiBottomLeft.X),
                CornerRadiiY = new Vector4(
                    (float)rect.RadiiTopLeft.Y,
                    (float)rect.RadiiTopRight.Y,
                    (float)rect.RadiiBottomRight.Y,
                    (float)rect.RadiiBottomLeft.Y),
                Kind = BackdropMaterialKind.Acrylic,
                Source = material.BackgroundSource == AcrylicBackgroundSource.Digger
                    ? BackdropMaterialSource.HostBackdrop
                    : BackdropMaterialSource.None,
                TintColor = new Vector4(
                    tintColor.R / 255f,
                    tintColor.G / 255f,
                    tintColor.B / 255f,
                    tintColor.A / 255f),
                LuminosityColor = new Vector4(
                    luminosityColor.R / 255f,
                    luminosityColor.G / 255f,
                    luminosityColor.B / 255f,
                    luminosityColor.A / 255f),
                FallbackColor = new Vector4(
                    fallbackColor.R / 255f,
                    fallbackColor.G / 255f,
                    fallbackColor.B / 255f,
                    fallbackColor.A / 255f),
                TintOpacity = 1f,
                LuminosityOpacity = 1f,
                MaterialOpacity = 1f,
                NoiseOpacity = 0.0225f,
                BlurRadius = 30f,
                Saturation = 1.25f
            };

            var replaceBackdrop = material.BackgroundSource == AcrylicBackgroundSource.Digger;
            if (replaceBackdrop)
            {
                DrawingContext.PushBlendMode(GpuBlendMode.Src);
            }

            DrawingContext.DrawBackdropMaterial(parameters, ToMatrix4x4(RenderTransform));

            if (replaceBackdrop)
            {
                DrawingContext.PopBlendMode();
            }
        }

        public void DrawRectangle(IBrush? brush, IPen? pen, RoundedRect rect, BoxShadows boxShadows = default)
        {
            var pPen = ConvertPen(pen, rect.Rect);
            var localRect = ToLocalProGpuRect(rect.Rect);
            var clipPath = rect.IsRounded
                ? PrimitivePathGeometry.CreateRoundedRectangle(
                    localRect.X,
                    localRect.Y,
                    localRect.Width,
                    localRect.Height,
                    (float)rect.RadiiTopLeft.X,
                    (float)rect.RadiiTopLeft.Y)
                : PrimitivePathGeometry.CreateRectangle(localRect.X, localRect.Y, localRect.Width, localRect.Height);
            if (TryDrawSceneBrush(brush, rect.Rect, clipPath, useGeometryClip: false) ||
                TryDrawImageBrush(brush, rect.Rect, clipPath))
            {
                if (pPen != null)
                {
                    DrawingContext.DrawPath(null, pPen, clipPath, ToMatrix4x4(RenderTransform));
                }
                return;
            }

            var pBrush = ConvertBrush(brush, rect.Rect);
            var transform = ToMatrix4x4(RenderTransform);
            if (rect.IsRounded)
            {
                DrawingContext.DrawRoundedRectangle(
                    pBrush,
                    pPen,
                    localRect,
                    (float)rect.RadiiTopLeft.X,
                    (float)rect.RadiiTopLeft.Y,
                    transform);
            }
            else
            {
                DrawingContext.DrawRectangle(pBrush, pPen, localRect, transform);
            }
        }

        public void DrawRegion(IBrush? brush, IPen? pen, IPlatformRenderInterfaceRegion region)
        {
            if (region.IsEmpty)
                return;

            var pBrush = ConvertBrush(brush);
            var pPen = ConvertPen(pen);
            var rects = region.Rects;
            if (rects.Count == 1)
            {
                DrawingContext.DrawRectangle(pBrush, pPen, ToProGpuRect(rects[0]));
            }
            else
            {
                DrawingContext.DrawPath(pBrush, pPen, CreateRegionGeometry(rects), Matrix4x4.Identity);
            }
        }

        public void DrawEllipse(IBrush? brush, IPen? pen, Avalonia.Rect rect)
        {
            var center = new Vector2((float)rect.Center.X, (float)rect.Center.Y);
            var radiusX = (float)(rect.Width / 2.0);
            var radiusY = (float)(rect.Height / 2.0);
            var clipPath = PrimitivePathGeometry.CreateEllipse(center, radiusX, radiusY);
            var pPen = ConvertPen(pen, rect);
            if (TryDrawSceneBrush(brush, rect, clipPath) ||
                TryDrawImageBrush(brush, rect, clipPath))
            {
                if (pPen != null)
                {
                    DrawingContext.DrawPath(null, pPen, clipPath, ToMatrix4x4(RenderTransform));
                }
                return;
            }

            var pBrush = ConvertBrush(brush, rect);
            DrawingContext.DrawEllipse(
                pBrush,
                pPen,
                center,
                radiusX,
                radiusY,
                ToMatrix4x4(RenderTransform));
        }

        public void DrawGlyphRun(IBrush? foreground, IGlyphRunImpl glyphRun)
        {
            if (glyphRun is GlyphRunImpl run)
            {
                var pBrush = ConvertBrush(foreground, run.Bounds);
                if (pBrush == null) return;

                var scale = (float)(run.FontRenderingEmSize / run.Typeface.Font.UnitsPerEm);
                var renderTransform = ToMatrix4x4(RenderTransform);
                var colorGlyphOpacity = foreground?.Opacity ?? 1.0;
                if (foreground is ISolidColorBrush solidColorBrush)
                {
                    colorGlyphOpacity *= solidColorBrush.Color.A / 255.0;
                }

                for (var i = 0; i < run.GlyphIndices.Length; i++)
                {
                    var glyphIndex = run.GlyphIndices[i];
                    var position = run.GlyphPositions[i];
                    var origin = run.BaselineOrigin + new Vector(position.X, position.Y);

                    if (BitmapGlyphCache.TryGetTexture(
                            run.Typeface.Font,
                            glyphIndex,
                            run.FontRenderingEmSize,
                            out var bitmapGlyph))
                    {
                        var metrics = bitmapGlyph.Value.Metrics;
                        var bounds = metrics.GetBounds(origin, run.FontRenderingEmSize);
                        if (!NearlyEqual(colorGlyphOpacity, 1.0))
                        {
                            DrawingContext.PushOpacity((float)colorGlyphOpacity);
                        }

                        DrawingContext.DrawTexture(
                            bitmapGlyph.Value.Texture,
                            ToLocalProGpuRect(bounds),
                            ToLocalProGpuRect(bitmapGlyph.Value.SourceRect),
                            renderTransform);

                        if (!NearlyEqual(colorGlyphOpacity, 1.0))
                        {
                            DrawingContext.PopOpacity();
                        }
                        continue;
                    }

                    var glyphTransform = Matrix4x4.CreateScale(scale, scale, 1f) *
                                         Matrix4x4.CreateTranslation((float)origin.X, (float)origin.Y, 0f) *
                                         renderTransform;
                    var colorLayers = run.Typeface.Font.GetColorLayers(glyphIndex);
                    if (colorLayers is { Count: > 0 })
                    {
                        foreach (var layer in colorLayers)
                        {
                            var layerOutline = run.Typeface.Font.GetFlippedGlyphOutline(layer.GlyphId);
                            if (layerOutline == null)
                            {
                                continue;
                            }

                            var layerColor = layer.Color;
                            layerColor.W *= (float)colorGlyphOpacity;
                            DrawingContext.DrawPath(
                                new ProGPU.Vector.SolidColorBrush(layerColor),
                                null,
                                layerOutline,
                                glyphTransform);
                        }
                        continue;
                    }

                    var outline = run.Typeface.Font.GetFlippedGlyphOutline(glyphIndex);
                    if (outline == null)
                    {
                        continue;
                    }

                    DrawingContext.DrawPath(pBrush, null, outline, glyphTransform);
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
            DrawingContext.PushClip(ToProGpuRect(clip));
            _clipStack.Push(ClipKind.Rectangle);
        }
        public void PushClip(RoundedRect clip)
        {
            DrawingContext.PushClip(ToProGpuRect(clip.Rect));
            _clipStack.Push(ClipKind.Rectangle);
        }
        public void PushClip(IPlatformRenderInterfaceRegion region)
        {
            var rects = region.Rects;
            if (rects.Count <= 1)
            {
                var rect = rects.Count == 0 ? default : rects[0];
                DrawingContext.PushClip(ToProGpuRect(rect));
                _clipStack.Push(ClipKind.Rectangle);
            }
            else
            {
                DrawingContext.PushGeometryClip(CreateRegionGeometry(rects));
                _clipStack.Push(ClipKind.Geometry);
            }
        }
        public void PopClip()
        {
            if (_clipStack.Count == 0 || _clipStack.Pop() == ClipKind.Rectangle)
                DrawingContext.PopClip();
            else
                DrawingContext.PopGeometryClip();
        }

        public void PushLayer(Avalonia.Rect bounds)
        {
            DrawingContext.PushClip(ToProGpuRect(bounds));
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
                var transform = RenderTransform;
                var path = transform == Matrix.Identity
                    ? geomImpl.Path
                    : geomImpl.Path.CreateTransformed(ToMatrix4x4(transform));
                DrawingContext.PushGeometryClip(path);
            }
        }
        public void PopGeometryClip()
        {
            DrawingContext.PopGeometryClip();
        }

        public void PushOpacityMask(IBrush mask, Avalonia.Rect bounds)
        {
            var pBrush = ConvertBrush(mask, bounds);
            if (pBrush != null)
            {
                DrawingContext.PushOpacityMask(pBrush, ToProGpuRect(bounds));
            }
            else
            {
                var ownerContext = DrawingContext;
                var picture = RecordOpacityMask(mask, bounds);
                ownerContext.RetainResource(picture);
                ownerContext.PushOpacityMask(picture, ToProGpuRect(bounds));
            }

            _opacityMaskDepth++;
        }

        public void PopOpacityMask()
        {
            if (_opacityMaskDepth > 0)
            {
                _opacityMaskDepth--;
                DrawingContext.PopOpacityMask();
            }
        }

        private GpuPicture RecordOpacityMask(IBrush mask, Avalonia.Rect bounds)
        {
            var recorder = new GpuPictureRecorder();
            var recordingContext = recorder.BeginRecording(ToLocalProGpuRect(bounds));
            var ownerContext = DrawingContext;
            GpuPicture? picture = null;

            DrawingContext = recordingContext;
            try
            {
                DrawRectangle(mask, null, new RoundedRect(bounds));
                picture = recorder.EndRecording();
                return picture;
            }
            finally
            {
                DrawingContext = ownerContext;
                if (picture == null)
                {
                    recordingContext.Clear();
                }
            }
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

        static DrawingContextImpl()
        {
            WgpuContext.Disposing += InvalidateForContext;
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

        private static (GpuTexture texture, GpuTextureReadbackBuffer readbackBuffer) GetOffscreenResources(
            OffscreenTextureCache cache, WgpuContext context, uint width, uint height, TextureFormat format)
        {
            if (cache.CachedTexture != null && 
                cache.CachedWidth == width && 
                cache.CachedHeight == height && 
                cache.CachedTexture.Format == format &&
                cache.CachedTexture.Context == context &&
                cache.CachedReadbackBuffer != null)
            {
                return (cache.CachedTexture, cache.CachedReadbackBuffer);
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

            cache.CachedReadbackBuffer = new GpuTextureReadbackBuffer(context);

            return (cache.CachedTexture, cache.CachedReadbackBuffer);
        }

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

                var (texture, readbackBuffer) = GetOffscreenResources(_offscreenCache, context, width, height, preferredFormat);
                var hostFrame = CreateHostFrame(width, height);

                var drawingVisual = new DrawingVisual();
                drawingVisual.Size = hostFrame.LogicalSize;
                drawingVisual.Context.Append(DrawingContext);

                bool loadExisting = !_offscreenCache.IsTextureFresh;
                _offscreenCache.IsTextureFresh = false;

                try
                {
                    compositor.RenderOffscreen(
                        drawingVisual,
                        hostFrame,
                        texture,
                        0.0f,
                        _clearColor,
                        loadExistingContents: loadExisting
                    );
                }
                finally
                {
                    drawingVisual.Context.Clear();
                }

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
                            presentVisual.Size = hostFrame.LogicalSize;
                            var rect = new ProGPU.Scene.Rect(0, 0, hostFrame.LogicalWidth, hostFrame.LogicalHeight);
                            presentVisual.Context.DrawTexture(texture, rect);

                            compositor.RenderScene(presentVisual, hostFrame, targetView);

                            context.Wgpu.SurfacePresent((Surface*)gpuFb.SurfacePointer);
                            context.Wgpu.TextureViewRelease(targetView);
                        }
                    }
                    return;
                }

                readbackBuffer.TryReadTextureRows(texture, width, height, (void*)_framebuffer.Address, (uint)_framebuffer.RowBytes);
                context.CleanupPendingResources();
            }
        }

        public void Dispose()
        {
            try
            {
                FlushToFramebuffer();
            }
            finally
            {
                if (!_preserveRecordedCommandsOnDispose)
                {
                    DrawingContext.Clear();
                }

                if (_disposables != null)
                {
                    foreach (var disposable in _disposables)
                    {
                        disposable?.Dispose();
                    }
                }
            }
        }

        private Vector2 TransformPoint(Point pt)
        {
            var p = pt * RenderTransform;
            return new Vector2((float)p.X, (float)p.Y);
        }

        private static ProGPU.Scene.Rect ToProGpuRect(LtrbPixelRect rect)
        {
            return new ProGPU.Scene.Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        private static ProGPU.Vector.PathGeometry CreateRegionGeometry(IList<LtrbPixelRect> rects)
        {
            var geometry = new ProGPU.Vector.PathGeometry { FillRule = ProGPU.Vector.FillRule.Nonzero };
            foreach (var rect in rects)
            {
                if (rect.IsEmpty)
                    continue;

                var figure = new ProGPU.Vector.PathFigure(new Vector2(rect.Left, rect.Top), isClosed: true);
                figure.Segments.Add(new ProGPU.Vector.LineSegment(new Vector2(rect.Right, rect.Top)));
                figure.Segments.Add(new ProGPU.Vector.LineSegment(new Vector2(rect.Right, rect.Bottom)));
                figure.Segments.Add(new ProGPU.Vector.LineSegment(new Vector2(rect.Left, rect.Bottom)));
                geometry.Figures.Add(figure);
            }

            return geometry;
        }

        internal ProGPU.Scene.Rect ToProGpuRect(Avalonia.Rect r)
        {
            var transformed = r.TransformToAABB(RenderTransform);
            return ToLocalProGpuRect(transformed);
        }

        private bool TryDrawSceneBrush(
            IBrush? brush,
            Avalonia.Rect targetRect,
            ProGPU.Vector.PathGeometry clipPath,
            bool useGeometryClip = true)
        {
            ISceneBrushContent? content = null;
            var ownsContent = false;
            if (brush is ISceneBrush sceneBrush)
            {
                content = sceneBrush.CreateContent();
                ownsContent = true;
            }
            else if (brush is ISceneBrushContent sceneBrushContent)
            {
                content = sceneBrushContent;
            }
            else
            {
                return false;
            }

            try
            {
                if (content == null || content.Rect.Width <= 0 || content.Rect.Height <= 0 ||
                    targetRect.Width <= 0 || targetRect.Height <= 0)
                {
                    return true;
                }

                var tileBrush = content.Brush;
                var calculator = new TileBrushCalculator(tileBrush, content.Rect.Size, targetRect.Size);
                var targetOffset = tileBrush.DestinationRect.Unit == RelativeUnit.Relative
                    ? new Vector(targetRect.X, targetRect.Y)
                    : default;

                if (useGeometryClip)
                {
                    DrawingContext.PushGeometryClip(clipPath, ToMatrix4x4(RenderTransform));
                }
                else
                {
                    PushClip(targetRect);
                }
                if (!NearlyEqual(brush.Opacity, 1.0))
                {
                    DrawingContext.PushOpacity((float)brush.Opacity);
                }

                if (tileBrush.TileMode == TileMode.None)
                {
                    var viewport = calculator.IntermediateClip.Translate(targetOffset);
                    PushClip(viewport);
                    content.Render(
                        this,
                        calculator.IntermediateTransform * Matrix.CreateTranslation(targetOffset));
                    PopClip();
                }
                else
                {
                    DrawSceneBrushTiles(content, calculator, targetRect, targetOffset);
                }

                if (!NearlyEqual(brush.Opacity, 1.0))
                {
                    DrawingContext.PopOpacity();
                }
                if (useGeometryClip)
                {
                    DrawingContext.PopGeometryClip();
                }
                else
                {
                    PopClip();
                }
                return true;
            }
            finally
            {
                if (ownsContent)
                {
                    content?.Dispose();
                }
            }
        }

        private void DrawSceneBrushTiles(
            ISceneBrushContent content,
            TileBrushCalculator calculator,
            Avalonia.Rect targetRect,
            Vector targetOffset)
        {
            var tileSize = calculator.DestinationRect.Size;
            if (tileSize.Width <= 0 || tileSize.Height <= 0)
            {
                return;
            }

            var anchor = new Point(
                calculator.DestinationRect.X + targetOffset.X,
                calculator.DestinationRect.Y + targetOffset.Y);
            var firstColumn = (int)Math.Floor((targetRect.Left - anchor.X) / tileSize.Width);
            var lastColumn = (int)Math.Ceiling((targetRect.Right - anchor.X) / tileSize.Width);
            var firstRow = (int)Math.Floor((targetRect.Top - anchor.Y) / tileSize.Height);
            var lastRow = (int)Math.Ceiling((targetRect.Bottom - anchor.Y) / tileSize.Height);

            for (var row = firstRow; row < lastRow; row++)
            {
                for (var column = firstColumn; column < lastColumn; column++)
                {
                    var tilePosition = new Point(
                        anchor.X + column * tileSize.Width,
                        anchor.Y + row * tileSize.Height);
                    var viewport = new Avalonia.Rect(tilePosition, tileSize);
                    var transform = calculator.IntermediateTransform *
                                    Matrix.CreateTranslation((Vector)tilePosition);
                    transform *= CreateTileFlipTransform(content.Brush.TileMode, row, column, viewport);

                    PushClip(viewport);
                    content.Render(this, transform);
                    PopClip();
                }
            }
        }

        private static Matrix CreateTileFlipTransform(
            TileMode tileMode,
            int row,
            int column,
            Avalonia.Rect viewport)
        {
            var flipX = (tileMode == TileMode.FlipX || tileMode == TileMode.FlipXY) && (column & 1) != 0;
            var flipY = (tileMode == TileMode.FlipY || tileMode == TileMode.FlipXY) && (row & 1) != 0;
            if (!flipX && !flipY)
            {
                return Matrix.Identity;
            }

            var center = viewport.Center;
            return Matrix.CreateTranslation(-(Vector)center) *
                   Matrix.CreateScale(flipX ? -1 : 1, flipY ? -1 : 1) *
                   Matrix.CreateTranslation((Vector)center);
        }

        private bool TryDrawImageBrush(
            IBrush? brush,
            Avalonia.Rect targetRect,
            ProGPU.Vector.PathGeometry clipPath)
        {
            if (brush is not IImageBrush imageBrush)
            {
                return false;
            }

            if (imageBrush.Source?.Bitmap?.Item is not IDrawableBitmapImpl bitmap)
            {
                return true;
            }

            bitmap.UploadToGpu();
            if (bitmap.Texture == null)
            {
                return true;
            }

            var imageSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
            if (imageSize.Width <= 0 || imageSize.Height <= 0 || targetRect.Width <= 0 || targetRect.Height <= 0)
            {
                return true;
            }

            var calculator = new TileBrushCalculator(imageBrush, imageSize, targetRect.Size);
            var sourceRect = calculator.SourceRect;
            if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
            {
                return true;
            }

            var targetOffset = imageBrush.DestinationRect.Unit == RelativeUnit.Relative
                ? targetRect.Position
                : default;
            var destinationRect = sourceRect.TransformToAABB(calculator.IntermediateTransform);
            var viewport = calculator.IntermediateClip;
            if (imageBrush.TileMode == TileMode.None)
            {
                destinationRect = destinationRect.Translate(targetOffset);
                viewport = viewport.Translate(targetOffset);
            }
            else
            {
                var tileOffset = targetOffset + calculator.DestinationRect.Position;
                destinationRect = destinationRect.Translate(tileOffset);
                viewport = new Avalonia.Rect(tileOffset, calculator.DestinationRect.Size);
            }

            var sourceScaleX = bitmap.PixelSize.Width / imageSize.Width;
            var sourceScaleY = bitmap.PixelSize.Height / imageSize.Height;
            var textureSourceRect = new Avalonia.Rect(
                sourceRect.X * sourceScaleX,
                sourceRect.Y * sourceScaleY,
                sourceRect.Width * sourceScaleX,
                sourceRect.Height * sourceScaleY);

            var brushTransform = Matrix.Identity;
            if (brush.Transform != null)
            {
                var origin = brush.TransformOrigin.ToPixels(targetRect);
                var offset = Matrix.CreateTranslation(origin);
                brushTransform = (-offset) * brush.Transform.Value * offset;
            }

            var imageTransform = brushTransform * RenderTransform;
            var viewportPath = PrimitivePathGeometry.CreateRectangle(
                (float)viewport.X,
                (float)viewport.Y,
                (float)viewport.Width,
                (float)viewport.Height);

            DrawingContext.PushGeometryClip(clipPath, ToMatrix4x4(RenderTransform));
            DrawingContext.PushGeometryClip(viewportPath, ToMatrix4x4(imageTransform));
            if (!NearlyEqual(brush.Opacity, 1.0))
            {
                DrawingContext.PushOpacity((float)brush.Opacity);
            }

            DrawingContext.DrawTexture(
                bitmap.Texture,
                ToLocalProGpuRect(destinationRect),
                ToLocalProGpuRect(textureSourceRect),
                ToMatrix4x4(imageTransform),
                ToTextureSamplingMode(RenderOptions.BitmapInterpolationMode));

            if (!NearlyEqual(brush.Opacity, 1.0))
            {
                DrawingContext.PopOpacity();
            }
            DrawingContext.PopGeometryClip();
            DrawingContext.PopGeometryClip();
            return true;
        }

        private ProGPU.Vector.Brush? ConvertBrush(IBrush? avaloniaBrush, Avalonia.Rect? targetRect = null)
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
                var bounds = targetRect ?? default;
                var start = TransformPoint(linear.StartPoint.ToPixels(bounds));
                var end = TransformPoint(linear.EndPoint.ToPixels(bounds));
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
                return new ProGPU.Vector.LinearGradientBrush(start, end, stops)
                {
                    Opacity = opacity,
                    SpreadMethod = ToGradientSpreadMethod(linear.SpreadMethod)
                };
            }
            else if (avaloniaBrush is IRadialGradientBrush radial)
            {
                var bounds = targetRect ?? default;
                var centerPoint = radial.Center.ToPixels(bounds);
                var originPoint = radial.GradientOrigin.ToPixels(bounds);
                var center = TransformPoint(centerPoint);
                var origin = TransformPoint(originPoint);
                var radiusXPoint = TransformPoint(centerPoint + new Vector(radial.RadiusX.ToValue(bounds.Width), 0));
                var radiusYPoint = TransformPoint(centerPoint + new Vector(0, radial.RadiusY.ToValue(bounds.Height)));
                var radiusX = Vector2.Distance(center, radiusXPoint);
                var radiusY = Vector2.Distance(center, radiusYPoint);
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
                return new ProGPU.Vector.RadialGradientBrush(center, origin, radiusX, radiusY, stops)
                {
                    Opacity = opacity,
                    SpreadMethod = ToGradientSpreadMethod(radial.SpreadMethod)
                };
            }

            return null;
        }

        private ProGPU.Vector.Pen? ConvertPen(IPen? avaloniaPen, Avalonia.Rect? targetRect = null)
        {
            if (avaloniaPen == null) return null;
            var brush = ConvertBrush(avaloniaPen.Brush, targetRect);
            if (brush == null) return null;
            return new ProGPU.Vector.Pen(brush, (float)avaloniaPen.Thickness);
        }

        private static ProGPU.Scene.Rect ToLocalProGpuRect(Avalonia.Rect rect)
        {
            return new ProGPU.Scene.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        }

        private static ProGPU.Vector.GradientSpreadMethod ToGradientSpreadMethod(
            Avalonia.Media.GradientSpreadMethod spreadMethod)
        {
            return spreadMethod switch
            {
                Avalonia.Media.GradientSpreadMethod.Reflect => ProGPU.Vector.GradientSpreadMethod.Reflect,
                Avalonia.Media.GradientSpreadMethod.Repeat => ProGPU.Vector.GradientSpreadMethod.Repeat,
                _ => ProGPU.Vector.GradientSpreadMethod.Pad
            };
        }

        private static TextureSamplingMode ToTextureSamplingMode(
            Avalonia.Media.Imaging.BitmapInterpolationMode interpolationMode)
        {
            return interpolationMode == Avalonia.Media.Imaging.BitmapInterpolationMode.None
                ? TextureSamplingMode.Nearest
                : TextureSamplingMode.Linear;
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

        private static CompositorHostFrame CreateHostFrame(uint renderTargetWidth, uint renderTargetHeight)
        {
            return CompositorHostFrame.FromRenderTarget(renderTargetWidth, renderTargetHeight, 1f);
        }

        private static bool TryGetDpiScale(Vector dpi, out double scaleX, out double scaleY)
        {
            scaleX = dpi.X / 96.0;
            scaleY = dpi.Y / 96.0;
            return double.IsFinite(scaleX) &&
                   double.IsFinite(scaleY) &&
                   scaleX > 0.0 &&
                   scaleY > 0.0;
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) < 0.0001;
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
                var hostFrame = CreateHostFrame(texture.Width, texture.Height);

                var drawingVisual = new DrawingVisual();
                drawingVisual.Size = hostFrame.LogicalSize;
                drawingVisual.Context.Append(sourceContext);

                try
                {
                    compositor.RenderOffscreen(
                        drawingVisual,
                        hostFrame,
                        texture,
                        0.0f,
                        new Vector4(0f, 0f, 0f, 0f), // Transparent clear color for layers
                        loadExistingContents: !isTextureFresh
                    );
                }
                finally
                {
                    drawingVisual.Context.Clear();
                }
            }
        }
    }
}
