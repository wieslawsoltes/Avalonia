using Avalonia.Media;
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

        private static DrawingContextImpl CreateTarget()
        {
            var createInfo = new DrawingContextImpl.CreateInfo
            {
                Dpi = new Vector(96, 96),
                ScaleDrawingToDpi = false
            };
            return new DrawingContextImpl(createInfo);
        }
    }
}
