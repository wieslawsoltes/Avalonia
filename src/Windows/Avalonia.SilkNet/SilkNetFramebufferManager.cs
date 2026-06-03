using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace Avalonia.SilkNet
{
    public class SilkNetFramebufferManager : IFramebufferPlatformSurface, IDisposable
    {
        private readonly Silk.NET.Windowing.IWindow _window;
        private byte[]? _buffer;
        private GCHandle _bufferHandle;
        
        public SilkNetFramebufferManager(Silk.NET.Windowing.IWindow window)
        {
            _window = window;
        }

        public ILockedFramebuffer Lock()
        {
            var size = new PixelSize((int)_window.FramebufferSize.X, (int)_window.FramebufferSize.Y);
            int width = Math.Max(1, size.Width);
            int height = Math.Max(1, size.Height);
            int stride = width * 4;
            int totalBytes = stride * height;

            if (_buffer == null || _buffer.Length != totalBytes)
            {
                if (_bufferHandle.IsAllocated)
                    _bufferHandle.Free();

                _buffer = new byte[totalBytes];
                _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            }

            var address = _bufferHandle.AddrOfPinnedObject();
            var dpi = new Vector(96, 96);

            return new SilkNetLockedFramebuffer(
                address,
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

        public void Dispose()
        {
            if (_bufferHandle.IsAllocated)
                _bufferHandle.Free();
            _buffer = null;
        }
    }
}
