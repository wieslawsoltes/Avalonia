using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Kanban.Core;

/// <summary>
/// UI-independent board index and transactional move coordinator. Use on one owning thread.
/// Source notifications are observed; DeferRefresh batches bulk edits. No source collection is reordered.
/// </summary>
public sealed class KanbanController : IDisposable
{
    private IEnumerable _source = Array.Empty<object>();
    private IKanbanAdapter _adapter = DefaultKanbanAdapter.Instance;
    private IEnumerable<KanbanColumnDefinition> _columns = Array.Empty<KanbanColumnDefinition>();
    private IEnumerable<KanbanLaneDefinition> _lanes = Array.Empty<KanbanLaneDefinition>();
    private readonly HashSet<INotifyPropertyChanged> _observed = new(ReferenceEqualityComparer.Instance);
    private readonly List<INotifyCollectionChanged> _collections = new();
    private Dictionary<string, KanbanEntry> _index = new(StringComparer.Ordinal);
    private Dictionary<(string Column, string Lane), List<KanbanEntry>> _buckets = new();
    private Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _diagnostics = Array.Empty<string>();
    private readonly List<History> _undo = new(), _redo = new();
    private int _defer;
    private bool _pending, _mutating, _busy, _disposed;
    private sealed record Delta(string Id, object Item, KanbanPlacement Before, KanbanPlacement After);
    private sealed record History(IReadOnlyList<Delta> Changes, IReadOnlySet<string> MovedIds);
    public long Revision { get; private set; }
    public bool IsReadOnly { get; set; }
    public bool AllowLaneChange { get; set; } = true;
    public WipLimitMode WipLimitMode { get; set; } = WipLimitMode.Enforce;
    public int HistoryLimit { get; set; } = 100;
    public bool CanUndo => _undo.Count > 0 && !IsReadOnly;
    public bool CanRedo => _redo.Count > 0 && !IsReadOnly;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public IKanbanAdapter Adapter => _adapter;
    public IReadOnlyCollection<KanbanEntry> Entries => _index.Values;
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public Func<KanbanMoveRequest, KanbanMoveResult>? MovePolicy { get; set; }
    public event EventHandler? Changed;
    public event EventHandler<KanbanMovingEventArgs>? Moving;
    public event EventHandler<KanbanMovedEventArgs>? Moved;

    public void Bind(IEnumerable? items, IEnumerable<KanbanColumnDefinition>? columns,
        IEnumerable<KanbanLaneDefinition>? lanes = null, IKanbanAdapter? adapter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Cannot rebind during a move.");
        Unsubscribe();
        _source = items ?? Array.Empty<object>(); _columns = columns ?? Array.Empty<KanbanColumnDefinition>();
        _lanes = lanes ?? Array.Empty<KanbanLaneDefinition>(); _adapter = adapter ?? DefaultKanbanAdapter.Instance;
        foreach (var o in new object[] { _source, _columns, _lanes })
            if (o is INotifyCollectionChanged c && !_collections.Contains(c)) { c.CollectionChanged += CollectionChanged; _collections.Add(c); }
        _undo.Clear(); _redo.Clear(); Refresh();
    }
    public KanbanEntry? Find(string id) => _index.GetValueOrDefault(id);
    public IReadOnlyList<KanbanEntry> GetCell(string column, string lane = "") =>
        _buckets.TryGetValue((column, lane), out var value) ? value.AsReadOnly() : Array.Empty<KanbanEntry>();
    public int GetColumnCount(string key) => _counts.GetValueOrDefault(key);

    public IDisposable DeferRefresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _defer++;
        return new Scope(() => { if (--_defer == 0 && _pending) { _pending = false; Refresh(); } });
    }
    private sealed class Scope(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestRefresh();
    private void ItemChanged(object? sender, PropertyChangedEventArgs e) => RequestRefresh();
    private void RequestRefresh()
    {
        if (_disposed) return;
        if (_mutating || _defer > 0) { _pending = true; return; }
        Refresh();
    }
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutating || _defer > 0) { _pending = true; return; }
        var diagnostics = new List<string>();
        var index = new Dictionary<string, KanbanEntry>(StringComparer.Ordinal);
        var observed = new HashSet<INotifyPropertyChanged>(ReferenceEqualityComparer.Instance);
        var cols = _columns.ToArray(); var lanes = _lanes.ToArray();
        if (cols.Any(c => string.IsNullOrWhiteSpace(c.Key)) || cols.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != cols.Length)
            diagnostics.Add("Column keys must be nonempty and unique.");
        if (lanes.Select(l => l.Key).Distinct(StringComparer.Ordinal).Count() != lanes.Length)
            diagnostics.Add("Swimlane keys must be unique.");
        var columnKeys = cols.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var laneKeys = lanes.Length == 0 ? new HashSet<string>(StringComparer.Ordinal) { "" } : lanes.Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var d in cols.Cast<INotifyPropertyChanged>().Concat(lanes)) observed.Add(d);
        int n = 0;
        foreach (var item in _source)
        {
            if (item is INotifyPropertyChanged p) observed.Add(p);
            try
            {
                if (item is null) throw new ArgumentException("Null card in ItemsSource.");
                var id = _adapter.GetId(item); var pos = _adapter.GetPlacement(item);
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Card IDs must be nonempty.");
                if (!double.IsFinite(pos.Order)) throw new ArgumentException($"{id}: non-finite card order.");
                if (!index.TryAdd(id, new(id, item, pos, n))) diagnostics.Add($"Duplicate card ID: {id}.");
                if (!columnKeys.Contains(pos.ColumnKey) || !laneKeys.Contains(pos.LaneKey)) diagnostics.Add($"{id}: unmapped column or swimlane.");
            }
            catch (Exception ex) { diagnostics.Add($"Card {n}: {ex.Message}"); }
            n++;
        }
        foreach (var old in _observed.Except(observed).ToArray()) { old.PropertyChanged -= ItemChanged; _observed.Remove(old); }
        foreach (var added in observed.Except(_observed).ToArray()) { added.PropertyChanged += ItemChanged; _observed.Add(added); }
        bool placementChanged = index.Count != _index.Count || index.Any(kv => !_index.TryGetValue(kv.Key, out var old) ||
            old.Placement != kv.Value.Placement || !ReferenceEquals(old.Item, kv.Value.Item));
        if (placementChanged) { _undo.Clear(); _redo.Clear(); }
        _index = index; _diagnostics = diagnostics.AsReadOnly();
        RebuildBuckets(); Revision++; _pending = false; Changed?.Invoke(this, EventArgs.Empty);
    }
    private void RebuildBuckets()
    {
        _buckets = _index.Values.GroupBy(e => (e.Placement.ColumnKey, e.Placement.LaneKey))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Placement.Order).ThenBy(e => e.SourceIndex).ToList());
        _counts = _index.Values.GroupBy(e => e.Placement.ColumnKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    public KanbanMoveResult Validate(KanbanMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed) return KanbanMoveResult.Reject(KanbanMoveFailure.InvalidData, "Board is disposed.");
        if (_defer > 0) return KanbanMoveResult.Reject(KanbanMoveFailure.Busy, "Finish the source update batch before moving cards.");
        if (IsReadOnly) return KanbanMoveResult.Reject(KanbanMoveFailure.ReadOnly, "Board is read-only.");
        if (_diagnostics.Count != 0) return KanbanMoveResult.Reject(KanbanMoveFailure.InvalidData, _diagnostics[0]);
        if (request.ExpectedRevision is { } revision && revision != Revision) return KanbanMoveResult.Reject(KanbanMoveFailure.StaleRevision, "Board changed during the operation.");
        if (request.ItemIds is null || request.ItemIds.Count == 0) return KanbanMoveResult.Reject(KanbanMoveFailure.EmptySelection, "Select a card first.");
        if (request.ItemIds.Any(string.IsNullOrWhiteSpace)) return KanbanMoveResult.Reject(KanbanMoveFailure.InvalidData, "Card IDs must be nonempty.");
        if (request.ItemIds.Distinct(StringComparer.Ordinal).Count() != request.ItemIds.Count) return KanbanMoveResult.Reject(KanbanMoveFailure.InvalidData, "Duplicate card IDs in move.");
        var col = _columns.FirstOrDefault(c => c.Key == request.ColumnKey);
        var lane = _lanes.FirstOrDefault(l => l.Key == request.LaneKey);
        if (col is null || (_lanes.Any() ? lane is null : !string.IsNullOrEmpty(request.LaneKey))) return KanbanMoveResult.Reject(KanbanMoveFailure.UnknownTarget, "Destination does not exist.");
        if (col.IsReadOnly || !col.AllowDrop || lane?.IsReadOnly == true) return KanbanMoveResult.Reject(KanbanMoveFailure.ReadOnly, "Destination does not accept moves.");
        int incoming = 0;
        foreach (var id in request.ItemIds)
        {
            if (!_index.TryGetValue(id, out var entry)) return KanbanMoveResult.Reject(KanbanMoveFailure.UnknownItem, $"Card {id} no longer exists.");
            if (!_adapter.CanMove(entry.Item)) return KanbanMoveResult.Reject(KanbanMoveFailure.Locked, $"{_adapter.GetTitle(entry.Item)} is locked.");
            if (_columns.Any(c => c.Key == entry.Placement.ColumnKey && c.IsReadOnly) || _lanes.Any(l => l.Key == entry.Placement.LaneKey && l.IsReadOnly))
                return KanbanMoveResult.Reject(KanbanMoveFailure.ReadOnly, "Source is read-only.");
            if (!AllowLaneChange && entry.Placement.LaneKey != request.LaneKey) return KanbanMoveResult.Reject(KanbanMoveFailure.LaneChangeNotAllowed, "Moving between swimlanes is disabled.");
            if (entry.Placement.ColumnKey != request.ColumnKey) incoming++;
        }
        if (request.BeforeId is { } before && (!_index.TryGetValue(before, out var anchor) ||
            anchor.Placement.ColumnKey != request.ColumnKey || anchor.Placement.LaneKey != request.LaneKey || request.ItemIds.Contains(before)))
            return KanbanMoveResult.Reject(KanbanMoveFailure.InvalidAnchor, "Insertion anchor is invalid.");
        bool over = col.MaximumWorkInProgress is { } max && GetColumnCount(col.Key) + incoming > max && incoming > 0;
        if (over && WipLimitMode == WipLimitMode.Enforce) return KanbanMoveResult.Reject(KanbanMoveFailure.WipLimit, $"{col.Header} has reached its WIP limit ({col.MaximumWorkInProgress}).");
        var policy = MovePolicy?.Invoke(request);
        if (policy is { Succeeded: false }) return policy;
        return KanbanMoveResult.Success(over && WipLimitMode == WipLimitMode.Warn ? "Move exceeds the WIP limit." : "Move allowed", over && WipLimitMode == WipLimitMode.Warn);
    }

    public KanbanMoveResult TryMove(KanbanMoveRequest request)
    {
        if (_busy) return KanbanMoveResult.Reject(KanbanMoveFailure.Busy, "Another board operation is in progress.");
        _busy = true;
        try
        {
            // Snapshot caller-owned collections so preview handlers cannot rewrite an in-flight request.
            request = request with { ItemIds = Array.AsReadOnly(request.ItemIds?.ToArray() ?? []) };
            var revision = Revision;
            var valid = Validate(request); if (!valid.Succeeded) return valid;
            if (Revision != revision) return KanbanMoveResult.Reject(KanbanMoveFailure.StaleRevision, "Board changed in the move policy.");
            var args = new KanbanMovingEventArgs(request); Moving?.Invoke(this, args);
            if (args.Cancel) return KanbanMoveResult.Reject(KanbanMoveFailure.Canceled, args.Reason ?? "Move canceled.");
            if (Revision != revision) return KanbanMoveResult.Reject(KanbanMoveFailure.StaleRevision, "Board changed in the preview handler.");
            valid = Validate(request); if (!valid.Succeeded) return valid;
            if (Revision != revision) return KanbanMoveResult.Reject(KanbanMoveFailure.StaleRevision, "Board changed in the move policy.");
            var moving = request.ItemIds.Select(id => _index[id]).ToArray();
            var movingIds = request.ItemIds.ToHashSet(StringComparer.Ordinal);
            var target = GetCell(request.ColumnKey, request.LaneKey).Where(e => !movingIds.Contains(e.Id)).ToList();
            int at = request.BeforeId is null ? target.Count : target.FindIndex(e => e.Id == request.BeforeId);
            target.InsertRange(at, moving);
            // Normalize just the target cell; gaps in source cells are harmless. Stable integer order avoids float exhaustion.
            var changes = target.Select((e, i) => new Delta(e.Id, e.Item, e.Placement, new(request.ColumnKey, request.LaneKey, i)))
                .Where(d => d.Before != d.After).ToArray();
            if (changes.Length == 0) return KanbanMoveResult.Success("No change.");
            var result = Apply(changes, forward: true); if (!result.Succeeded) return result;
            if (HistoryLimit > 0)
            {
                _undo.Add(new(changes, movingIds));
                while (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            }
            _redo.Clear();
            result = KanbanMoveResult.Success($"Moved {moving.Length} card{(moving.Length == 1 ? "" : "s")}", valid.IsWarning);
            Changed?.Invoke(this, EventArgs.Empty); Moved?.Invoke(this, new(request, result)); return result;
        }
        finally { _busy = false; }
    }

    private KanbanMoveResult Apply(IReadOnlyList<Delta> changes, bool forward)
    {
        var applied = new List<Delta>(); _mutating = true;
        try
        {
            foreach (var d in changes)
            {
                // Register before calling the adapter: an adapter may throw after a partial write.
                applied.Add(d); _adapter.SetPlacement(d.Item, forward ? d.After : d.Before);
            }
            foreach (var d in changes) _index[d.Id] = _index[d.Id] with { Placement = forward ? d.After : d.Before };
            RebuildBuckets(); Revision++; _pending = false;
            return KanbanMoveResult.Success();
        }
        catch (Exception ex)
        {
            var errors = new List<Exception> { ex };
            foreach (var d in applied.AsEnumerable().Reverse())
                try { _adapter.SetPlacement(d.Item, forward ? d.Before : d.After); } catch (Exception rollback) { errors.Add(rollback); }
            _undo.Clear(); _redo.Clear();
            if (errors.Count > 1)
                throw new AggregateException("Adapter failed to roll back; reload the authoritative data source.", errors);
            return KanbanMoveResult.Reject(KanbanMoveFailure.AdapterFailure, $"Move rolled back: {ex.Message}");
        }
        finally { _mutating = false; if (_pending) { _pending = false; Refresh(); } }
    }

    public KanbanMoveResult Undo() => Replay(false);
    public KanbanMoveResult Redo() => Replay(true);
    private KanbanMoveResult Replay(bool forward)
    {
        if (_busy || _defer > 0) return KanbanMoveResult.Reject(KanbanMoveFailure.Busy, "Another operation or source update batch is in progress.");
        if (IsReadOnly) return KanbanMoveResult.Reject(KanbanMoveFailure.ReadOnly, "Board is read-only.");
        var source = forward ? _redo : _undo; var destination = forward ? _undo : _redo;
        if (source.Count == 0) return KanbanMoveResult.Reject(KanbanMoveFailure.NoHistory, "No history available.");
        var h = source[^1];
        foreach (var d in h.Changes)
        {
            if (!_index.TryGetValue(d.Id, out var e) || !ReferenceEquals(e.Item, d.Item) ||
                _adapter.GetPlacement(e.Item) != (forward ? d.Before : d.After))
            { _undo.Clear(); _redo.Clear(); return KanbanMoveResult.Reject(KanbanMoveFailure.HistoryConflict, "External changes invalidated history."); }
            if ((h.MovedIds.Contains(d.Id) && !_adapter.CanMove(e.Item)) || _columns.Any(c => (c.Key == d.Before.ColumnKey || c.Key == d.After.ColumnKey) && c.IsReadOnly) ||
                _lanes.Any(l => (l.Key == d.Before.LaneKey || l.Key == d.After.LaneKey) && l.IsReadOnly))
                return KanbanMoveResult.Reject(KanbanMoveFailure.Locked, "A card or its column/lane is now locked.");
        }
        _busy = true;
        try
        {
            var result = Apply(h.Changes, forward);
            if (result.Succeeded) { source.RemoveAt(source.Count - 1); destination.Add(h); Changed?.Invoke(this, EventArgs.Empty); }
            return result.Succeeded ? KanbanMoveResult.Success(forward ? "Redone" : "Undone") : result;
        }
        finally { _busy = false; }
    }
    private void Unsubscribe()
    {
        foreach (var c in _collections) c.CollectionChanged -= CollectionChanged; _collections.Clear();
        foreach (var p in _observed) p.PropertyChanged -= ItemChanged; _observed.Clear();
    }
    public void Dispose() { if (_disposed) return; Unsubscribe(); _index.Clear(); _buckets.Clear(); _undo.Clear(); _redo.Clear(); _disposed = true; }
}
