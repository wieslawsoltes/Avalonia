namespace Avalonia.Inking.Core;

public readonly record struct IndexedInk(InkLayer Layer, InkStroke Stroke, int Order);

/// <summary>Bounded uniform-grid broad phase. Large strokes and wide queries safely fall back to a scan.</summary>
public sealed class InkSpatialIndex
{
    private const double CellSize = 128;
    private readonly Dictionary<(int X, int Y), List<int>> _cells = [];
    private readonly List<int> _large = [];
    private readonly IndexedInk[] _entries;
    public int Count => _entries.Length;
    public int CellEntryCount { get; private set; }
    public InkSpatialIndex(InkDocument document)
    {
        _entries = document.Layers.Where(l => l.IsVisible && l.Opacity > 0).SelectMany(l => l.Strokes.Select(s => (l, s))).Select((x, i) => new IndexedInk(x.l, x.s, i)).ToArray();
        for (var i = 0; i < _entries.Length; i++)
        {
            var r = _entries[i].Stroke.Bounds; var (x0, y0, x1, y1) = Cells(r);
            var count = ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1);
            if (count > 4096 || CellEntryCount + count > 250_000) { _large.Add(i); continue; }
            for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++)
            {
                if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = [];
                list.Add(i); CellEntryCount++;
            }
        }
    }
    private static (int X0, int Y0, int X1, int Y1) Cells(InkRect r) => ((int)Math.Floor(r.X / CellSize), (int)Math.Floor(r.Y / CellSize), (int)Math.Floor(r.Right / CellSize), (int)Math.Floor(r.Bottom / CellSize));
    public IReadOnlyList<IndexedInk> Query(InkRect bounds, bool editableOnly = false)
    {
        if (bounds.IsEmpty) return [];
        var (x0, y0, x1, y1) = Cells(bounds);
        bool Accept(IndexedInk item) => (!editableOnly || !item.Layer.IsLocked) && item.Stroke.Bounds.Intersects(bounds);
        if (((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) > 4096) return _entries.Where(Accept).ToArray();
        var ids = new HashSet<int>(_large);
        for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++)
            if (_cells.TryGetValue((x, y), out var entries)) foreach (var id in entries) ids.Add(id);
        return ids.Order().Select(i => _entries[i]).Where(Accept).ToArray();
    }
}
