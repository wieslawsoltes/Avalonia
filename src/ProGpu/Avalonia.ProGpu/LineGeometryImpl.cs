using System.Numerics;
using ProGPU.Vector;

namespace Avalonia.ProGpu
{
    internal class LineGeometryImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public LineGeometryImpl(Point p1, Point p2)
        {
            var path = new PathGeometry();
            var figure = new PathFigure(new Vector2((float)p1.X, (float)p1.Y));
            figure.Segments.Add(new LineSegment(new Vector2((float)p2.X, (float)p2.Y)));
            figure.IsClosed = false;
            figure.IsFilled = false;
            path.Figures.Add(figure);

            Path = path;
        }
    }
}
