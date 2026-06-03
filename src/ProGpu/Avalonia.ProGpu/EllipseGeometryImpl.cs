using System.Numerics;
using ProGPU.Vector;

namespace Avalonia.ProGpu
{
    internal class EllipseGeometryImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public EllipseGeometryImpl(Rect rect)
        {
            var path = new PathGeometry();
            var figure = new PathFigure();

            float rx = (float)rect.Width / 2f;
            float ry = (float)rect.Height / 2f;
            float cx = (float)rect.X + rx;
            float cy = (float)rect.Y + ry;

            float k = 0.55228475f; // Bezier kappa for circles

            figure.StartPoint = new Vector2(cx + rx, cy);
            
            // Top-right to top-left
            figure.Segments.Add(new CubicBezierSegment(
                new Vector2(cx + rx, cy - ry * k),
                new Vector2(cx + rx * k, cy - ry),
                new Vector2(cx, cy - ry)
            ));

            // Top-left to bottom-left
            figure.Segments.Add(new CubicBezierSegment(
                new Vector2(cx - rx * k, cy - ry),
                new Vector2(cx - rx, cy - ry * k),
                new Vector2(cx - rx, cy)
            ));

            // Bottom-left to bottom-right
            figure.Segments.Add(new CubicBezierSegment(
                new Vector2(cx - rx, cy + ry * k),
                new Vector2(cx - rx * k, cy + ry),
                new Vector2(cx, cy + ry)
            ));

            // Bottom-right to top-right
            figure.Segments.Add(new CubicBezierSegment(
                new Vector2(cx + rx * k, cy + ry),
                new Vector2(cx + rx, cy + ry * k),
                new Vector2(cx + rx, cy)
            ));

            figure.IsClosed = true;
            figure.IsFilled = true;
            path.Figures.Add(figure);

            Path = path;
        }
    }
}
