using Avalonia.Controls;
using Avalonia.Inking.Core;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Avalonia.Inking;

internal static class InkDrawing
{
    public static Point Point(InkVector p) => new(p.X, p.Y);
    public static Rect Rect(InkRect r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    public static StreamGeometry Geometry(InkStroke stroke)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(FillRule.NonZero); InkGeometry.EmitStroke(stroke, new Sink(context));
        }
        return geometry;
    }
    public static IBrush Brush(InkBrush brush) => new SolidColorBrush(Color.FromUInt32(brush.Color), brush.Opacity);
    public static Matrix ViewMatrix(InkCanvas canvas) => new(canvas.Zoom, 0, 0, canvas.Zoom, canvas.PanOffset.X, canvas.PanOffset.Y);
    public static void Draw(DrawingContext context, InkStroke stroke) => context.DrawGeometry(Brush(stroke.Brush), null, Geometry(stroke));
    private sealed class Sink(StreamGeometryContext context) : IInkPathSink
    {
        public void Begin(InkVector p) => context.BeginFigure(Point(p));
        public void Line(InkVector p) => context.LineTo(Point(p));
        public void Cubic(InkVector a, InkVector b, InkVector end) => context.CubicBezierTo(Point(a), Point(b), Point(end));
        public void Close() => context.EndFigure(true);
    }
}

/// <summary>Replaceable retained-ink presenter with viewport culling and bounded geometry caching.</summary>
public class InkPresenter : Control
{
    private sealed record Cached(StreamGeometry Geometry, IBrush Brush, int Points, LinkedListNode<InkStroke> Node);
    private readonly Dictionary<InkStroke, Cached> _cache = [];
    private readonly LinkedList<InkStroke> _lru = [];
    private int _cachedPoints;
    internal InkCanvas? Owner { get; set; }
    public int MaximumCachedStrokes { get; set; } = 1200;
    public int MaximumCachedPoints { get; set; } = 200_000;
    public int CachedStrokeCount => _cache.Count;
    public int CachedPointCount => _cachedPoints;
    public long CacheHits { get; private set; }
    public long CacheMisses { get; private set; }
    public int LastVisibleStrokeCount { get; private set; }
    public void ClearCache() { _cache.Clear(); _lru.Clear(); _cachedPoints = 0; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var canvas = Owner; if (canvas is null) return;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        DrawGrid(context, canvas);
        var topLeft = canvas.ViewToDocument(default); var bottomRight = canvas.ViewToDocument(new(Bounds.Width, Bounds.Height));
        var visible = canvas.Session.Index.Query(InkRect.Between(topLeft, bottomRight).Inflate(2 / canvas.Zoom));
        LastVisibleStrokeCount = visible.Count;
        using var transform = context.PushTransform(InkDrawing.ViewMatrix(canvas));
        Guid layer = Guid.Empty; DrawingContext.PushedState opacity = default; var hasOpacity = false;
        try
        {
            foreach (var item in visible)
            {
                if (canvas.HasTransformPreview && canvas.Session.Selection.Contains(item.Stroke.Id)) continue;
                if (item.Layer.Id != layer)
                {
                    if (hasOpacity) opacity.Dispose();
                    opacity = context.PushOpacity(item.Layer.Opacity); hasOpacity = true; layer = item.Layer.Id;
                }
                var cached = Get(item.Stroke); context.DrawGeometry(cached.Brush, null, cached.Geometry);
            }
        }
        finally { if (hasOpacity) opacity.Dispose(); }
    }
    private Cached Get(InkStroke stroke)
    {
        if (_cache.TryGetValue(stroke, out var item))
        {
            CacheHits++; _lru.Remove(item.Node); _lru.AddLast(item.Node); return item;
        }
        CacheMisses++;
        var geometry = InkDrawing.Geometry(stroke); var brush = InkDrawing.Brush(stroke.Brush);
        var node = new LinkedListNode<InkStroke>(stroke); item = new(geometry, brush, stroke.Points.Length, node);
        if (MaximumCachedStrokes <= 0 || stroke.Points.Length > MaximumCachedPoints) return item;
        while (_cache.Count > 0 && (_cache.Count >= MaximumCachedStrokes || _cachedPoints + item.Points > MaximumCachedPoints))
        {
            var first = _lru.First!; _cachedPoints -= _cache[first.Value].Points; _cache.Remove(first.Value); _lru.RemoveFirst();
        }
        _cache.Add(stroke, item); _lru.AddLast(node); _cachedPoints += item.Points; return item;
    }
    private void DrawGrid(DrawingContext context, InkCanvas canvas)
    {
        if (canvas.GridMode == InkGrid.None || canvas.GridBrush is null) return;
        var spacing = canvas.GridSpacing * canvas.Zoom;
        while (spacing < 12) spacing *= 2;
        var x0 = ((canvas.PanOffset.X % spacing) + spacing) % spacing;
        var y0 = ((canvas.PanOffset.Y % spacing) + spacing) % spacing;
        var pen = new Pen(canvas.GridBrush, .7);
        if (canvas.GridMode == InkGrid.Dots)
        {
            for (var x = x0; x < Bounds.Width; x += spacing) for (var y = y0; y < Bounds.Height; y += spacing)
                context.DrawEllipse(canvas.GridBrush, null, new Point(x, y), .8, .8);
        }
        else
        {
            for (var y = y0; y < Bounds.Height; y += spacing) context.DrawLine(pen, new(0, y), new(Bounds.Width, y));
            if (canvas.GridMode == InkGrid.Lines) for (var x = x0; x < Bounds.Width; x += spacing) context.DrawLine(pen, new(x, 0), new(x, Bounds.Height));
        }
    }
}

/// <summary>Replaceable live-ink, selection, handles, ruler and eraser overlay.</summary>
public class InkAdornerPresenter : Control
{
    internal InkCanvas? Owner { get; set; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var canvas = Owner; if (canvas is null) return;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using (context.PushTransform(InkDrawing.ViewMatrix(canvas)))
        {
            if (canvas.LiveStroke is { } stroke)
                using (context.PushOpacity(canvas.Session.ActiveLayer.Opacity)) InkDrawing.Draw(context, stroke);
            if (canvas.HasTransformPreview)
                foreach (var layer in canvas.Session.Document.Layers)
                {
                    using var opacity = context.PushOpacity(layer.Opacity);
                    foreach (var item in layer.Strokes.Where(s => canvas.Session.Selection.Contains(s.Id))) InkDrawing.Draw(context, item.Transformed(canvas.PreviewTransform));
                }
        }
        var selectionPen = new Pen(canvas.SelectionBrush, 1.2, new DashStyle([4, 3], 0));
        if (!canvas.BoxPreview.IsEmpty)
        {
            var a = canvas.DocumentToView(new(canvas.BoxPreview.X, canvas.BoxPreview.Y)); var b = canvas.DocumentToView(new(canvas.BoxPreview.Right, canvas.BoxPreview.Bottom));
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 70, 87, 213)), selectionPen, new Rect(a, b));
        }
        if (canvas.LassoPreview.Count > 1)
        {
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                g.BeginFigure(canvas.DocumentToView(canvas.LassoPreview[0]));
                foreach (var p in canvas.LassoPreview.Skip(1)) g.LineTo(canvas.DocumentToView(p));
                g.EndFigure(false);
            }
            context.DrawGeometry(null, selectionPen, path);
        }
        if (canvas.ShowSelection && canvas.Session.Selection.Count > 0)
        {
            var handles = canvas.GetSelectionHandles();
            if (handles.Length == 9)
            {
                context.DrawRectangle(null, selectionPen, new Rect(handles[0], handles[4]));
                if (!canvas.EffectiveReadOnly)
                {
                    context.DrawLine(new Pen(canvas.SelectionBrush, 1), handles[1], handles[8]);
                    foreach (var p in handles.Take(8)) context.DrawRectangle(Brushes.White, new Pen(canvas.SelectionBrush, 1.3), new Rect(p.X - 4, p.Y - 4, 8, 8), 2, 2);
                    context.DrawEllipse(Brushes.White, new Pen(canvas.SelectionBrush, 1.3), handles[8], 5, 5);
                }
            }
        }
        if (canvas.RulerEnabled)
        {
            var angle = canvas.RulerAngle * Math.PI / 180; var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var origin = canvas.DocumentToView(new(canvas.RulerOrigin.X, canvas.RulerOrigin.Y)); var length = Bounds.Width + Bounds.Height + Math.Abs(origin.X) + Math.Abs(origin.Y);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(25, 70, 87, 213)), 24), origin - direction * length, origin + direction * length);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(140, 70, 87, 213)), 1), origin - direction * length, origin + direction * length);
        }
        if (canvas.EraserCursor is { } cursor)
        {
            var radius = canvas.EraserSize * canvas.Zoom / 2;
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(25, 218, 92, 114)), new Pen(new SolidColorBrush(Color.FromRgb(218, 92, 114)), 1), cursor, radius, radius);
        }
    }
}

public static class InkExporter
{
    /// <summary>Exports ink only, without selection, cursor or grid. The raster budget prevents unbounded allocations.</summary>
    public static byte[] ExportPng(InkDocument document, double scale = 1, uint? background = 0xFFFFFFFF, double margin = 24)
    {
        InkLimits.Require(double.IsFinite(scale) && scale is > 0 and <= 8 && double.IsFinite(margin) && margin is >= 0 and <= 1024, "Invalid raster export settings.");
        var bounds = document.ContentBounds;
        if (bounds.IsEmpty) bounds = new(0, 0, 800, 600);
        bounds = bounds.Inflate(margin);
        var width = (int)Math.Ceiling(Math.Max(1, bounds.Width * scale)); var height = (int)Math.Ceiling(Math.Max(1, bounds.Height * scale));
        InkLimits.Require(width is > 0 and <= 8192 && height is > 0 and <= 8192 && (long)width * height <= 32_000_000, "Raster export exceeds the 8192-pixel / 32-megapixel budget. Export SVG or reduce scale.");
        var surface = new ExportSurface(document, bounds, scale, background) { Width = width, Height = height };
        surface.Measure(new(width, height)); surface.Arrange(new(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(surface); using var stream = new MemoryStream(); bitmap.Save(stream); return stream.ToArray();
    }
    private sealed class ExportSurface(InkDocument document, InkRect bounds, double scale, uint? background) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (background is { } color) context.FillRectangle(new SolidColorBrush(Color.FromUInt32(color)), new Rect(Bounds.Size));
            using var transform = context.PushTransform(new Matrix(scale, 0, 0, scale, -bounds.X * scale, -bounds.Y * scale));
            foreach (var layer in document.Layers.Where(l => l.IsVisible && l.Opacity > 0))
            {
                using var opacity = context.PushOpacity(layer.Opacity);
                foreach (var stroke in layer.Strokes) InkDrawing.Draw(context, stroke);
            }
        }
    }
}
