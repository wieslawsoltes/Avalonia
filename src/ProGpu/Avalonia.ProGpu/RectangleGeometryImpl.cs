using System.Numerics;
using ProGPU.Vector;

namespace Avalonia.ProGpu
{
    internal class RectangleGeometryImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public RectangleGeometryImpl(Rect rect)
        {
            var path = new PathGeometry();
            var figure = new PathFigure(new Vector2((float)rect.X, (float)rect.Y));
            
            figure.Segments.Add(new LineSegment(new Vector2((float)rect.Right, (float)rect.Y)));
            figure.Segments.Add(new LineSegment(new Vector2((float)rect.Right, (float)rect.Bottom)));
            figure.Segments.Add(new LineSegment(new Vector2((float)rect.X, (float)rect.Bottom)));
            
            figure.IsClosed = true;
            figure.IsFilled = true;
            path.Figures.Add(figure);

            Path = path;
        }
    }
}
