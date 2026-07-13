using System;
using System.Threading;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace Avalonia.SilkNet
{
    public class SilkNetFramebufferManager : IFramebufferPlatformSurface, IDisposable
    {
        private readonly Silk.NET.Windowing.IWindow _window;
        private readonly SilkNetFramebufferAddressProvider _addressProvider = new();
        private readonly object _sync = new();
        private readonly Action _unlock;
        private int _disposed;
        
        public SilkNetFramebufferManager(Silk.NET.Windowing.IWindow window)
        {
            _window = window;
            _unlock = Unlock;
        }

        public bool IsReady => Volatile.Read(ref _disposed) == 0 && _window.IsInitialized;

        public ILockedFramebuffer Lock()
        {
            Monitor.Enter(_sync);
            SilkNetLockedFramebuffer? framebuffer = null;
            try
            {
                if (Volatile.Read(ref _disposed) != 0 || !_window.IsInitialized)
                    throw new RenderTargetNotReadyException();

                var framebufferSize = _window.FramebufferSize;
                var size = new PixelSize(framebufferSize.X, framebufferSize.Y);
                var width = Math.Max(1, size.Width);
                var height = Math.Max(1, size.Height);
                var stride = checked(width * 4);
                var totalBytes = checked(stride * height);

                return framebuffer = new SilkNetLockedFramebuffer(
                    _addressProvider,
                    totalBytes,
                    size,
                    stride,
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul,
                    _unlock,
                    _window);
            }
            finally
            {
                if (framebuffer is null)
                    Monitor.Exit(_sync);
            }
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new RenderTarget(this);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_sync)
                _addressProvider.Dispose();
        }

        private void Unlock() => Monitor.Exit(_sync);

        private PlatformRenderTargetState State => Volatile.Read(ref _disposed) != 0
            ? PlatformRenderTargetState.Disposed
            : _window.IsInitialized
                ? PlatformRenderTargetState.Ready
                : PlatformRenderTargetState.NotReadyTryLater;

        private sealed class RenderTarget : IFramebufferRenderTarget
        {
            private SilkNetFramebufferManager? _manager;

            public RenderTarget(SilkNetFramebufferManager manager)
            {
                _manager = manager;
            }

            public PlatformRenderTargetState State =>
                _manager?.State ?? PlatformRenderTargetState.Disposed;

            public ILockedFramebuffer Lock(
                IRenderTarget.RenderTargetSceneInfo sceneInfo,
                out FramebufferLockProperties properties)
            {
                properties = default;
                return (_manager ?? throw new RenderTargetNotReadyException()).Lock();
            }

            public void Dispose() => _manager = null;
        }
    }
}
