using System;
using System.Numerics;
using Avalonia.Platform;
using ProGPU.Vector;

namespace Avalonia.ProGpu
{
    internal class TransformedGeometryImpl : GeometryImpl, ITransformedGeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public IGeometryImpl SourceGeometry { get; }

        public Matrix Transform { get; }

        public TransformedGeometryImpl(GeometryImpl source, Matrix transform)
        {
            SourceGeometry = source;
            Transform = transform;

            var transformedPath = new PathGeometry();
            foreach (var figure in source.Path.Figures)
            {
                var transFigure = new PathFigure(TransformPoint(figure.StartPoint, transform), figure.IsClosed)
                {
                    IsFilled = figure.IsFilled
                };
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                    {
                        transFigure.Segments.Add(new LineSegment(TransformPoint(line.Point, transform)));
                    }
                    else if (segment is QuadraticBezierSegment quad)
                    {
                        transFigure.Segments.Add(new QuadraticBezierSegment(
                            TransformPoint(quad.ControlPoint, transform),
                            TransformPoint(quad.Point, transform)
                        ));
                    }
                    else if (segment is CubicBezierSegment cubic)
                    {
                        transFigure.Segments.Add(new CubicBezierSegment(
                            TransformPoint(cubic.ControlPoint1, transform),
                            TransformPoint(cubic.ControlPoint2, transform),
                            TransformPoint(cubic.Point, transform)
                        ));
                    }
                }
                transformedPath.Figures.Add(transFigure);
            }

            Path = transformedPath;
        }

        private static Vector2 TransformPoint(Vector2 pt, Matrix m)
        {
            var p = new Point(pt.X, pt.Y) * m;
            return new Vector2((float)p.X, (float)p.Y);
        }
    }
}
