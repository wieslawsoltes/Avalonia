using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using Kanban.Core;

namespace Kanban.Avalonia;

/// <summary>A cell's template context. Items are original application objects, not copies.</summary>
public sealed class KanbanColumnView : ObservableObject
{
    private int _totalCount, _columnCount;
    private bool _isDropTarget, _isDropAllowed;
    private string _dropText = "Drop cards here";
    public KanbanBoard Board { get; }
    public KanbanColumnDefinition Definition { get; }
    public KanbanLaneDefinition Lane { get; }
    public KanbanItems Items { get; } = new();
    public int TotalCount { get => _totalCount; internal set { Set(ref _totalCount, value); RefreshLabels(); } }
    public int ColumnCount { get => _columnCount; internal set { Set(ref _columnCount, value); RefreshLabels(); } }
    public int VisibleCount => Items.Count;
    public string CountLabel => Definition.MaximumWorkInProgress is { } max ? $"{ColumnCount} / {max}" : $"{TotalCount}";
    public bool IsOverWip => Definition.MaximumWorkInProgress is { } max && ColumnCount > max;
    public bool IsAtWip => Definition.MaximumWorkInProgress is { } max && ColumnCount >= max;
    public bool IsEmpty => Items.Count == 0;
    public bool IsExpanded => !Definition.IsCollapsed;
    public bool CanAdd => !Board.IsReadOnly && !Definition.IsReadOnly && !Lane.IsReadOnly && Board.AddCardCommand is not null;
    public double DisplayWidth => Definition.IsCollapsed ? 64 : Definition.Width;
    public IBrush Accent { get { try { return Brush.Parse(Definition.Accent); } catch { return Brushes.SlateBlue; } } }
    public bool IsDropTarget { get => _isDropTarget; internal set => Set(ref _isDropTarget, value); }
    public bool IsDropAllowed { get => _isDropAllowed; internal set => Set(ref _isDropAllowed, value); }
    public string DropText { get => _dropText; internal set => Set(ref _dropText, value); }
    internal KanbanColumnView(KanbanBoard board, KanbanColumnDefinition column, KanbanLaneDefinition lane)
    { Board = board; Definition = column; Lane = lane; }
    internal void RefreshLabels()
    {
        foreach (var property in new[] { nameof(CountLabel), nameof(IsOverWip), nameof(IsAtWip), nameof(IsEmpty), nameof(VisibleCount), nameof(IsExpanded), nameof(CanAdd), nameof(DisplayWidth), nameof(Accent) }) Raise(property);
    }
}
public sealed class KanbanLaneView : ObservableObject
{
    public KanbanBoard Board { get; }
    public KanbanLaneDefinition Definition { get; }
    public ObservableCollection<KanbanColumnView> Columns { get; } = new();
    public bool ShowHeader => Board.Swimlanes.Count > 0;
    public bool IsExpanded => !Definition.IsCollapsed;
    public int Count => Columns.Sum(c => c.TotalCount);
    public string Label => $"{Definition.Header}  ·  {Count}";
    public double BodyHeight => Board.FitSingleLaneToViewport && !ShowHeader && Board.Bounds.Height > 160
        ? Math.Max(160, Board.Bounds.Height - 16) : Board.LaneHeight;
    internal KanbanLaneView(KanbanBoard board, KanbanLaneDefinition lane) { Board = board; Definition = lane; }
    internal void RefreshLabels() { Raise(nameof(Count)); Raise(nameof(Label)); Raise(nameof(ShowHeader)); Raise(nameof(IsExpanded)); Raise(nameof(BodyHeight)); }
}
/// <summary>Small edits preserve containers and scroll position; large changes send a single Reset.</summary>
public sealed class KanbanItems : ObservableCollection<object>
{
    internal void ReplaceWith(IReadOnlyList<object> desired)
    {
        if (Count == desired.Count && this.SequenceEqual(desired, ReferenceEqualityComparer.Instance)) return;
        if (Math.Max(Count, desired.Count) > 500)
        {
            Items.Clear(); foreach (var item in desired) Items.Add(item);
            OnPropertyChanged(new(nameof(Count))); OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset)); return;
        }
        for (int i = Count - 1; i >= 0; i--) if (!desired.Contains(this[i], ReferenceEqualityComparer.Instance)) RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < Count && ReferenceEquals(this[i], desired[i])) continue;
            int existing = IndexOf(desired[i]); if (existing >= 0) Move(existing, i); else Insert(i, desired[i]);
        }
    }
}
public sealed record KanbanAddRequest(string ColumnKey, string LaneKey);
