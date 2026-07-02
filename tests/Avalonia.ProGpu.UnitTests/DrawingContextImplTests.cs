using Avalonia.Media;
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
