using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kanban.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>Convenience model. Custom application models can use an IKanbanAdapter instead.</summary>
public class KanbanItem : ObservableObject
{
    private string _title = "", _description = "", _columnKey = "", _laneKey = "", _assignee = "", _tags = "", _priority = "Normal";
    private double _order;
    private bool _isLocked;
    private DateTimeOffset? _dueDate;
    private int _completed, _total;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get => _title; set => Set(ref _title, value ?? ""); }
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }
    public string ColumnKey { get => _columnKey; set => Set(ref _columnKey, value ?? ""); }
    public string LaneKey { get => _laneKey; set => Set(ref _laneKey, value ?? ""); }
    public double Order { get => _order; set { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _order, value); } }
    public string Assignee { get => _assignee; set => Set(ref _assignee, value ?? ""); }
    public string Tags { get => _tags; set => Set(ref _tags, value ?? ""); }
    public string Priority { get => _priority; set => Set(ref _priority, value ?? "Normal"); }
    public bool IsLocked { get => _isLocked; set => Set(ref _isLocked, value); }
    public DateTimeOffset? DueDate { get => _dueDate; set { if (Set(ref _dueDate, value)) Raise(nameof(DueText)); } }
    public string DueText => DueDate?.ToString("dd MMM") ?? "No due date";
    public int CompletedTasks { get => _completed; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); if (Set(ref _completed, value)) Raise(nameof(Progress)); } }
    public int TotalTasks { get => _total; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); if (Set(ref _total, value)) Raise(nameof(Progress)); } }
    public double Progress => TotalTasks == 0 ? 0 : Math.Clamp(100d * CompletedTasks / TotalTasks, 0, 100);
    public override string ToString() => Title;
}

public sealed class KanbanColumnDefinition : ObservableObject
{
    private string _header = "Column", _key = "", _accent = "#6875D9";
    private bool _collapsed, _readOnly, _allowDrop = true;
    private int? _maximum;
    private double _width = 292;
    public string Key { get => _key; set => Set(ref _key, value ?? ""); }
    public string Header { get => _header; set => Set(ref _header, value ?? ""); }
    public string Accent { get => _accent; set => Set(ref _accent, value ?? "#6875D9"); }
    public int? MaximumWorkInProgress { get => _maximum; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _maximum, value); } }
    public bool IsCollapsed { get => _collapsed; set => Set(ref _collapsed, value); }
    public bool IsReadOnly { get => _readOnly; set => Set(ref _readOnly, value); }
    public bool AllowDrop { get => _allowDrop; set => Set(ref _allowDrop, value); }
    public double Width { get => _width; set { if (!double.IsFinite(value) || value < 160 || value > 1200) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _width, value); } }
}

public sealed class KanbanLaneDefinition : ObservableObject
{
    private string _key = "", _header = "";
    private bool _collapsed, _readOnly;
    public string Key { get => _key; set => Set(ref _key, value ?? ""); }
    public string Header { get => _header; set => Set(ref _header, value ?? ""); }
    public bool IsCollapsed { get => _collapsed; set => Set(ref _collapsed, value); }
    public bool IsReadOnly { get => _readOnly; set => Set(ref _readOnly, value); }
}

public readonly record struct KanbanPlacement(string ColumnKey, string LaneKey, double Order);

/// <summary>Maps arbitrary application objects without reflection. SetPlacement must be synchronous and reversible.</summary>
public interface IKanbanAdapter
{
    string GetId(object item);
    string GetTitle(object item);
    string GetSearchText(object item);
    KanbanPlacement GetPlacement(object item);
    bool CanMove(object item);
    void SetPlacement(object item, KanbanPlacement placement);
}

public sealed class DefaultKanbanAdapter : IKanbanAdapter
{
    public static DefaultKanbanAdapter Instance { get; } = new();
    private static KanbanItem Model(object item) => item as KanbanItem ?? throw new ArgumentException("Set ItemAdapter for models other than KanbanItem.");
    public string GetId(object item) => Model(item).Id;
    public string GetTitle(object item) => Model(item).Title;
    public string GetSearchText(object item) { var m = Model(item); return $"{m.Id} {m.Title} {m.Description} {m.Assignee} {m.Tags} {m.Priority}"; }
    public KanbanPlacement GetPlacement(object item) { var m = Model(item); return new(m.ColumnKey, m.LaneKey, m.Order); }
    public bool CanMove(object item) => !Model(item).IsLocked;
    public void SetPlacement(object item, KanbanPlacement p) { var m = Model(item); m.ColumnKey = p.ColumnKey; m.LaneKey = p.LaneKey; m.Order = p.Order; }
}

public enum WipLimitMode { Ignore, Warn, Enforce }
public enum KanbanMoveFailure { None, EmptySelection, InvalidData, UnknownItem, UnknownTarget, Locked, ReadOnly, LaneChangeNotAllowed, WipLimit, InvalidAnchor, StaleRevision, Canceled, Policy, AdapterFailure, HistoryConflict, Busy, NoHistory }
public sealed record KanbanMoveResult(bool Succeeded, KanbanMoveFailure Failure = KanbanMoveFailure.None, string Message = "", bool IsWarning = false)
{
    public static KanbanMoveResult Reject(KanbanMoveFailure reason, string message) => new(false, reason, message);
    public static KanbanMoveResult Success(string message = "Moved", bool warning = false) => new(true, Message: message, IsWarning: warning);
}
/// <summary>BeforeId is an anchor in the destination after removing all moving items; null appends.</summary>
public sealed record KanbanMoveRequest(IReadOnlyList<string> ItemIds, string ColumnKey, string LaneKey = "", string? BeforeId = null, long? ExpectedRevision = null);
public sealed class KanbanMovingEventArgs(KanbanMoveRequest request) : EventArgs
{
    public KanbanMoveRequest Request { get; } = request;
    public bool Cancel { get; set; }
    public string? Reason { get; set; }
}
public sealed class KanbanMovedEventArgs(KanbanMoveRequest request, KanbanMoveResult result) : EventArgs
{
    public KanbanMoveRequest Request { get; } = request;
    public KanbanMoveResult Result { get; } = result;
}
public sealed record KanbanEntry(string Id, object Item, KanbanPlacement Placement, int SourceIndex);
