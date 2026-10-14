namespace Avalonia.Inking.Core;

/// <summary>Causal, velocity-adaptive input smoothing. No predicted points are committed.</summary>
public sealed class InkStrokeBuilder
{
    private readonly List<InkPoint> _points = [];
    private InkPoint _filtered;
    public InkBrush Brush { get; }
    public double Smoothing { get; }
    public int Count => _points.Count;
    public InkStrokeBuilder(InkBrush brush, double smoothing = .4)
    {
        brush.Validate(); InkLimits.Require(double.IsFinite(smoothing) && smoothing is >= 0 and <= 1, "Invalid smoothing.");
        Brush = brush; Smoothing = smoothing;
    }
    public bool Add(InkPoint point, bool final = false)
    {
        point.Validate();
        if (_points.Count >= InkLimits.MaxPointsPerStroke) return false;
        if (_points.Count == 0) { _points.Add(point); _filtered = point; return true; }
        point = point with { Timestamp = Math.Max(point.Timestamp, _filtered.Timestamp) };
        var distance = (point.Position - _filtered.Position).Length;
        var speed = distance / Math.Max(1, point.Timestamp - _filtered.Timestamp);
        var alpha = final ? 1 : Math.Clamp(1 - Smoothing * .88 + speed * .06, .12, 1);
        var position = InkVector.Lerp(_filtered.Position, point.Position, alpha);
        var filtered = point with { X = position.X, Y = position.Y };
        if (!final && (position - _points[^1].Position).Length < .45 && Math.Abs(point.Pressure - _points[^1].Pressure) < .015) return false;
        if (filtered == _points[^1]) return false;
        _filtered = filtered; _points.Add(filtered); return true;
    }
    public InkStroke Build() => new(_points, Brush);
}
