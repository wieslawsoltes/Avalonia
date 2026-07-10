using System;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Silk.NET.WebGPU;

namespace Avalonia.ProGpu
{
    /// <summary>
    /// Render target that renders using the ProGpu drawing context.
    /// </summary>
    internal class FramebufferRenderTarget : IRenderTarget
    {
        private readonly bool _useScaledDrawing;
        private IFramebufferRenderTarget? _renderTarget;
        private readonly OffscreenTextureCache _textureCache = new();

        public FramebufferRenderTarget(IFramebufferPlatformSurface platformSurface, bool useScaledDrawing = false)
        {
            _useScaledDrawing = useScaledDrawing;
            _renderTarget = platformSurface.CreateFramebufferRenderTarget();
        }

        public void Dispose()
        {
            _renderTarget?.Dispose();
            _renderTarget = null;
            _textureCache.Dispose();
        }

        public RenderTargetProperties Properties => new()
        {
            RetainsPreviousFrameContents = true,
            IsSuitableForDirectRendering = true
        };

        public PlatformRenderTargetState PlatformRenderTargetState =>
            _renderTarget?.State ?? PlatformRenderTargetState.Disposed;

        public IDrawingContextImpl CreateDrawingContext(IRenderTarget.RenderTargetSceneInfo sceneInfo,
            out RenderTargetDrawingContextProperties properties)
        {
            if (_renderTarget == null)
                throw new ObjectDisposedException(nameof(FramebufferRenderTarget));
            
            var framebuffer = _renderTarget.Lock(sceneInfo, out _);
            var renderFormat = framebuffer.Format == PixelFormats.Rgba8888
                ? TextureFormat.Rgba8Unorm
                : TextureFormat.Bgra8Unorm;

            var createInfo = new DrawingContextImpl.CreateInfo
            {
                Dpi = framebuffer.Dpi,
                ScaleDrawingToDpi = _useScaledDrawing,
                CacheHolder = _textureCache
            };

            properties = new()
            {
                PreviousFrameIsRetained = framebuffer.Size.Width > 0
                                          && framebuffer.Size.Height > 0
                                          && _textureCache.HasRetainedFrame(
                                              (uint)framebuffer.Size.Width,
                                              (uint)framebuffer.Size.Height,
                                              renderFormat)
            };
            
            return new DrawingContextImpl(createInfo, framebuffer);
        }
    }
}
