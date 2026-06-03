using System;
using System.IO;
using Avalonia.Platform;
using ProGPU.Backend;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Avalonia.ProGpu
{
    internal class SurfaceRenderTarget : IDrawableBitmapImpl, IDrawingContextLayerWithRenderContextAffinityImpl
    {
        private readonly DrawingContextImpl _layerContext;
        private bool _isTextureFresh = true;

        public struct CreateInfo
        {
            public int Width;
            public int Height;
            public Vector Dpi;
            public bool UseScaledDrawing;
            public bool DisableTextLcdRendering;
            public PixelFormat? Format;
        }

        public SurfaceRenderTarget(CreateInfo createInfo)
        {
            Console.WriteLine($"[SurfaceRenderTarget] size={createInfo.Width}x{createInfo.Height} format={createInfo.Format}");
            PixelSize = new PixelSize(createInfo.Width, createInfo.Height);
            Dpi = createInfo.Dpi;

            var drawingCreateInfo = new DrawingContextImpl.CreateInfo
            {
                Size = PixelSize,
                Dpi = Dpi,
                ScaleDrawingToDpi = createInfo.UseScaledDrawing,
                DisableSubpixelTextRendering = createInfo.DisableTextLcdRendering
            };

            _layerContext = new DrawingContextImpl(drawingCreateInfo);

            var context = WgpuContext.Current;
            if (context != null)
            {
                var format = Silk.NET.WebGPU.TextureFormat.Bgra8Unorm;
                if (createInfo.Format == PixelFormats.Rgba8888)
                {
                    format = Silk.NET.WebGPU.TextureFormat.Rgba8Unorm;
                }

                Texture = new GpuTexture(
                    context,
                    (uint)PixelSize.Width,
                    (uint)PixelSize.Height,
                    format,
                    Silk.NET.WebGPU.TextureUsage.TextureBinding | Silk.NET.WebGPU.TextureUsage.RenderAttachment,
                    "SurfaceRenderTarget"
                );
            }
        }

        public GpuTexture? Texture { get; }

        public void UploadToGpu()
        {
        }

        public RenderTargetProperties Properties => default;

        public void Dispose()
        {
            _layerContext.Dispose();
            Texture?.Dispose();
        }

        public IDrawingContextImpl CreateDrawingContext()
        {
            _layerContext.Reset();
            return _layerContext;
        }

        public bool IsCorrupted => false;
        public Vector Dpi { get; }
        public PixelSize PixelSize { get; }
        public int Version { get; private set; } = 1;

        public void Save(string fileName, int? quality = null)
        {
            using var image = new Image<Rgba32>(PixelSize.Width, PixelSize.Height);
            image.Save(fileName);
        }

        public void Save(Stream stream, int? quality = null)
        {
            using var image = new Image<Rgba32>(PixelSize.Width, PixelSize.Height);
            image.Save(stream, SixLabors.ImageSharp.Formats.Png.PngFormat.Instance);
        }

        public void Blit(IDrawingContextImpl contextImpl)
        {
            if (contextImpl is DrawingContextImpl target)
            {
                Console.WriteLine($"[Blit] targetSize={target._size.Width}x{target._size.Height} layerSize={PixelSize.Width}x{PixelSize.Height} commands={_layerContext.DrawingContext.Commands.Count}");
                if (Texture != null)
                {
                    if (_layerContext.DrawingContext.Commands.Count > 0)
                    {
                        DrawingContextImpl.RenderToTexture(_layerContext.DrawingContext, Texture, Dpi, _isTextureFresh);
                        _isTextureFresh = false;
                        _layerContext.DrawingContext.Clear();
                    }

                    double scaleX = Math.Abs(target.Transform.M11);
                    double scaleY = Math.Abs(target.Transform.M22);
                    if (scaleX <= 0.0001) scaleX = 1.0;
                    if (scaleY <= 0.0001) scaleY = 1.0;
                    var logicalRect = new Avalonia.Rect(0, 0, PixelSize.Width / scaleX, PixelSize.Height / scaleY);
                    var destRect = target.ToProGpuRect(logicalRect);
                    Console.WriteLine($"[Blit] targetTransform={target.Transform.M11},{target.Transform.M12},{target.Transform.M21},{target.Transform.M22},{target.Transform.M31},{target.Transform.M32} logical={logicalRect.Width}x{logicalRect.Height} destRect={destRect.X},{destRect.Y},{destRect.Width},{destRect.Height}");
                    target.DrawingContext.DrawTexture(Texture, destRect);
                }
                else
                {
                    target.DrawingContext.Append(_layerContext.DrawingContext);
                }
                Version++;
            }
        }

        public bool CanBlit => true;

        public bool HasRenderContextAffinity => Texture != null;

        public IBitmapImpl CreateNonAffinedSnapshot()
        {
            return this;
        }
    }
}
