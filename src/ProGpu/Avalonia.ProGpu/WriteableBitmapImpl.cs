using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ProGPU.Backend;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Avalonia.ProGpu
{
    internal class WriteableBitmapImpl : IWriteableBitmapImpl, IDrawableBitmapImpl
    {
        private readonly object _lock = new();
        private IntPtr _address;
        private int _stride;
        private bool _isDisposed;
        private readonly PixelFormat _format;

        public GpuTexture? Texture { get; private set; }
        public PixelSize PixelSize { get; }
        public Vector Dpi { get; }
        public int Version { get; private set; } = 1;
        public PixelFormat? Format => _format;
        public AlphaFormat? AlphaFormat => Platform.AlphaFormat.Premul;

        public WriteableBitmapImpl(Stream stream)
        {
            _format = PixelFormats.Rgba8888;
            using var image = Image.Load<Rgba32>(stream);
            PixelSize = new PixelSize(image.Width, image.Height);
            Dpi = new Vector(96, 96);
            _stride = image.Width * 4;
            _address = Marshal.AllocHGlobal(image.Width * image.Height * 4);

            unsafe
            {
                var span = new Span<Rgba32>((void*)_address, image.Width * image.Height);
                image.CopyPixelDataTo(span);
            }
            UploadToGpu();
        }

        public WriteableBitmapImpl(Stream stream, int decodeSize, bool horizontal, BitmapInterpolationMode interpolationMode)
        {
            _format = PixelFormats.Rgba8888;
            using var image = Image.Load<Rgba32>(stream);
            double scale = horizontal ? (double)decodeSize / image.Width : (double)decodeSize / image.Height;
            int w = horizontal ? decodeSize : (int)(image.Width * scale);
            int h = horizontal ? (int)(image.Height * scale) : decodeSize;
            image.Mutate(x => x.Resize(w, h));

            PixelSize = new PixelSize(w, h);
            Dpi = new Vector(96, 96);
            _stride = w * 4;
            _address = Marshal.AllocHGlobal(w * h * 4);

            unsafe
            {
                var span = new Span<Rgba32>((void*)_address, w * h);
                image.CopyPixelDataTo(span);
            }
            UploadToGpu();
        }

        public WriteableBitmapImpl(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
        {
            _format = format;
            PixelSize = size;
            Dpi = dpi;
            _stride = size.Width * 4;
            _address = Marshal.AllocHGlobal(size.Width * size.Height * 4);

            unsafe
            {
                var span = new Span<byte>((void*)_address, size.Width * size.Height * 4);
                span.Clear();
            }
            UploadToGpu();
        }

        public void UploadToGpu()
        {
            lock (_lock)
            {
                if (_isDisposed || _address == IntPtr.Zero) return;

                var context = WgpuContext.Current;
                if (context != null)
                {
                    lock (context.RenderLock)
                    {
                        if (context.IsDisposed) return;
                        if (Texture == null)
                        {
                            var wgpuFormat = Silk.NET.WebGPU.TextureFormat.Rgba8Unorm;
                            if (_format == PixelFormats.Bgra8888)
                            {
                                wgpuFormat = Silk.NET.WebGPU.TextureFormat.Bgra8Unorm;
                            }

                            Texture = new GpuTexture(
                                context,
                                (uint)PixelSize.Width,
                                (uint)PixelSize.Height,
                                wgpuFormat,
                                Silk.NET.WebGPU.TextureUsage.TextureBinding | Silk.NET.WebGPU.TextureUsage.CopyDst,
                                "WriteableBitmap"
                            );
                        }
                        unsafe
                        {
                            var span = new ReadOnlySpan<byte>((void*)_address, PixelSize.Width * PixelSize.Height * 4);
                            Texture.WritePixels(span);
                        }
                    }
                }
            }
        }

        public void Save(string fileName, int? quality = null)
        {
            unsafe
            {
                var span = new ReadOnlySpan<Rgba32>((void*)_address, PixelSize.Width * PixelSize.Height);
                using var image = Image.LoadPixelData<Rgba32>(span, PixelSize.Width, PixelSize.Height);
                image.Save(fileName);
            }
        }

        public void Save(Stream stream, int? quality = null)
        {
            unsafe
            {
                var span = new ReadOnlySpan<Rgba32>((void*)_address, PixelSize.Width * PixelSize.Height);
                using var image = Image.LoadPixelData<Rgba32>(span, PixelSize.Width, PixelSize.Height);
                image.SaveAsPng(stream);
            }
        }

        public ILockedFramebuffer Lock()
        {
            return new WriteableBitmapFramebuffer(this);
        }

        public virtual void Dispose()
        {
            lock (_lock)
            {
                if (!_isDisposed)
                {
                    Texture?.Dispose();
                    Texture = null;
                    if (_address != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_address);
                        _address = IntPtr.Zero;
                    }
                    _isDisposed = true;
                }
            }
        }

        private class WriteableBitmapFramebuffer : ILockedFramebuffer
        {
            private readonly WriteableBitmapImpl _parent;

            public WriteableBitmapFramebuffer(WriteableBitmapImpl parent)
            {
                _parent = parent;
                Monitor.Enter(parent._lock);
            }

            public void Dispose()
            {
                _parent.Version++;
                _parent.UploadToGpu();
                Monitor.Exit(_parent._lock);
            }

            public IntPtr Address => _parent._address;
            public PixelSize Size => _parent.PixelSize;
            public int RowBytes => _parent._stride;
            public Vector Dpi => _parent.Dpi;
            public PixelFormat Format => _parent._format;
            public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        }
    }
}
