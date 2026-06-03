using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Platform;
using ProGPU.Vector;
using PathGeometry = ProGPU.Vector.PathGeometry;
using PathSegment = ProGPU.Vector.PathSegment;
using LineSegment = ProGPU.Vector.LineSegment;
using QuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using CubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using PathFigure = ProGPU.Vector.PathFigure;

namespace Avalonia.ProGpu
{
    internal abstract class GeometryImpl : IGeometryImpl
    {
        public abstract ProGPU.Vector.PathGeometry Path { get; }

        protected void InvalidateCaches()
        {
        }

        public Rect Bounds => CalculateBounds(Path);

        public double ContourLength => CalculateLength(Path);

        public bool FillContains(Point point)
        {
            return PathContains(Path, point, FillRule.EvenOdd);
        }

        public bool StrokeContains(IPen? pen, Point point)
        {
            double threshold = (pen?.Thickness ?? 1.0) / 2.0;
            return DistanceToPath(Path, point) <= threshold;
        }

        public IGeometryImpl? Intersect(IGeometryImpl geometry)
        {
            return this;
        }

        public Rect GetRenderBounds(IPen? pen)
        {
            var bounds = Bounds;
            if (pen != null)
            {
                bounds = bounds.Inflate(pen.Thickness / 2.0);
            }
            return bounds;
        }

        public IGeometryImpl GetWidenedGeometry(IPen pen)
        {
            return this;
        }

        public ITransformedGeometryImpl WithTransform(Matrix transform)
        {
            return new TransformedGeometryImpl(this, transform);
        }

        public bool TryGetPointAtDistance(double distance, out Point point)
        {
            point = new Point();
            double accum = 0;
            foreach (var figure in Path.Figures)
            {
                var curr = figure.StartPoint;
                foreach (var seg in figure.Segments)
                {
                    var pts = FlattenSegment(curr, seg);
                    foreach (var pt in pts)
                    {
                        double len = (pt - curr).Length();
                        if (accum + len >= distance)
                        {
                            double ratio = (distance - accum) / len;
                            var interp = curr + (float)ratio * (pt - curr);
                            point = new Point(interp.X, interp.Y);
                            return true;
                        }
                        accum += len;
                        curr = pt;
                    }
                }
            }
            return false;
        }

        public bool TryGetPointAndTangentAtDistance(double distance, out Point point, out Point tangent)
        {
            point = new Point();
            tangent = new Point(1, 0);
            double accum = 0;
            foreach (var figure in Path.Figures)
            {
                var curr = figure.StartPoint;
                foreach (var seg in figure.Segments)
                {
                    var pts = FlattenSegment(curr, seg);
                    foreach (var pt in pts)
                    {
                        double len = (pt - curr).Length();
                        if (accum + len >= distance)
                        {
                            double ratio = (distance - accum) / len;
                            var interp = curr + (float)ratio * (pt - curr);
                            point = new Point(interp.X, interp.Y);
                            var dir = Vector2.Normalize(pt - curr);
                            tangent = new Point(dir.X, dir.Y);
                            return true;
                        }
                        accum += len;
                        curr = pt;
                    }
                }
            }
            return false;
        }

        public bool TryGetSegment(double startDistance, double stopDistance, bool startOnBeginFigure,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IGeometryImpl? segmentGeometry)
        {
            segmentGeometry = this;
            return true;
        }

        public static Avalonia.Rect CalculateBounds(ProGPU.Vector.PathGeometry path)
        {
            if (path.Figures.Count == 0) return new Avalonia.Rect();

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasPoints = false;

            foreach (var figure in path.Figures)
            {
                minX = Math.Min(minX, figure.StartPoint.X);
                minY = Math.Min(minY, figure.StartPoint.Y);
                maxX = Math.Max(maxX, figure.StartPoint.X);
                maxY = Math.Max(maxY, figure.StartPoint.Y);
                hasPoints = true;

                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                    {
                        minX = Math.Min(minX, line.Point.X);
                        minY = Math.Min(minY, line.Point.Y);
                        maxX = Math.Max(maxX, line.Point.X);
                        maxY = Math.Max(maxY, line.Point.Y);
                    }
                    else if (segment is QuadraticBezierSegment quad)
                    {
                        minX = Math.Min(minX, quad.ControlPoint.X);
                        minY = Math.Min(minY, quad.ControlPoint.Y);
                        maxX = Math.Max(maxX, quad.ControlPoint.X);
                        maxY = Math.Max(maxY, quad.ControlPoint.Y);
                        minX = Math.Min(minX, quad.Point.X);
                        minY = Math.Min(minY, quad.Point.Y);
                        maxX = Math.Max(maxX, quad.Point.X);
                        maxY = Math.Max(maxY, quad.Point.Y);
                    }
                    else if (segment is CubicBezierSegment cubic)
                    {
                        minX = Math.Min(minX, cubic.ControlPoint1.X);
                        minY = Math.Min(minY, cubic.ControlPoint1.Y);
                        maxX = Math.Max(maxX, cubic.ControlPoint1.X);
                        maxY = Math.Max(maxY, cubic.ControlPoint1.Y);
                        minX = Math.Min(minX, cubic.ControlPoint2.X);
                        minY = Math.Min(minY, cubic.ControlPoint2.Y);
                        maxX = Math.Max(maxX, cubic.ControlPoint2.X);
                        maxY = Math.Max(maxY, cubic.ControlPoint2.Y);
                        minX = Math.Min(minX, cubic.Point.X);
                        minY = Math.Min(minY, cubic.Point.Y);
                        maxX = Math.Max(maxX, cubic.Point.X);
                        maxY = Math.Max(maxY, cubic.Point.Y);
                    }
                }
            }

            if (!hasPoints) return new Avalonia.Rect();
            return new Avalonia.Rect(minX, minY, maxX - minX, maxY - minY);
        }

        public static double CalculateLength(ProGPU.Vector.PathGeometry path)
        {
            double length = 0;
            foreach (var figure in path.Figures)
            {
                var curr = figure.StartPoint;
                foreach (var seg in figure.Segments)
                {
                    var pts = FlattenSegment(curr, seg);
                    foreach (var pt in pts)
                    {
                        length += (pt - curr).Length();
                        curr = pt;
                    }
                }
            }
            return length;
        }

        private static List<Vector2> FlattenSegment(Vector2 start, ProGPU.Vector.PathSegment segment)
        {
            var list = new List<Vector2>();
            if (segment is LineSegment line)
            {
                list.Add(line.Point);
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f;
                    float u = 1 - t;
                    var pt = u * u * start + 2 * u * t * quad.ControlPoint + t * t * quad.Point;
                    list.Add(pt);
                }
            }
            else if (segment is CubicBezierSegment cubic)
            {
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f;
                    float u = 1 - t;
                    var pt = u * u * u * start + 3 * u * u * t * cubic.ControlPoint1 + 3 * u * t * t * cubic.ControlPoint2 + t * t * t * cubic.Point;
                    list.Add(pt);
                }
            }
            return list;
        }

        public static bool PathContains(ProGPU.Vector.PathGeometry path, Point point, FillRule fillRule)
        {
            int windingNumber = 0;
            int crossCount = 0;
            float px = (float)point.X;
            float py = (float)point.Y;

            foreach (var figure in path.Figures)
            {
                var curr = figure.StartPoint;
                var figureLines = new List<(Vector2 A, Vector2 B)>();

                foreach (var seg in figure.Segments)
                {
                    var pts = FlattenSegment(curr, seg);
                    foreach (var pt in pts)
                    {
                        figureLines.Add((curr, pt));
                        curr = pt;
                    }
                }

                if (figure.IsClosed && curr != figure.StartPoint)
                {
                    figureLines.Add((curr, figure.StartPoint));
                }

                foreach (var line in figureLines)
                {
                    var a = line.A;
                    var b = line.B;

                    bool upward = (a.Y <= py && b.Y > py);
                    bool downward = (a.Y > py && b.Y <= py);

                    if (upward || downward)
                    {
                        float t = (py - a.Y) / (b.Y - a.Y);
                        float xIntersect = a.X + t * (b.X - a.X);

                        if (px < xIntersect)
                        {
                            crossCount++;
                            if (upward) windingNumber++;
                            else windingNumber--;
                        }
                    }
                }
            }

            if (fillRule == FillRule.EvenOdd)
            {
                return (crossCount % 2) != 0;
            }
            else
            {
                return windingNumber != 0;
            }
        }

        private static double DistanceToPath(ProGPU.Vector.PathGeometry path, Point point)
        {
            double minDistance = double.MaxValue;
            Vector2 p = new Vector2((float)point.X, (float)point.Y);

            foreach (var figure in path.Figures)
            {
                var curr = figure.StartPoint;
                var figureLines = new List<(Vector2 A, Vector2 B)>();

                foreach (var seg in figure.Segments)
                {
                    var pts = FlattenSegment(curr, seg);
                    foreach (var pt in pts)
                    {
                        figureLines.Add((curr, pt));
                        curr = pt;
                    }
                }

                if (figure.IsClosed && curr != figure.StartPoint)
                {
                    figureLines.Add((curr, figure.StartPoint));
                }

                foreach (var line in figureLines)
                {
                    var a = line.A;
                    var b = line.B;

                    float l2 = Vector2.DistanceSquared(a, b);
                    float t = 0;
                    if (l2 > 0)
                    {
                        t = Math.Max(0, Math.Min(1, Vector2.Dot(p - a, b - a) / l2));
                    }
                    var projection = a + t * (b - a);
                    double dist = Vector2.Distance(p, projection);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                    }
                }
            }

            return minDistance;
        }
    }
}
