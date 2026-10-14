using System.Windows.Input;
using System.Text.Json.Serialization;
using Kanban.Core;
namespace Kanban.Sample;
public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class WorkItem : KanbanItem
{
    public string Kind { get; set; } = "FEATURE";
    public string Initials => string.Concat(Assignee.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s[0])).ToUpperInvariant();
    [JsonIgnore] public ICommand? OpenCommand { get; set; }
    [JsonIgnore] public ICommand? DeleteCommand { get; set; }
    [JsonIgnore] public ICommand? NextCommand { get; set; }
    [JsonIgnore] public ICommand? LockCommand { get; set; }
}
public sealed record CardDocument(string Id, string Title, string Description, string ColumnKey, string LaneKey, double Order,
    string Assignee, string Tags, string Priority, bool IsLocked, DateTimeOffset? DueDate, int CompletedTasks, int TotalTasks, string Kind);
public sealed record BoardDocument(int SchemaVersion, bool Swimlanes, CardDocument[] Cards);
[JsonSerializable(typeof(BoardDocument))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class BoardJsonContext : JsonSerializerContext { }
