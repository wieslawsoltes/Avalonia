using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.UnitTests;
using Avalonia.Utilities;
using ProGPU.Backend;
using ProGPU.Scene;
using Xunit;

namespace Avalonia.ProGpu.UnitTests
{
    public class DrawingContextImplTests
    {
        [Fact]
        public void DrawLine_With_Zero_Thickness_Pen_Does_Not_Throw()
        {
            var target = CreateTarget();
            target.DrawLine(new Pen(Brushes.Black, 0), new Point(0, 0), new Point(10, 10));
        }

        [Fact]
        public void DrawRectangle_With_Zero_Thickness_Pen_Does_Not_Throw()
        {
            var target = CreateTarget();
            target.DrawRectangle(Brushes.Black, new Pen(Brushes.Black, 0), new RoundedRect(new Rect(0, 0, 100, 100), new CornerRadius(4)));
        }

        [Fact]
        public void ScaleDrawingToDpi_Applies_Dpi_PostTransform_To_DrawCommands()
        {
            var target = CreateTarget(new Vector(192, 144), scaleDrawingToDpi: true);

            target.DrawLine(new Pen(Brushes.Black, 1), new Point(1, 2), new Point(3, 4));

            var command = Assert.Single(target.DrawingContext.Commands);
            Assert.Equal(RenderCommandType.DrawLine, command.Type);
            Assert.Equal(2f, command.Position.X);
            Assert.Equal(3f, command.Position.Y);
            Assert.Equal(6f, command.Position2.X);
            Assert.Equal(6f, command.Position2.Y);
        }

        [Fact]
        public void Multi_Rect_Region_Uses_Geometry_Clip_With_Matching_Nested_Pops()
        {
            var target = CreateTarget();
            var region = new SkiaRegionImpl();
            region.AddRect(new LtrbPixelRect(10, 20, 30, 40));
            region.AddRect(new LtrbPixelRect(50, 60, 80, 90));

            target.PushClip(region);
            target.PushClip(new Rect(1, 2, 3, 4));
            target.PopClip();
            target.PopClip();

            Assert.Collection(target.DrawingContext.Commands,
                command =>
                {
                    Assert.Equal(RenderCommandType.PushGeometryClip, command.Type);
                    Assert.Equal(2, command.Path?.Figures.Count);
                },
                command => Assert.Equal(RenderCommandType.PushClip, command.Type),
                command => Assert.Equal(RenderCommandType.PopClip, command.Type),
                command => Assert.Equal(RenderCommandType.PopGeometryClip, command.Type));
        }

        [Fact]
        public void Single_Rect_Region_Uses_Rectangle_Clip()
        {
            var target = CreateTarget();
            var region = new SkiaRegionImpl();
            region.AddRect(new LtrbPixelRect(10, 20, 30, 40));

            target.PushClip(region);
            target.PopClip();

            Assert.Collection(target.DrawingContext.Commands,
                command =>
                {
                    Assert.Equal(RenderCommandType.PushClip, command.Type);
                    Assert.Equal(new ProGPU.Scene.Rect(10, 20, 20, 20), command.Rect);
                },
                command => Assert.Equal(RenderCommandType.PopClip, command.Type));
        }

        [Fact]
        public void DrawRectangle_Records_Local_Rect_And_Full_Transform()
        {
            var target = CreateTarget();
            var transform = Matrix.CreateRotation(Math.PI / 6) * Matrix.CreateTranslation(20, 30);
            target.Transform = transform;

            target.DrawRectangle(Brushes.Red, null, new RoundedRect(new Rect(1, 2, 30, 40)));

            var command = Assert.Single(target.DrawingContext.Commands);
            Assert.Equal(RenderCommandType.DrawRect, command.Type);
            Assert.Equal(new ProGPU.Scene.Rect(1, 2, 30, 40), command.Rect);
            Assert.Equal((float)transform.M11, command.Transform.M11);
            Assert.Equal((float)transform.M12, command.Transform.M12);
            Assert.Equal((float)transform.M21, command.Transform.M21);
            Assert.Equal((float)transform.M22, command.Transform.M22);
            Assert.Equal((float)transform.M31, command.Transform.M41);
            Assert.Equal((float)transform.M32, command.Transform.M42);
        }

        [Fact]
        public void Rotated_Rectangle_Clip_Uses_All_Four_Corners()
        {
            var target = CreateTarget();
            target.Transform = Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(20, 4);

            target.PushClip(new Rect(0, 0, 12, 4));

            var command = Assert.Single(target.DrawingContext.Commands);
            Assert.Equal(RenderCommandType.PushClip, command.Type);
            Assert.Equal(16, command.Rect.X, 3);
            Assert.Equal(4, command.Rect.Y, 3);
            Assert.Equal(4, command.Rect.Width, 3);
            Assert.Equal(12, command.Rect.Height, 3);
        }

        [Fact]
        public void ImageBrush_Records_Texture_Command_With_Premultiplied_Alpha()
        {
            var target = CreateTarget();
            var data = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.Copy(new byte[]
                {
                    0, 0, 255, 255,
                    0, 255, 0, 255,
                    255, 0, 0, 255,
                    0, 0, 0, 0
                }, 0, data, 16);

                var impl = new ImmutableBitmap(
                    new PixelSize(2, 2),
                    new Vector(96, 96),
                    8,
                    PixelFormats.Rgba8888,
                    AlphaFormat.Premul,
                    data);
                using var bitmapRef = RefCountable.Create<IBitmapImpl>(impl);
                using var bitmap = new Bitmap(bitmapRef);

                target.DrawRectangle(
                    new ImageBrush(bitmap),
                    null,
                    new RoundedRect(new Rect(10, 20, 40, 30)));

                var command = Assert.Single(
                    target.DrawingContext.Commands.Where(x => x.Type == RenderCommandType.DrawTexture));
                Assert.Equal(new ProGPU.Scene.Rect(15, 20, 30, 30), command.Rect);
                Assert.Equal(new ProGPU.Scene.Rect(0, 0, 2, 2), command.SrcRect);
                Assert.Equal(GpuTextureAlphaMode.Premultiplied, command.Texture?.AlphaMode);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        [Fact]
        public void DrawingBrush_OpacityMask_Survives_Recording_Context_Dispose()
        {
            using var app = UnitTestApplication.Start(
                TestServices.MockPlatformRenderInterface.With(renderInterface: new PlatformRenderInterface()));
            var renderTarget = new SurfaceRenderTarget(new SurfaceRenderTarget.CreateInfo
            {
                Width = 100,
                Height = 20,
                Dpi = new Vector(96, 96)
            });
            var target = Assert.IsType<DrawingContextImpl>(renderTarget.CreateDrawingContext());
            var mask = new DrawingBrush
            {
                Drawing = new GeometryDrawing
                {
                    Brush = Brushes.Black,
                    Geometry = new GeometryGroup
                    {
                        Children =
                        {
                            new RectangleGeometry(new Rect(0, 0, 30, 20)),
                            new RectangleGeometry(new Rect(70, 0, 30, 20))
                        }
                    }
                }
            };

            target.PushOpacityMask(mask, new Rect(0, 0, 100, 20));
            target.PopOpacityMask();

            Assert.Collection(target.DrawingContext.Commands,
                command =>
                {
                    Assert.Equal(RenderCommandType.PushOpacityMask, command.Type);
                    Assert.NotNull(command.Picture);
                    Assert.Contains(command.Picture.Commands, nested => nested.Type == RenderCommandType.DrawPath);
                },
                command => Assert.Equal(RenderCommandType.PopOpacityMask, command.Type));
            Assert.Equal(1, target.DrawingContext.RetainedResourceCount);

            target.Dispose();

            Assert.Equal(2, target.DrawingContext.Commands.Count);
            Assert.Equal(1, target.DrawingContext.RetainedResourceCount);

            renderTarget.Dispose();

            Assert.Empty(target.DrawingContext.Commands);
            Assert.Equal(0, target.DrawingContext.RetainedResourceCount);
        }

        private static DrawingContextImpl CreateTarget()
        {
            return CreateTarget(new Vector(96, 96), scaleDrawingToDpi: false);
        }

        private static DrawingContextImpl CreateTarget(Vector dpi, bool scaleDrawingToDpi)
        {
            var createInfo = new DrawingContextImpl.CreateInfo
            {
                Dpi = dpi,
                ScaleDrawingToDpi = scaleDrawingToDpi
            };
            return new DrawingContextImpl(createInfo);
        }
    }
}
