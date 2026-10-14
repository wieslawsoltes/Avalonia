namespace Avalonia.Inking.Core;

/// <summary>Backend-independent, non-zero filled path sink. A stroke must be filled exactly once.</summary>
public interface IInkPathSink
{
    void Begin(InkVector p);
    void Line(InkVector p);
    void Cubic(InkVector a, InkVector b, InkVector end);
    void Close();
}

public static class InkGeometry
{
    /// <summary>Emits elliptical nib footprints and their connecting strips with consistent winding.</summary>
    public static void EmitStroke(InkStroke stroke, IInkPathSink sink)
    {
        const double k = .5522847498307936;
        var points = stroke.Points;
        for (var i = 0; i < points.Length; i++)
        {
            var sample = points[i]; var nib = stroke.Brush.GetNib(sample);
            InkVector P(double x, double y) => stroke.Transform.Apply(sample.Position + nib.Map(new(x, y)));
            sink.Begin(P(1, 0));
            sink.Cubic(P(1, k), P(k, 1), P(0, 1));
            sink.Cubic(P(-k, 1), P(-1, k), P(-1, 0));
            sink.Cubic(P(-1, -k), P(-k, -1), P(0, -1));
            sink.Cubic(P(k, -1), P(1, -k), P(1, 0));
            sink.Close();
            if (i == 0) continue;
            var previous = points[i - 1]; var direction = sample.Position - previous.Position;
            if (direction.LengthSquared < 1e-14) continue;
            var normal = new InkVector(-direction.Y, direction.X) / direction.Length;
            var a = stroke.Brush.GetNib(previous).Support(normal); var b = nib.Support(normal);
            sink.Begin(stroke.Transform.Apply(previous.Position - a));
            sink.Line(stroke.Transform.Apply(sample.Position - b));
            sink.Line(stroke.Transform.Apply(sample.Position + b));
            sink.Line(stroke.Transform.Apply(previous.Position + a));
            sink.Close();
        }
    }

    public static double DistanceToSegment(InkVector p, InkVector a, InkVector b)
    {
        var d = b - a; var den = d.LengthSquared;
        return (p - (a + d * (den < 1e-20 ? 0 : Math.Clamp(InkVector.Dot(p - a, d) / den, 0, 1)))).Length;
    }
    public static bool SegmentsIntersect(InkVector a, InkVector b, InkVector c, InkVector d)
    {
        var r = b - a; var s = d - c; var den = InkVector.Cross(r, s);
        if (Math.Abs(den) < 1e-12)
            return DistanceToSegment(a, c, d) < 1e-9 || DistanceToSegment(b, c, d) < 1e-9 || DistanceToSegment(c, a, b) < 1e-9 || DistanceToSegment(d, a, b) < 1e-9;
        var t = InkVector.Cross(c - a, s) / den; var u = InkVector.Cross(c - a, r) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }
    public static double SegmentDistance(InkVector a, InkVector b, InkVector c, InkVector d)
        => SegmentsIntersect(a, b, c, d) ? 0 : Math.Min(Math.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)), Math.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
    public static bool HitTest(InkStroke stroke, InkVector point, double radius = 0) => HitSweep(stroke, point, point, radius);
    public static bool HitSweep(InkStroke stroke, InkVector from, InkVector to, double radius)
    {
        if (!stroke.Bounds.Inflate(radius).Intersects(InkRect.Between(from, to))) return false;
        if (stroke.Points.Length == 1) return DistanceToSegment(stroke.WorldPoint(0), from, to) <= radius + stroke.WorldRadius(0);
        for (var i = 1; i < stroke.Points.Length; i++)
            if (SegmentDistance(stroke.WorldPoint(i - 1), stroke.WorldPoint(i), from, to) <= radius + Math.Max(stroke.WorldRadius(i - 1), stroke.WorldRadius(i))) return true;
        return false;
    }
    public static bool PointInPolygon(InkVector point, IReadOnlyList<InkVector> polygon)
    {
        if (polygon.Count < 3) return false;
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if (DistanceToSegment(point, a, b) < 1e-8) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    public static bool IntersectsPolygon(InkStroke stroke, IReadOnlyList<InkVector> polygon)
    {
        if (polygon.Count < 3) return false;
        for (var i = 0; i < stroke.Points.Length; i++)
        {
            var p = stroke.WorldPoint(i);
            if (PointInPolygon(p, polygon)) return true;
            for (var j = 0; j < polygon.Count; j++)
            {
                var a = polygon[j]; var b = polygon[(j + 1) % polygon.Count];
                if (DistanceToSegment(p, a, b) <= stroke.WorldRadius(i)) return true;
                if (i > 0 && SegmentDistance(stroke.WorldPoint(i - 1), p, a, b) <= Math.Max(stroke.WorldRadius(i - 1), stroke.WorldRadius(i))) return true;
            }
        }
        return false;
    }

    // A line/capsule intersection is convex. The union of two end circles and the connecting strip
    // therefore has a single parameter interval. Unlike point sampling, this cannot miss fast eraser drags.
    private static (double Lo, double Hi)? CapsuleInterval(InkVector p, InkVector q, InkVector a, InkVector b, double radius)
    {
        var intervals = new List<(double Lo, double Hi)>(3); var d = q - p;
        void Circle(InkVector center)
        {
            var v = p - center; var aa = d.LengthSquared; var cc = v.LengthSquared - radius * radius;
            if (aa < 1e-20) { if (cc <= 0) intervals.Add((0, 1)); return; }
            var bb = 2 * InkVector.Dot(v, d); var disc = bb * bb - 4 * aa * cc;
            if (disc < 0) return;
            var root = Math.Sqrt(Math.Max(0, disc));
            var lo = Math.Max(0, (-bb - root) / (2 * aa)); var hi = Math.Min(1, (-bb + root) / (2 * aa));
            if (hi >= lo) intervals.Add((lo, hi));
        }
        Circle(a); Circle(b);
        var axis = b - a; var length = axis.Length;
        if (length > 1e-12)
        {
            var u = axis / length; var n = new InkVector(-u.Y, u.X);
            double lo = 0, hi = 1;
            bool Clip(double value, double delta, double min, double max)
            {
                if (Math.Abs(delta) < 1e-15) return value >= min && value <= max;
                var t0 = (min - value) / delta; var t1 = (max - value) / delta;
                lo = Math.Max(lo, Math.Min(t0, t1)); hi = Math.Min(hi, Math.Max(t0, t1));
                return hi >= lo;
            }
            var relative = p - a;
            if (Clip(InkVector.Dot(relative, u), InkVector.Dot(d, u), 0, length) && Clip(InkVector.Dot(relative, n), InkVector.Dot(d, n), -radius, radius)) intervals.Add((lo, hi));
        }
        if (intervals.Count == 0) return null;
        return (intervals.Min(x => x.Lo), intervals.Max(x => x.Hi));
    }

    /// <summary>Continuous partial eraser, conservatively expanded by the maximum transformed nib radius per segment.</summary>
    public static IReadOnlyList<InkStroke> Erase(InkStroke stroke, InkVector from, InkVector to, double radius)
    {
        InkLimits.Require(double.IsFinite(radius) && radius >= 0, "Invalid eraser radius.");
        if (!HitSweep(stroke, from, to, radius)) return [stroke];
        if (stroke.Points.Length == 1) return [];
        var pieces = new List<InkStroke>(); var current = new List<InkPoint>(); var changed = false;
        void Add(InkPoint point) { if (current.Count == 0 || current[^1] != point) current.Add(point); }
        void Flush() { if (current.Count > 0) { pieces.Add(stroke.Fragment(current)); current.Clear(); } }
        for (var i = 1; i < stroke.Points.Length; i++)
        {
            var a = stroke.Points[i - 1]; var b = stroke.Points[i];
            var interval = CapsuleInterval(stroke.WorldPoint(i - 1), stroke.WorldPoint(i), from, to, radius + Math.Max(stroke.WorldRadius(i - 1), stroke.WorldRadius(i)));
            if (interval is not { } cut || cut.Hi - cut.Lo < 1e-10) { Add(a); Add(b); continue; }
            changed = true;
            if (cut.Lo > 1e-10) { Add(a); Add(InkPoint.Lerp(a, b, cut.Lo)); }
            Flush();
            if (cut.Hi < 1 - 1e-10) { Add(InkPoint.Lerp(a, b, cut.Hi)); Add(b); }
        }
        if (!changed) return [stroke];
        Flush(); return pieces;
    }

    public static InkStroke Shape(InkTool tool, InkVector from, InkVector to, InkBrush brush, bool constrain = false)
    {
        var delta = to - from;
        if (tool == InkTool.Line)
        {
            if (constrain)
            {
                var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
                to = from + new InkVector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
            }
            return new([new InkPoint(from.X, from.Y), new InkPoint(to.X, to.Y)], brush);
        }
        if (constrain)
        {
            var size = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            to = from + new InkVector(Math.CopySign(size, delta.X), Math.CopySign(size, delta.Y));
        }
        var r = InkRect.Between(from, to);
        if (tool == InkTool.Rectangle)
            return new([new InkPoint(r.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom), new(r.X, r.Y)], brush);
        if (tool != InkTool.Ellipse) throw new ArgumentException("Not a shape tool.");
        var count = (int)Math.Clamp(Math.Ceiling(Math.PI * Math.Max(r.Width, r.Height) / 4), 32, 512);
        return new(Enumerable.Range(0, count + 1).Select(i => new InkPoint(r.Center.X + r.Width / 2 * Math.Cos(i * 2 * Math.PI / count), r.Center.Y + r.Height / 2 * Math.Sin(i * 2 * Math.PI / count))), brush);
    }
}
