using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ProGPU.Backend;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Avalonia.ProGpu
{
    internal class ImmutableBitmap : IDrawableBitmapImpl
    {
        private readonly Action? _customImageDispose = null;
        private readonly object _uploadLock = new();
        public GpuTexture? Texture { get; private set; }
        public Rgba32[] Pixels { get; }
        public PixelSize PixelSize { get; }
        public Vector Dpi { get; }
        public int Version => 1;

        public ImmutableBitmap(Stream stream)
        {
            try
            {
                using var image = Image.Load<Rgba32>(stream);
                PixelSize = new PixelSize(image.Width, image.Height);
                Dpi = new Vector(96, 96);
                Pixels = new Rgba32[image.Width * image.Height];
                image.CopyPixelDataTo(Pixels);
            }
            catch (Exception)
            {
                PixelSize = new PixelSize(1, 1);
                Dpi = new Vector(96, 96);
                Pixels = new Rgba32[] { new Rgba32(0, 0, 0, 0) };
            }
            UploadToGpu();
        }

        public ImmutableBitmap(ImmutableBitmap src, PixelSize destinationSize, BitmapInterpolationMode interpolationMode)
        {
            using var image = Image.LoadPixelData<Rgba32>(src.Pixels, src.PixelSize.Width, src.PixelSize.Height);
            image.Mutate(x => x.Resize(destinationSize.Width, destinationSize.Height));
            PixelSize = destinationSize;
            Dpi = src.Dpi;
            Pixels = new Rgba32[destinationSize.Width * destinationSize.Height];
            image.CopyPixelDataTo(Pixels);
            UploadToGpu();
        }

        public ImmutableBitmap(Stream stream, int decodeSize, bool horizontal, BitmapInterpolationMode interpolationMode)
        {
            try
            {
                using var image = Image.Load<Rgba32>(stream);
                double scale = horizontal ? (double)decodeSize / image.Width : (double)decodeSize / image.Height;
                int w = horizontal ? decodeSize : (int)(image.Width * scale);
                int h = horizontal ? (int)(image.Height * scale) : decodeSize;
                image.Mutate(x => x.Resize(w, h));
                PixelSize = new PixelSize(w, h);
                Dpi = new Vector(96, 96);
                Pixels = new Rgba32[w * h];
                image.CopyPixelDataTo(Pixels);
            }
            catch (Exception)
            {
                PixelSize = new PixelSize(1, 1);
                Dpi = new Vector(96, 96);
                Pixels = new Rgba32[] { new Rgba32(0, 0, 0, 0) };
            }
            UploadToGpu();
        }

        public ImmutableBitmap(PixelSize size, Vector dpi, int stride, PixelFormat format, AlphaFormat alphaFormat, IntPtr data)
        {
            PixelSize = size;
            Dpi = dpi;
            Pixels = new Rgba32[size.Width * size.Height];
            unsafe
            {
                byte* srcPtr = (byte*)data;
                for (int y = 0; y < size.Height; y++)
                {
                    byte* rowPtr = srcPtr + y * stride;
                    for (int x = 0; x < size.Width; x++)
                    {
                        byte r = 0, g = 0, b = 0, a = 255;
                        if (format == PixelFormats.Bgra8888)
                        {
                            b = rowPtr[x * 4];
                            g = rowPtr[x * 4 + 1];
                            r = rowPtr[x * 4 + 2];
                            a = rowPtr[x * 4 + 3];
                        }
                        else if (format == PixelFormats.Rgba8888)
                        {
                            r = rowPtr[x * 4];
                            g = rowPtr[x * 4 + 1];
                            b = rowPtr[x * 4 + 2];
                            a = rowPtr[x * 4 + 3];
                        }
                        Pixels[y * size.Width + x] = new Rgba32(r, g, b, a);
                    }
                }
            }
            UploadToGpu();
        }

        public void UploadToGpu()
        {
            if (Texture != null) return;
            lock (_uploadLock)
            {
                if (Texture != null) return;
                var context = WgpuContext.Current;
                if (context != null)
                {
                    lock (context.RenderLock)
                    {
                        if (context.IsDisposed) return;
                        Texture = new GpuTexture(
                            context,
                            (uint)PixelSize.Width,
                            (uint)PixelSize.Height,
                            Silk.NET.WebGPU.TextureFormat.Rgba8Unorm,
                            Silk.NET.WebGPU.TextureUsage.TextureBinding | Silk.NET.WebGPU.TextureUsage.CopyDst,
                            "ImmutableBitmap"
                        );
                        Texture.WritePixels(new ReadOnlySpan<Rgba32>(Pixels));
                    }
                }
            }
        }

        public void Save(string fileName, int? quality = null)
        {
            using var image = Image.LoadPixelData<Rgba32>(Pixels, PixelSize.Width, PixelSize.Height);
            image.Save(fileName);
        }

        public void Save(Stream stream, int? quality = null)
        {
            using var image = Image.LoadPixelData<Rgba32>(Pixels, PixelSize.Width, PixelSize.Height);
            image.SaveAsPng(stream);
        }

        public void Dispose()
        {
            Texture?.Dispose();
            Texture = null;
            _customImageDispose?.Invoke();
        }
    }
}
