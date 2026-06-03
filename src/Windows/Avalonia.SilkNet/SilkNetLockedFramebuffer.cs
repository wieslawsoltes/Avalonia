using System;
using System.Linq;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using ProGPU.Backend;

namespace Avalonia.SilkNet
{
    public class SilkNetLockedFramebuffer : IGpuLockedFramebuffer, IDisposable
    {
        private readonly Action _onDispose;
        private readonly Silk.NET.Windowing.IWindow _window;

        public SilkNetLockedFramebuffer(
            IntPtr address,
            PixelSize size,
            int rowBytes,
            Vector dpi,
            PixelFormat format,
            AlphaFormat alphaFormat,
            Action onDispose,
            Silk.NET.Windowing.IWindow window)
        {
            Address = address;
            Size = size;
            RowBytes = rowBytes;
            Dpi = dpi;
            Format = format;
            AlphaFormat = alphaFormat;
            _onDispose = onDispose;
            _window = window;
        }

        public IntPtr Address { get; }
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi { get; }
        public PixelFormat Format { get; }
        public AlphaFormat AlphaFormat { get; }

        public IntPtr SurfacePointer
        {
            get
            {
                unsafe
                {
                    var context = WgpuContext.ActiveContexts.FirstOrDefault(c => c.Window == _window);
                    if (context != null && context.Surface != null)
                    {
                        return (IntPtr)context.Surface;
                    }
                    return IntPtr.Zero;
                }
            }
        }

        public IntPtr WindowPointer => IntPtr.Zero;

        public void Dispose()
        {
            _onDispose();
        }
    }
}
