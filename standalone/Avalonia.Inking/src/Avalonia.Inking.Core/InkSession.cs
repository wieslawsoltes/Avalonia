using System.Collections.Immutable;
using System.ComponentModel;

namespace Avalonia.Inking.Core;

public sealed class InkSessionChangedEventArgs(string reason, bool documentChanged, bool selectionChanged) : EventArgs
{
    public string Reason { get; } = reason;
    public bool DocumentChanged { get; } = documentChanged;
    public bool SelectionChanged { get; } = selectionChanged;
}

/// <summary>
/// Thread-affine editing session. Documents are immutable; history shares samples. One gesture is one
/// transaction. Disposing an uncommitted transaction restores the exact document and redo branch.
/// </summary>
public sealed class InkSession : INotifyPropertyChanged
{
    private sealed record State(InkDocument Document, Guid ActiveLayerId, ImmutableHashSet<Guid> Selection);
    private sealed record Entry(State Before, State After, string Label)
    {
        public long Weight => (long)Before.Document.PointCount + After.Document.PointCount;
    }
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Entry> _undo = [], _redo = [];
    private State _state;
    private InkDocument _saved;
    private InkSpatialIndex? _index;
    private InkDocument? _indexedDocument;
    private InkTransaction? _transaction;
    private State? _transactionBefore;
    private bool _readOnly, _notifying;
    private int _historyCapacity = 200;
    private long _historyPointBudget = 2_000_000;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<InkSessionChangedEventArgs>? Changed;
    public InkDocument Document => _state.Document;
    public Guid ActiveLayerId => _state.ActiveLayerId;
    public InkLayer ActiveLayer => Document.Layers.First(l => l.Id == ActiveLayerId);
    public ImmutableHashSet<Guid> Selection => _state.Selection;
    public IReadOnlyList<InkStroke> SelectedStrokes => Document.AllStrokes.Where(s => Selection.Contains(s.Id)).ToArray();
    public InkRect SelectionBounds => SelectedStrokes.Aggregate(InkRect.Empty, (r, s) => r.Union(s.Bounds));
    public bool IsDirty => !ReferenceEquals(Document, _saved);
    public bool IsEditing => _transaction is not null;
    public bool CanUndo => !IsReadOnly && !IsEditing && _undo.Count > 0;
    public bool CanRedo => !IsReadOnly && !IsEditing && _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string? UndoLabel => _undo.LastOrDefault()?.Label;
    public string? RedoLabel => _redo.LastOrDefault()?.Label;
    public long Revision { get; private set; }
    public bool CanWriteActiveLayer => !IsReadOnly && ActiveLayer.IsVisible && !ActiveLayer.IsLocked && ActiveLayer.Opacity > 0;
    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            Check(); if (_readOnly == value) return;
            if (value) _transaction?.Dispose();
            _readOnly = value; Notify("Read-only changed", false, false);
        }
    }
    public int HistoryCapacity
    {
        get => _historyCapacity;
        set { Check(); InkLimits.Require(value is >= 0 and <= 10_000, "Invalid history capacity."); _historyCapacity = value; TrimHistory(); Notify("History policy", false, false); }
    }
    /// <summary>Conservative point-reference budget, not a claim about exact managed heap usage.</summary>
    public long HistoryPointBudget
    {
        get => _historyPointBudget;
        set { Check(); InkLimits.Require(value >= 0, "Invalid history budget."); _historyPointBudget = value; TrimHistory(); Notify("History policy", false, false); }
    }
    public InkSpatialIndex Index
    {
        get
        {
            if (!ReferenceEquals(Document, _indexedDocument)) { _index = new(Document); _indexedDocument = Document; }
            return _index!;
        }
    }
    public InkSession(InkDocument? document = null)
    {
        var doc = document ?? new InkDocument();
        _state = new(doc, doc.Layers[^1].Id, ImmutableHashSet<Guid>.Empty); _saved = doc;
    }
    private void Check(bool write = false)
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("InkSession must be edited on its creating thread.");
        if (_notifying) throw new InvalidOperationException("Do not edit an InkSession from its change notification; dispatch the edit instead.");
        if (write && IsReadOnly) throw new InvalidOperationException("Document is read-only.");
    }
    private void Notify(string reason, bool document, bool selection)
    {
        if (document) Revision++;
        _notifying = true;
        try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); Changed?.Invoke(this, new(reason, document, selection)); }
        finally { _notifying = false; }
    }
    private static ImmutableHashSet<Guid> EligibleSelection(InkDocument document, IEnumerable<Guid> ids)
    {
        var eligible = document.Layers.Where(l => l.IsVisible && !l.IsLocked && l.Opacity > 0).SelectMany(l => l.Strokes).Select(s => s.Id).ToHashSet();
        return ids.Where(eligible.Contains).ToImmutableHashSet();
    }
    private void SetState(State state, string label, bool record)
    {
        var documentChanged = !ReferenceEquals(_state.Document, state.Document);
        var selectionChanged = !_state.Selection.SetEquals(state.Selection);
        if (!documentChanged && !selectionChanged && _state.ActiveLayerId == state.ActiveLayerId) return;
        var before = _state; _state = state;
        if (record && documentChanged && _transaction is null)
        {
            _undo.Add(new(before, state, label)); _redo.Clear(); TrimHistory();
        }
        Notify(label, documentChanged, selectionChanged);
    }
    private void Apply(InkDocument document, string label, IEnumerable<Guid>? selection = null, Guid? activeLayer = null)
    {
        Check(true);
        var active = activeLayer ?? ActiveLayerId;
        if (!document.Layers.Any(l => l.Id == active)) active = document.Layers[^1].Id;
        SetState(new(document, active, EligibleSelection(document, selection ?? Selection)), label, true);
    }
    private void TrimHistory()
    {
        while (_undo.Count + _redo.Count > _historyCapacity || _undo.Sum(e => e.Weight) + _redo.Sum(e => e.Weight) > _historyPointBudget)
        {
            if (_undo.Count > 0) _undo.RemoveAt(0);
            else if (_redo.Count > 0) _redo.RemoveAt(0);
            else break;
        }
    }
    public InkTransaction BeginTransaction(string label)
    {
        Check(true);
        if (_transaction is not null) throw new InvalidOperationException("Nested ink transactions are not supported.");
        _transactionBefore = _state; _transaction = new(this, label);
        Notify("Gesture started", false, false); return _transaction;
    }
    internal void EndTransaction(InkTransaction transaction, bool commit)
    {
        Check(); if (!ReferenceEquals(_transaction, transaction)) return;
        var before = _transactionBefore!; _transaction = null; _transactionBefore = null;
        if (!commit) { SetState(before, "Gesture cancelled", false); Notify("Gesture ended", false, false); return; }
        if (!ReferenceEquals(before.Document, Document))
        {
            _undo.Add(new(before, _state, transaction.Label)); _redo.Clear(); TrimHistory();
        }
        Notify(transaction.Label, false, false);
    }
    public void Undo()
    {
        Check(true); if (!CanUndo) return;
        var item = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(item); SetState(item.Before, "Undo " + item.Label, false);
    }
    public void Redo()
    {
        Check(true); if (!CanRedo) return;
        var item = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(item); SetState(item.After, "Redo " + item.Label, false);
    }
    public void ClearHistory() { Check(); if (IsEditing) throw new InvalidOperationException("A gesture is active."); _undo.Clear(); _redo.Clear(); Notify("History cleared", false, false); }
    public void MarkSaved(InkDocument? savedSnapshot = null) { Check(); _saved = savedSnapshot ?? Document; Notify("Saved", false, false); }
    public void Load(InkDocument document, bool markSaved = true)
    {
        ArgumentNullException.ThrowIfNull(document); Check(); _transaction?.Dispose();
        _undo.Clear(); _redo.Clear(); if (markSaved) _saved = document;
        SetState(new(document, document.Layers[^1].Id, ImmutableHashSet<Guid>.Empty), "Loaded", false);
    }
    public void LoadJson(string json, bool markSaved = true) { var document = InkSerializer.Deserialize(json); Load(document, markSaved); }
    public void Rename(string title) { if (title != Document.Title) Apply(Document.WithTitle(title), "Rename document"); }
    public void SetActiveLayer(Guid id)
    {
        Check(); InkLimits.Require(Document.Layers.Any(l => l.Id == id), "Unknown layer.");
        SetState(_state with { ActiveLayerId = id }, "Active layer", false);
    }
    private void RequireActiveLayer()
    {
        Check(true); if (!CanWriteActiveLayer) throw new InvalidOperationException("The active layer is hidden, locked, or transparent.");
    }
    public void AddStroke(InkStroke stroke) => AddStrokes([stroke]);
    public void AddStrokes(IEnumerable<InkStroke> strokes, bool select = false)
    {
        RequireActiveLayer(); var added = strokes.ToArray(); if (added.Length == 0) return;
        var next = Document.WithLayers(Document.Layers.Select(l => l.Id == ActiveLayerId ? l.With(strokes: l.Strokes.Concat(added)) : l));
        Apply(next, "Add ink", select ? added.Select(s => s.Id) : []);
    }
    public Guid AddLayer(string? name = null)
    {
        Check(true); var layer = new InkLayer(name ?? $"Layer {Document.Layers.Length + 1}");
        Apply(Document.WithLayers(Document.Layers.Add(layer)), "Add layer", [], layer.Id); return layer.Id;
    }
    public void UpdateLayer(Guid id, string? name = null, bool? visible = null, bool? locked = null, double? opacity = null)
    {
        Check(true); var layer = Document.Layers.FirstOrDefault(l => l.Id == id) ?? throw new ArgumentException("Unknown layer.");
        var replacement = layer.With(name, visible, locked, opacity);
        if (replacement.Name == layer.Name && replacement.IsVisible == layer.IsVisible && replacement.IsLocked == layer.IsLocked && replacement.Opacity == layer.Opacity) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.Id == id ? replacement : l)), "Update layer");
    }
    public void RemoveLayer(Guid id)
    {
        Check(true); var layer = Document.Layers.FirstOrDefault(l => l.Id == id) ?? throw new ArgumentException("Unknown layer.");
        if (layer.IsLocked || Document.Layers.Length == 1) throw new InvalidOperationException("Unlock the layer and retain at least one layer.");
        Apply(Document.WithLayers(Document.Layers.Where(l => l.Id != id)), "Remove layer");
    }
    public void MoveLayer(Guid id, int delta)
    {
        Check(true); var list = Document.Layers.ToList(); var old = list.FindIndex(l => l.Id == id);
        if (old < 0) throw new ArgumentException("Unknown layer.");
        var index = Math.Clamp(old + delta, 0, list.Count - 1); if (index == old) return;
        var layer = list[old]; list.RemoveAt(old); list.Insert(index, layer); Apply(Document.WithLayers(list), "Reorder layer");
    }
    public void SetSelection(IEnumerable<Guid> ids)
    {
        Check(); SetState(_state with { Selection = EligibleSelection(Document, ids) }, "Selection", false);
    }
    public void ClearSelection() => SetSelection([]);
    public void SelectAll() => SetSelection(Document.AllStrokes.Select(s => s.Id));
    public Guid? HitTest(InkVector point, double radius = 4)
        => Index.Query(new InkRect(point.X, point.Y, 0, 0).Inflate(radius), true).Reverse().FirstOrDefault(e => InkGeometry.HitTest(e.Stroke, point, radius)).Stroke?.Id;
    public void SelectPolygon(IReadOnlyList<InkVector> polygon, bool additive = false)
    {
        Check(); if (polygon.Count < 3) return;
        var bounds = polygon.Aggregate(InkRect.Empty, (r, p) => r.Union(new(p.X, p.Y, 0, 0)));
        var ids = Index.Query(bounds, true).Where(e => InkGeometry.IntersectsPolygon(e.Stroke, polygon)).Select(e => e.Stroke.Id);
        SetSelection(additive ? Selection.Union(ids) : ids);
    }
    public void SelectRectangle(InkRect r, bool additive = false) => SelectPolygon([new(r.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom)], additive);
    public void TransformSelection(InkTransform transform)
    {
        Check(true); transform.Validate(); if (Selection.Count == 0 || transform == InkTransform.Identity) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.With(strokes: l.Strokes.Select(s => Selection.Contains(s.Id) && !l.IsLocked ? s.Transformed(transform) : s)))), "Transform ink");
    }
    public void RestyleSelection(Func<InkBrush, InkBrush> change)
    {
        Check(true); if (Selection.Count == 0) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.With(strokes: l.Strokes.Select(s => Selection.Contains(s.Id) && !l.IsLocked ? s.Restyled(change(s.Brush)) : s)))), "Style ink");
    }
    public void DeleteSelection() => DeleteStrokes(Selection);
    public void DeleteStrokes(IEnumerable<Guid> ids)
    {
        Check(true); var targets = EligibleSelection(Document, ids); if (targets.Count == 0) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.With(strokes: l.Strokes.Where(s => !targets.Contains(s.Id))))), "Delete ink");
    }
    public void ClearActiveLayer()
    {
        RequireActiveLayer(); if (ActiveLayer.Strokes.Length == 0) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.Id == ActiveLayerId ? l.With(strokes: []) : l)), "Clear layer");
    }
    public void DuplicateSelection()
    {
        RequireActiveLayer(); AddStrokes(SelectedStrokes.Select(s => s.Clone(InkTransform.Translation(18, 18))), true);
    }
    public void ReorderSelection(bool front)
    {
        Check(true); if (Selection.Count == 0) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.With(strokes: front
            ? l.Strokes.Where(s => !Selection.Contains(s.Id)).Concat(l.Strokes.Where(s => Selection.Contains(s.Id)))
            : l.Strokes.Where(s => Selection.Contains(s.Id)).Concat(l.Strokes.Where(s => !Selection.Contains(s.Id)))))), "Reorder ink");
    }
    public void Erase(InkVector from, InkVector to, double radius, bool partial)
    {
        Check(true); InkLimits.Require(double.IsFinite(radius) && radius is >= 0 and <= 512, "Invalid eraser radius.");
        var replacements = new Dictionary<Guid, IReadOnlyList<InkStroke>>();
        foreach (var e in Index.Query(InkRect.Between(from, to).Inflate(radius), true))
        {
            if (!InkGeometry.HitSweep(e.Stroke, from, to, radius)) continue;
            var pieces = partial ? InkGeometry.Erase(e.Stroke, from, to, radius) : [];
            if (pieces.Count == 1 && ReferenceEquals(pieces[0], e.Stroke)) continue;
            replacements[e.Stroke.Id] = pieces;
        }
        if (replacements.Count == 0) return;
        Apply(Document.WithLayers(Document.Layers.Select(l => l.With(strokes: l.Strokes.SelectMany(s => replacements.TryGetValue(s.Id, out var pieces) ? pieces : (IReadOnlyList<InkStroke>)[s])))), "Erase ink");
    }
    public string CopySelectionJson()
    {
        var strokes = Document.Layers.SelectMany(l => l.Strokes.Where(s => Selection.Contains(s.Id)).Select(s => s.Restyled(s.Brush with { Opacity = s.Brush.Opacity * l.Opacity })));
        return InkSerializer.Serialize(new InkDocument("Ink selection", [new InkLayer("Selection", strokes)]));
    }
    public void PasteJson(string json, InkTransform? transform = null)
    {
        var document = InkSerializer.Deserialize(json);
        AddStrokes(document.Layers.Where(l => l.IsVisible).SelectMany(l => l.Strokes.Select(s => s.Restyled(s.Brush with { Opacity = s.Brush.Opacity * l.Opacity }).Clone(transform ?? InkTransform.Translation(18, 18)))), true);
    }
}

public sealed class InkTransaction : IDisposable
{
    private InkSession? _session;
    public string Label { get; }
    internal InkTransaction(InkSession session, string label) { _session = session; Label = label; }
    public void Commit() { var session = _session; _session = null; session?.EndTransaction(this, true); }
    public void Dispose() { var session = _session; _session = null; session?.EndTransaction(this, false); }
}
