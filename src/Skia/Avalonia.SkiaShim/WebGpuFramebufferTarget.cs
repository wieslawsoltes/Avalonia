using System;
using Avalonia.Platform;
using ProGPU.Backend;
using Silk.NET.WebGPU;
using SkiaSharp;

namespace Avalonia.Skia;

internal sealed unsafe class WebGpuFramebufferTarget : IDisposable
{
    private const string SurfaceHandleDescriptor = "WGPU_SURFACE";

    private WgpuContext? _context;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _backendRenderTarget;
    private GpuTexture? _texture;
    private SKSurface? _surface;

    public bool HasRetainedFrame { get; private set; }

    public static bool TryResolveContext(ILockedFramebuffer framebuffer, out WgpuContext context)
    {
        if (framebuffer is not IPlatformHandle
            {
                HandleDescriptor: SurfaceHandleDescriptor,
                Handle: var surfaceHandle
            } || surfaceHandle == IntPtr.Zero)
        {
            context = null!;
            return false;
        }

        var current = WgpuContext.Current;
        if (MatchesSurface(current, surfaceHandle))
        {
            context = current!;
            return true;
        }

        var activeContexts = WgpuContext.ActiveContexts;
        for (var index = 0; index < activeContexts.Count; index++)
        {
            if (MatchesSurface(activeContexts[index], surfaceHandle))
            {
                context = activeContexts[index];
                return true;
            }
        }

        context = null!;
        return false;
    }

    public IDrawingContextImpl CreateDrawingContext(
        ILockedFramebuffer framebuffer,
        WgpuContext context,
        bool useScaledDrawing,
        out RenderTargetDrawingContextProperties properties)
    {
        if (framebuffer.Size.Width <= 0 || framebuffer.Size.Height <= 0)
        {
            throw new ArgumentException(
                $"Unable to create a WebGPU surface with size {framebuffer.Size.Width}x{framebuffer.Size.Height}.",
                nameof(framebuffer));
        }

        lock (context.RenderLock)
        {
            if (context.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(WgpuContext));
            }

            context.ReconfigureIfNeeded((uint)framebuffer.Size.Width, (uint)framebuffer.Size.Height);
            EnsureResources(
                context,
                (uint)framebuffer.Size.Width,
                (uint)framebuffer.Size.Height);
        }

        var canvas = _surface!.Canvas;
        canvas.RestoreToCount(-1);
        canvas.Save();
        canvas.ResetMatrix();

        var createInfo = new DrawingContextImpl.CreateInfo
        {
            Surface = _surface,
            Dpi = framebuffer.Dpi,
            ScaleDrawingToDpi = useScaledDrawing,
            GrContext = _grContext
        };
        properties = new RenderTargetDrawingContextProperties
        {
            PreviousFrameIsRetained = HasRetainedFrame
        };

        var currentScope = WgpuContext.PushCurrent(context);
        try
        {
            return new DrawingContextImpl(
                createInfo,
                new FramePresenter(
                    this,
                    framebuffer,
                    ((IPlatformHandle)framebuffer).Handle,
                    currentScope));
        }
        catch
        {
            currentScope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        ReleaseResources();
    }

    private static bool MatchesSurface(WgpuContext? context, IntPtr surfaceHandle)
    {
        return context is { IsDisposed: false }
               && (IntPtr)context.Surface == surfaceHandle;
    }

    private void EnsureResources(WgpuContext context, uint width, uint height)
    {
        if (ReferenceEquals(_context, context)
            && _texture is { IsDisposed: false }
            && _texture.Width == width
            && _texture.Height == height
            && _texture.Format == TextureFormat.Rgba8Unorm)
        {
            return;
        }

        ReleaseResources();

        _context = context;
        try
        {
            _texture = new GpuTexture(
                context,
                width,
                height,
                TextureFormat.Rgba8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
                "Avalonia SkiaSharp shim WebGPU target",
                alphaMode: GpuTextureAlphaMode.Premultiplied);
            _texture.ClearRenderTarget();
            _grContext = new GRContext(context);
            _backendRenderTarget = new GRBackendRenderTarget((int)width, (int)height, _texture);
            _surface = SKSurface.Create(
                _grContext,
                _backendRenderTarget,
                GRSurfaceOrigin.TopLeft,
                SKColorType.Rgba8888);
            HasRetainedFrame = false;
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }

    private void ReleaseResources()
    {
        var surface = _surface;
        var backendRenderTarget = _backendRenderTarget;
        var texture = _texture;
        var grContext = _grContext;
        _surface = null;
        _backendRenderTarget = null;
        _texture = null;
        _grContext = null;
        _context = null;
        HasRetainedFrame = false;

        try
        {
            surface?.Dispose();
        }
        finally
        {
            try
            {
                backendRenderTarget?.Dispose();
            }
            finally
            {
                try
                {
                    texture?.Dispose();
                }
                finally
                {
                    grContext?.Dispose();
                }
            }
        }
    }

    private void FlushAndPresent(IntPtr surfaceHandle)
    {
        var context = _context;
        var texture = _texture;
        var surface = _surface;
        if (context == null || texture == null || surface == null || context.IsDisposed)
        {
            return;
        }

        lock (context.RenderLock)
        {
            if (context.IsDisposed)
            {
                return;
            }

            using var currentScope = WgpuContext.PushCurrent(context);
            surface.Flush();
            HasRetainedFrame = true;

            context.ReconfigureIfNeeded(texture.Width, texture.Height);
            var surfaceTexture = new SurfaceTexture();
            TextureView* targetView = null;
            context.Wgpu.SurfaceGetCurrentTexture((Surface*)surfaceHandle, &surfaceTexture);
            try
            {
                if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
                {
                    return;
                }

                var viewDescriptor = new TextureViewDescriptor
                {
                    Format = context.SwapChainFormat,
                    Dimension = TextureViewDimension.Dimension2D,
                    BaseMipLevel = 0,
                    MipLevelCount = 1,
                    BaseArrayLayer = 0,
                    ArrayLayerCount = 1,
                    Aspect = TextureAspect.All
                };
                targetView = context.Wgpu.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
                if (targetView == null)
                {
                    return;
                }

                GpuTextureBlitter.Blit(texture, targetView, context.SwapChainFormat);
                context.Wgpu.SurfacePresent((Surface*)surfaceHandle);
            }
            finally
            {
                if (targetView != null)
                {
                    context.Wgpu.TextureViewRelease(targetView);
                }
                if (surfaceTexture.Texture != null)
                {
                    context.Wgpu.TextureRelease(surfaceTexture.Texture);
                }
            }
        }
    }

    private sealed class FramePresenter : IDisposable
    {
        private WebGpuFramebufferTarget? _owner;
        private ILockedFramebuffer? _framebuffer;
        private readonly IntPtr _surfaceHandle;
        private readonly WgpuContext.CurrentContextScope _currentScope;
        private bool _isDisposed;

        public FramePresenter(
            WebGpuFramebufferTarget owner,
            ILockedFramebuffer framebuffer,
            IntPtr surfaceHandle,
            WgpuContext.CurrentContextScope currentScope)
        {
            _owner = owner;
            _framebuffer = framebuffer;
            _surfaceHandle = surfaceHandle;
            _currentScope = currentScope;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            var owner = _owner;
            var framebuffer = _framebuffer;
            _owner = null;
            _framebuffer = null;
            try
            {
                owner?.FlushAndPresent(_surfaceHandle);
            }
            finally
            {
                try
                {
                    framebuffer?.Dispose();
                }
                finally
                {
                    _currentScope.Dispose();
                }
            }
        }
    }
}
