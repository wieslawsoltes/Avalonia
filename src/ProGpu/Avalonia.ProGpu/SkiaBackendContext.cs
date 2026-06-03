using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using ProGPU.Backend;

namespace Avalonia.ProGpu
{
    internal class SkiaContext : IPlatformRenderInterfaceContext
    {
        public SkiaContext(object? gpu)
        {
            PublicFeatures = new Dictionary<Type, object>();
        }
        
        public void Dispose()
        {
        }

        public IRenderTarget CreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            if (surfaces is not IList)
                surfaces = surfaces.ToList();

            foreach (var surface in surfaces)
            {
                if (surface is IFramebufferPlatformSurface framebufferSurface)
                    return new FramebufferRenderTarget(framebufferSurface);
            }

            throw new NotSupportedException(
                "Don't know how to create a ProGpu render target from any of the provided surfaces");
        }

        public bool IsReadyToCreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            if (surfaces is not IList)
                surfaces = surfaces.ToList();

            foreach (var surface in surfaces)
            {
                if (surface is IFramebufferPlatformSurface)
                {
                    return surface.IsReady;
                }
            }

            return false;
        }

        public PixelSize? MaxOffscreenRenderTargetPixelSize => new PixelSize(8192, 8192);
        
        public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize pixelSize, Vector scaling,
            bool enableTextAntialiasing)
        {
            PixelFormat? preferredFormat = null;
            var currentContext = WgpuContext.Current;
            if (currentContext != null)
            {
                preferredFormat = currentContext.SwapChainFormat == Silk.NET.WebGPU.TextureFormat.Rgba8Unorm
                    ? PixelFormats.Rgba8888
                    : PixelFormats.Bgra8888;
            }

            var createInfo = new SurfaceRenderTarget.CreateInfo
            {
                Width = pixelSize.Width,
                Height = pixelSize.Height,
                Dpi = scaling * 96,
                Format = preferredFormat,
                DisableTextLcdRendering = !enableTextAntialiasing
            };

            return new SurfaceRenderTarget(createInfo);
        }

        public bool IsLost => false;
        public IReadOnlyDictionary<Type, object> PublicFeatures { get; }

        public object? TryGetFeature(Type featureType) => null;
    }
}
