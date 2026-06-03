using Xunit;
using ProGPU.Vector;
using Avalonia;

namespace Avalonia.ProGpu.UnitTests
{
    public class CombinedGeometryImplTests
    {
        [Fact]
        public void Combining_Fill_With_Empty_Stroke_Returns_Fill_Bounds()
        {
            var fill = new ProGPU.Vector.PathGeometry();
            var figure = new ProGPU.Vector.PathFigure { StartPoint = new System.Numerics.Vector2(0, 0) };
            figure.Segments.Add(new ProGPU.Vector.LineSegment(new System.Numerics.Vector2(100, 0)));
            figure.Segments.Add(new ProGPU.Vector.LineSegment(new System.Numerics.Vector2(100, 100)));
            figure.Segments.Add(new ProGPU.Vector.LineSegment(new System.Numerics.Vector2(0, 100)));
            fill.Figures.Add(figure);

            var result = new CombinedGeometryImpl(fill);

            Assert.Equal(new Rect(0, 0, 100, 100), result.Bounds);
        }
    }
}
