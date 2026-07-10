using System;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace Avalonia.SilkNet
{
    public class SilkNetFramebufferManager : IFramebufferPlatformSurface, IDisposable
    {
        private readonly Silk.NET.Windowing.IWindow _window;
        private readonly SilkNetFramebufferAddressProvider _addressProvider = new();
        
        public SilkNetFramebufferManager(Silk.NET.Windowing.IWindow window)
        {
            _window = window;
        }

        public ILockedFramebuffer Lock()
        {
            var size = new PixelSize((int)_window.FramebufferSize.X, (int)_window.FramebufferSize.Y);
            int width = Math.Max(1, size.Width);
            int height = Math.Max(1, size.Height);
            int stride = checked(width * 4);
            int totalBytes = checked(stride * height);

            var dpi = new Vector(96, 96);

            return new SilkNetLockedFramebuffer(
                _addressProvider,
                totalBytes,
                size,
                stride,
                dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                () => {
                },
                _window
            );
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new FuncFramebufferRenderTarget(Lock);

        public void Dispose() => _addressProvider.Dispose();
    }
}
