using System.Collections.Immutable;

namespace Avalonia.Inking.Core;

/// <summary>An immutable stroke. Raw samples and their device channels survive all affine editing.</summary>
public sealed class InkStroke
{
    public Guid Id { get; }
    public InkBrush Brush { get; }
    public ImmutableArray<InkPoint> Points { get; }
    public InkTransform Transform { get; }
    public InkRect LocalBounds { get; }
    public InkRect Bounds { get; }
    public InkStroke(IEnumerable<InkPoint> points, InkBrush? brush = null, Guid? id = null, InkTransform? transform = null)
        : this(points.ToImmutableArray(), brush ?? InkPresets.Pen, id ?? Guid.NewGuid(), transform ?? InkTransform.Identity, false) { }
    private InkStroke(ImmutableArray<InkPoint> points, InkBrush brush, Guid id, InkTransform transform, bool trusted)
    {
        InkLimits.Require(id != Guid.Empty, "Stroke ID cannot be empty.");
        InkLimits.Require(!points.IsDefaultOrEmpty && points.Length <= InkLimits.MaxPointsPerStroke, "Invalid stroke sample count.");
        brush.Validate(); transform.Validate();
        var bounds = InkRect.Empty;
        foreach (var p in points)
        {
            if (!trusted) p.Validate();
            bounds = bounds.Union(new(p.X, p.Y, 0, 0));
        }
        Id = id; Brush = brush; Points = points; Transform = transform;
        LocalBounds = bounds.Inflate(brush.MaximumRadius); Bounds = transform.Apply(LocalBounds);
    }
    public InkVector WorldPoint(int index) => Transform.Apply(Points[index].Position);
    public double WorldRadius(int index) => Brush.GetNib(Points[index]).RadiusX * Transform.MaximumScale;
    public InkStroke Transformed(InkTransform transform) => new(Points, Brush, Id, Transform.Then(transform), true);
    public InkStroke Restyled(InkBrush brush) => new(Points, brush, Id, Transform, true);
    public InkStroke Clone(InkTransform? transform = null) => new(Points, Brush, Guid.NewGuid(), Transform.Then(transform ?? InkTransform.Identity), true);
    internal InkStroke Fragment(IEnumerable<InkPoint> points) => new(points.ToImmutableArray(), Brush, Guid.NewGuid(), Transform, true);
}

public sealed class InkLayer
{
    public Guid Id { get; }
    public string Name { get; }
    public bool IsVisible { get; }
    public bool IsLocked { get; }
    public double Opacity { get; }
    public ImmutableArray<InkStroke> Strokes { get; }
    public int PointCount { get; }
    public InkLayer(string name = "Ink layer", IEnumerable<InkStroke>? strokes = null, Guid? id = null,
        bool isVisible = true, bool isLocked = false, double opacity = 1)
    {
        InkLimits.Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 256, "Layer name must have 1–256 characters.");
        InkLimits.Require(double.IsFinite(opacity) && opacity is >= 0 and <= 1, "Invalid layer opacity.");
        Id = id ?? Guid.NewGuid(); InkLimits.Require(Id != Guid.Empty, "Layer ID cannot be empty.");
        Name = name; IsVisible = isVisible; IsLocked = isLocked; Opacity = opacity;
        Strokes = strokes?.ToImmutableArray() ?? [];
        InkLimits.Require(Strokes.Length <= InkLimits.MaxStrokes && Strokes.All(s => s is not null), "Invalid strokes.");
        PointCount = checked(Strokes.Sum(s => s.Points.Length));
    }
    public InkLayer With(string? name = null, bool? visible = null, bool? locked = null, double? opacity = null, IEnumerable<InkStroke>? strokes = null)
        => new(name ?? Name, strokes ?? Strokes, Id, visible ?? IsVisible, locked ?? IsLocked, opacity ?? Opacity);
}

/// <summary>Validated immutable snapshot. Layers and strokes are ordered from back to front.</summary>
public sealed class InkDocument
{
    public string Title { get; }
    public ImmutableArray<InkLayer> Layers { get; }
    public int StrokeCount { get; }
    public int PointCount { get; }
    public InkRect ContentBounds { get; }
    public IEnumerable<InkStroke> AllStrokes => Layers.SelectMany(l => l.Strokes);
    public IEnumerable<InkStroke> VisibleStrokes => Layers.Where(l => l.IsVisible && l.Opacity > 0).SelectMany(l => l.Strokes);
    public InkDocument(string title = "Untitled ink", IEnumerable<InkLayer>? layers = null)
    {
        InkLimits.Require(!string.IsNullOrWhiteSpace(title) && title.Length <= 512, "Invalid document title.");
        Title = title; Layers = layers?.ToImmutableArray() ?? [new InkLayer()];
        InkLimits.Require(Layers.Length is > 0 and <= InkLimits.MaxLayers && Layers.All(l => l is not null), "Document must contain 1–128 layers.");
        InkLimits.Require(Layers.Select(l => l.Id).Distinct().Count() == Layers.Length, "Duplicate layer ID.");
        var ids = new HashSet<Guid>(); var bounds = InkRect.Empty;
        foreach (var layer in Layers)
        {
            foreach (var stroke in layer.Strokes)
            {
                InkLimits.Require(ids.Add(stroke.Id), "Duplicate stroke ID.");
                StrokeCount++; PointCount = checked(PointCount + stroke.Points.Length);
                if (layer.IsVisible && layer.Opacity > 0) bounds = bounds.Union(stroke.Bounds);
            }
        }
        InkLimits.Require(StrokeCount <= InkLimits.MaxStrokes && PointCount <= InkLimits.MaxPoints, "Document exceeds resource limits.");
        ContentBounds = bounds;
    }
    public InkDocument WithLayers(IEnumerable<InkLayer> layers) => new(Title, layers);
    public InkDocument WithTitle(string title) => new(title, Layers);
}
