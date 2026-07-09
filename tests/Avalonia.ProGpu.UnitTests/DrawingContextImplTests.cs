using Avalonia.Media;
using Avalonia.Platform;
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
