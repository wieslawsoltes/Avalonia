using System.Collections.Immutable;

namespace Avalonia.Inking.Core;

public static class InkLimits
{
    public const int MaxLayers = 128, MaxStrokes = 100_000, MaxPoints = 2_000_000;
    public const int MaxPointsPerStroke = 100_000, MaxJsonCharacters = 64 * 1024 * 1024;
    public const double MaxCoordinate = 10_000_000;
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
    public static bool Finite(double value) => double.IsFinite(value);
}

public readonly record struct InkVector(double X, double Y)
{
    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);
    public static InkVector operator +(InkVector a, InkVector b) => new(a.X + b.X, a.Y + b.Y);
    public static InkVector operator -(InkVector a, InkVector b) => new(a.X - b.X, a.Y - b.Y);
    public static InkVector operator *(InkVector a, double b) => new(a.X * b, a.Y * b);
    public static InkVector operator /(InkVector a, double b) => new(a.X / b, a.Y / b);
    public static double Dot(InkVector a, InkVector b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(InkVector a, InkVector b) => a.X * b.Y - a.Y * b.X;
    public static InkVector Lerp(InkVector a, InkVector b, double t) => a + (b - a) * t;
}

/// <summary>Device sample. Coordinates are document units; timestamps are monotonic milliseconds.</summary>
public readonly record struct InkPoint(double X, double Y, double Pressure = 1,
    double TiltX = 0, double TiltY = 0, double Twist = 0, double Timestamp = 0)
{
    public InkVector Position => new(X, Y);
    public void Validate()
    {
        InkLimits.Require(double.IsFinite(X) && double.IsFinite(Y) && Math.Abs(X) <= InkLimits.MaxCoordinate && Math.Abs(Y) <= InkLimits.MaxCoordinate, "Invalid ink coordinates.");
        InkLimits.Require(double.IsFinite(Pressure) && Pressure is >= 0 and <= 1, "Pressure must be between zero and one.");
        InkLimits.Require(double.IsFinite(TiltX) && double.IsFinite(TiltY) && Math.Abs(TiltX) <= 90 && Math.Abs(TiltY) <= 90, "Invalid pen tilt.");
        InkLimits.Require(double.IsFinite(Twist) && Twist is >= 0 and <= 360, "Invalid pen twist.");
        InkLimits.Require(double.IsFinite(Timestamp) && Timestamp >= 0, "Invalid timestamp.");
    }
    public static InkPoint Lerp(InkPoint a, InkPoint b, double t)
    {
        var rotation = ((b.Twist - a.Twist + 540) % 360) - 180;
        return new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
            a.Pressure + (b.Pressure - a.Pressure) * t,
            a.TiltX + (b.TiltX - a.TiltX) * t, a.TiltY + (b.TiltY - a.TiltY) * t,
            (a.Twist + rotation * t + 360) % 360, a.Timestamp + (b.Timestamp - a.Timestamp) * t);
    }
}

public readonly record struct InkRect(double X, double Y, double Width, double Height)
{
    public static InkRect Empty => new(0, 0, -1, -1);
    public bool IsEmpty => Width < 0 || Height < 0;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public InkVector Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(InkVector p) => !IsEmpty && p.X >= X && p.Y >= Y && p.X <= Right && p.Y <= Bottom;
    public bool Intersects(InkRect r) => !IsEmpty && !r.IsEmpty && r.X <= Right && r.Right >= X && r.Y <= Bottom && r.Bottom >= Y;
    public InkRect Inflate(double amount) => IsEmpty ? this : new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
    public InkRect Union(InkRect r)
    {
        if (IsEmpty) return r;
        if (r.IsEmpty) return this;
        var x = Math.Min(X, r.X); var y = Math.Min(Y, r.Y);
        return new(x, y, Math.Max(Right, r.Right) - x, Math.Max(Bottom, r.Bottom) - y);
    }
    public static InkRect Between(InkVector a, InkVector b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>Full affine transform. Stored separately so editing never destroys pressure or raw samples.</summary>
public readonly record struct InkTransform(double A, double B, double C, double D, double Tx, double Ty)
{
    public static InkTransform Identity => new(1, 0, 0, 1, 0, 0);
    public double Determinant => A * D - B * C;
    public double MaximumScale
    {
        get { var s = A * A + B * B + C * C + D * D; return Math.Sqrt((s + Math.Sqrt(Math.Max(0, s * s - 4 * Determinant * Determinant))) / 2); }
    }
    public void Validate()
    {
        InkLimits.Require(double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D) && double.IsFinite(Tx) && double.IsFinite(Ty), "Transform must be finite.");
        InkLimits.Require(Math.Abs(Determinant) >= 1e-12 && MaximumScale <= 10_000 && Math.Abs(Tx) <= InkLimits.MaxCoordinate && Math.Abs(Ty) <= InkLimits.MaxCoordinate, "Transform is singular or outside supported bounds.");
    }
    public InkVector Apply(InkVector p) => new(A * p.X + C * p.Y + Tx, B * p.X + D * p.Y + Ty);
    public InkRect Apply(InkRect r)
    {
        if (r.IsEmpty) return r;
        var a = Apply(new(r.X, r.Y)); var b = Apply(new(r.Right, r.Y));
        var c = Apply(new(r.Right, r.Bottom)); var d = Apply(new(r.X, r.Bottom));
        return InkRect.Between(a, b).Union(InkRect.Between(c, d));
    }
    public InkTransform Then(InkTransform n) => new(n.A * A + n.C * B, n.B * A + n.D * B,
        n.A * C + n.C * D, n.B * C + n.D * D, n.A * Tx + n.C * Ty + n.Tx, n.B * Tx + n.D * Ty + n.Ty);
    public InkTransform Inverse()
    {
        Validate(); var det = Determinant;
        return new(D / det, -B / det, -C / det, A / det, (C * Ty - D * Tx) / det, (B * Tx - A * Ty) / det);
    }
    public static InkTransform Translation(double x, double y) => new(1, 0, 0, 1, x, y);
    public static InkTransform Scale(double x, double y, InkVector center) => new(x, 0, 0, y, center.X * (1 - x), center.Y * (1 - y));
    public static InkTransform Rotation(double radians, InkVector center)
    {
        var c = Math.Cos(radians); var s = Math.Sin(radians);
        return new(c, s, -s, c, center.X * (1 - c) + center.Y * s, center.Y * (1 - c) - center.X * s);
    }
}

public enum InkBrushKind { Pen, Pencil, Marker, Highlighter }
public enum InkTool { Pen, Pencil, Highlighter, StrokeEraser, PointEraser, Select, Lasso, Line, Rectangle, Ellipse, Pan }
public enum InkGrid { None, Dots, Lines, Ruled }

public sealed record InkBrush
{
    public InkBrushKind Kind { get; init; } = InkBrushKind.Pen;
    public uint Color { get; init; } = 0xFF4657D5;
    public double Width { get; init; } = 3;
    public double Opacity { get; init; } = 1;
    public bool UsePressure { get; init; } = true;
    public double MinimumPressureWidth { get; init; } = .18;
    public double PressureCurve { get; init; } = .7;
    public double NibAspect { get; init; } = 1;
    public double NibAngle { get; init; }
    public double TiltInfluence { get; init; }
    public double MaximumRadius => Width * .5 * (1 + TiltInfluence);
    public void Validate()
    {
        InkLimits.Require(Enum.IsDefined(Kind), "Unknown brush kind.");
        InkLimits.Require(double.IsFinite(Width) && Width is >= .1 and <= 512, "Brush width must be .1–512.");
        InkLimits.Require(double.IsFinite(Opacity) && Opacity is >= 0 and <= 1, "Invalid opacity.");
        InkLimits.Require(double.IsFinite(MinimumPressureWidth) && MinimumPressureWidth is >= .01 and <= 1, "Invalid minimum pressure width.");
        InkLimits.Require(double.IsFinite(PressureCurve) && PressureCurve is >= .1 and <= 4, "Invalid pressure curve.");
        InkLimits.Require(double.IsFinite(NibAspect) && NibAspect is >= .1 and <= 1 && double.IsFinite(NibAngle), "Invalid nib.");
        InkLimits.Require(double.IsFinite(TiltInfluence) && TiltInfluence is >= 0 and <= 2, "Invalid tilt influence.");
    }
    public InkNib GetNib(InkPoint point)
    {
        var pressure = UsePressure ? MinimumPressureWidth + (1 - MinimumPressureWidth) * Math.Pow(point.Pressure, PressureCurve) : 1;
        var tilt = Math.Min(1, Math.Sqrt(point.TiltX * point.TiltX + point.TiltY * point.TiltY) / 90);
        var r = Width * .5 * pressure;
        var angle = (NibAngle + point.Twist) * Math.PI / 180;
        if (TiltInfluence > 0 && tilt > .01) angle += Math.Atan2(point.TiltY, point.TiltX);
        return new(r * (1 + tilt * TiltInfluence), r * NibAspect, angle);
    }
}

public readonly record struct InkNib(double RadiusX, double RadiusY, double Angle)
{
    public InkVector Map(InkVector p)
    {
        var x = p.X * RadiusX; var y = p.Y * RadiusY; var c = Math.Cos(Angle); var s = Math.Sin(Angle);
        return new(x * c - y * s, x * s + y * c);
    }
    public InkVector Support(InkVector direction)
    {
        var c = Math.Cos(Angle); var s = Math.Sin(Angle);
        var x = direction.X * c + direction.Y * s; var y = -direction.X * s + direction.Y * c;
        var length = Math.Sqrt(x * x * RadiusX * RadiusX + y * y * RadiusY * RadiusY);
        return length < 1e-12 ? default : Map(new(x * RadiusX / length, y * RadiusY / length));
    }
}

public static class InkPresets
{
    public static InkBrush Pen { get; } = new();
    public static InkBrush Pencil { get; } = new() { Kind = InkBrushKind.Pencil, Color = 0xFF354052, Width = 2.5, Opacity = .82, PressureCurve = 1.25, NibAspect = .7, TiltInfluence = 1 };
    public static InkBrush Marker { get; } = new() { Kind = InkBrushKind.Marker, Width = 9, NibAspect = .65 };
    public static InkBrush Highlighter { get; } = new() { Kind = InkBrushKind.Highlighter, Color = 0xFFFFC857, Width = 24, Opacity = .38, UsePressure = false, NibAspect = .8 };
    public static ImmutableArray<uint> Palette { get; } = [0xFF293449, 0xFF4657D5, 0xFF9361CE, 0xFFDA5C72, 0xFFF09244, 0xFFFFC857, 0xFF279B87, 0xFF4AA6C2];
}
