using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Kanban.Avalonia;
using Kanban.Core;

namespace Kanban.Sample;

public partial class MainView : UserControl
{
    public static MainView? Current { get; private set; }
    public ObservableCollection<WorkItem> Items { get; } = new();
    public KanbanBoard BoardControl => this.FindControl<KanbanBoard>("Board")!;
    private bool _initialized, _resetting;
    private WorkItem? _editing;
    private int _nextId = 100;
    public double LastLoadMilliseconds { get; private set; }
    public MainView()
    {
        AvaloniaXamlLoader.Load(this); DataContext = this; Current = this;
        BoardControl.ItemsSource = Items;
        BoardControl.AddCardCommand = new RelayCommand(p => AddCard(p as KanbanAddRequest));
        BoardControl.CardInvokedCommand = new RelayCommand(p => { if (p is WorkItem w) OpenEditor(w); });
        BoardControl.DeleteCardsCommand = new RelayCommand(p => { if (p is object[] a) DeleteCards(a.OfType<WorkItem>()); });
        BoardControl.BoardSelectionChanged += (_, _) => UpdateMetrics();
        BoardControl.Controller.Changed += (_, _) => Dispatcher.UIThread.Post(UpdateMetrics);
        _initialized = true; Reset();
        AttachedToVisualTree += (_, _) => { Current = this; BoardControl.RefreshView(); };
        DetachedFromVisualTree += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
    }
    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    public void Reset(int count = 16)
    {
        _resetting = true; var watch = Stopwatch.StartNew();
        try
        {
            BoardControl.CancelDrag(); BoardControl.SelectOnly(null); BoardControl.IsReadOnly = false;
            BoardControl.SearchText = ""; BoardControl.Filter = null; BoardControl.SortComparer = null;
            C<CheckBox>("MineBox").IsChecked = false; C<CheckBox>("LockedBox").IsChecked = false;
            C<CheckBox>("LanesBox").IsChecked = false; C<ComboBox>("SortBox").SelectedIndex = 0;
            C<Border>("Editor").IsVisible = false; _editing = null;
            using (BoardControl.Controller.DeferRefresh())
            {
                Items.Clear(); BoardControl.Columns.Clear(); BoardControl.Swimlanes.Clear();
                BoardControl.Columns.Add(new() { Key = "backlog", Header = "Backlog", Accent = "#8592AE", Width = 280 });
                BoardControl.Columns.Add(new() { Key = "doing", Header = "In progress", Accent = "#7367DE", MaximumWorkInProgress = count > 100 ? null : 6, Width = 280 });
                BoardControl.Columns.Add(new() { Key = "review", Header = "In review", Accent = "#DEA94D", MaximumWorkInProgress = count > 100 ? null : 4, Width = 280 });
                BoardControl.Columns.Add(new() { Key = "done", Header = "Done", Accent = "#52AA91", Width = 280 });
                string[] titles = ["Map the onboarding journey", "Refresh the component library", "Improve keyboard navigation", "Ship the activity timeline", "Explore saved board views", "Build accessible card actions", "Tune virtualized scrolling", "Add workspace preferences", "Review empty-state content", "Polish the card detail panel", "Verify browser drag and drop", "Document the adapter contract", "Release the new navigation", "Publish the design tokens", "Clean up loading states", "Update the getting-started guide"];
                string[] people = ["Maya Chen", "Alex Rivera", "Sam Taylor", "Jordan Lee"];
                string[] tags = ["Design", "Platform", "Accessibility", "Experience"];
                string[] keys = ["backlog", "doing", "review", "done"];
                for (int i = 0; i < count; i++)
                {
                    int col = count <= 100 ? Math.Min(3, i / 4) : i % 4;
                    var item = new WorkItem
                    {
                        Id = $"ATL-{101 + i}", Title = count > 100 ? $"{titles[i % titles.Length]} · {i + 1:N0}" : titles[i % titles.Length],
                        Description = i % 2 == 0 ? "Make the everyday workflow clearer, faster, and more inclusive." : "A focused improvement for the next product milestone.",
                        ColumnKey = keys[col], Order = count <= 100 ? i % 4 : i / 4,
                        Assignee = people[i % 4], Tags = tags[i % 4], Priority = i % 5 == 0 ? "High" : "Normal",
                        DueDate = new DateTimeOffset(2026, 10, 15 + i % 12, 12, 0, 0, TimeSpan.Zero),
                        TotalTasks = 5, CompletedTasks = col == 3 ? 5 : i % 5, IsLocked = i == 3,
                        Kind = i % 3 == 0 ? "DESIGN" : i % 3 == 1 ? "FEATURE" : "IMPROVEMENT"
                    };
                    Wire(item); Items.Add(item);
                }
                _nextId = 101 + count;
            }
            BoardControl.RefreshView(); UpdateMetrics();
        }
        finally { watch.Stop(); LastLoadMilliseconds = watch.Elapsed.TotalMilliseconds; _resetting = false; }
    }
    private void Wire(WorkItem item)
    {
        item.OpenCommand = new RelayCommand(_ => OpenEditor(item));
        item.NextCommand = new RelayCommand(_ => { BoardControl.SelectOnly(item); BoardControl.MoveSelection(1); }, _ => !BoardControl.IsReadOnly && !item.IsLocked);
        item.DeleteCommand = new RelayCommand(_ => DeleteCards([item]), _ => !BoardControl.IsReadOnly && !item.IsLocked);
        item.LockCommand = new RelayCommand(_ => { if (!BoardControl.IsReadOnly) item.IsLocked = !item.IsLocked; }, _ => !BoardControl.IsReadOnly);
    }
    public void SetSwimlanes(bool enabled)
    {
        using (BoardControl.Controller.DeferRefresh())
        {
            BoardControl.Swimlanes.Clear();
            if (enabled)
            {
                BoardControl.Swimlanes.Add(new() { Key = "product", Header = "Product experience" });
                BoardControl.Swimlanes.Add(new() { Key = "platform", Header = "Platform & quality" });
            }
            for (int i = 0; i < Items.Count; i++) Items[i].LaneKey = enabled ? (i % 2 == 0 ? "product" : "platform") : "";
        }
        BoardControl.LaneHeight = enabled ? 390 : 500;
        BoardControl.RefreshView();
    }
    public void SetCompact(bool compact) => BoardControl.Classes.Set("compact", compact);
    public void SetDark(bool dark) { if (Application.Current is { } app) app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; }
    public void AddCard(KanbanAddRequest? request = null)
    {
        if (BoardControl.IsReadOnly || BoardControl.Columns.Count == 0) return;
        string column = request?.ColumnKey ?? BoardControl.Columns[0].Key;
        string lane = request?.LaneKey ?? BoardControl.Swimlanes.FirstOrDefault()?.Key ?? "";
        var definition = BoardControl.Columns.First(c => c.Key == column);
        if (definition.IsReadOnly || BoardControl.Swimlanes.Any(l => l.Key == lane && l.IsReadOnly)) return;
        if (BoardControl.WipLimitMode == WipLimitMode.Enforce && definition.MaximumWorkInProgress is { } max && BoardControl.Controller.GetColumnCount(column) >= max) return;
        var item = new WorkItem { Id = $"ATL-{_nextId++}", Title = "Untitled card", ColumnKey = column, LaneKey = lane,
            Order = BoardControl.Controller.GetCell(column, lane).Select(e => e.Placement.Order).DefaultIfEmpty(-1).Max() + 1,
            Assignee = "Maya Chen", Tags = "Product", Kind = "FEATURE" };
        Wire(item); Items.Add(item); BoardControl.SelectOnly(item); OpenEditor(item);
    }
    private void DeleteCards(IEnumerable<WorkItem> cards)
    {
        var items = cards.ToArray();
        if (BoardControl.IsReadOnly || items.Any(i => i.IsLocked || BoardControl.Columns.Any(c => c.Key == i.ColumnKey && c.IsReadOnly) || BoardControl.Swimlanes.Any(l => l.Key == i.LaneKey && l.IsReadOnly))) return;
        using (BoardControl.Controller.DeferRefresh()) foreach (var item in items) Items.Remove(item);
        if (_editing is not null && !Items.Contains(_editing)) { C<Border>("Editor").IsVisible = false; _editing = null; }
    }
    public void OpenEditor(WorkItem item)
    {
        _editing = item; C<Border>("Editor").IsVisible = true;
        C<TextBlock>("EditorId").Text = item.Id;
        C<TextBox>("TitleEdit").Text = item.Title; C<TextBox>("DescriptionEdit").Text = item.Description;
        C<TextBox>("AssigneeEdit").Text = item.Assignee; C<TextBox>("TagsEdit").Text = item.Tags;
        C<TextBlock>("EditError").Text = "";
        C<Button>("SaveCardButton").IsEnabled = !BoardControl.IsReadOnly && !item.IsLocked;
    }
    private void SaveEditorClick(object? s, RoutedEventArgs e)
    {
        if (_editing is not { } item || BoardControl.IsReadOnly || item.IsLocked || !Items.Contains(item)) return;
        var title = C<TextBox>("TitleEdit").Text?.Trim();
        if (string.IsNullOrEmpty(title)) { C<TextBlock>("EditError").Text = "A title is required."; return; }
        using (BoardControl.Controller.DeferRefresh())
        { item.Title = title; item.Description = C<TextBox>("DescriptionEdit").Text ?? ""; item.Assignee = C<TextBox>("AssigneeEdit").Text ?? ""; item.Tags = C<TextBox>("TagsEdit").Text ?? ""; }
        C<Border>("Editor").IsVisible = false; _editing = null;
    }
    private void CloseEditorClick(object? s, RoutedEventArgs e) { C<Border>("Editor").IsVisible = false; _editing = null; }
    private void FilterChanged(object? s, RoutedEventArgs e)
    {
        if (!_initialized || _resetting) return;
        bool mine = C<CheckBox>("MineBox").IsChecked == true, locked = C<CheckBox>("LockedBox").IsChecked == true;
        BoardControl.Filter = o => o is WorkItem w && (!mine || w.Assignee == "Maya Chen") && (!locked || w.IsLocked);
    }
    private void SortChanged(object? s, SelectionChangedEventArgs e)
    {
        if (!_initialized || _resetting) return;
        BoardControl.SortComparer = C<ComboBox>("SortBox").SelectedIndex switch
        {
            1 => Comparer<object>.Create((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(((WorkItem)a).Title, ((WorkItem)b).Title)),
            2 => Comparer<object>.Create((a, b) => Nullable.Compare(((WorkItem)a).DueDate, ((WorkItem)b).DueDate)),
            _ => null
        };
    }
    private void LanesChanged(object? s, RoutedEventArgs e) { if (_initialized && !_resetting) SetSwimlanes(C<CheckBox>("LanesBox").IsChecked == true); }
    private void CompactChanged(object? s, RoutedEventArgs e) { if (_initialized) SetCompact(C<CheckBox>("CompactBox").IsChecked == true); }
    private void ThemeClick(object? s, RoutedEventArgs e) => SetDark(Application.Current?.RequestedThemeVariant != ThemeVariant.Dark);
    private void NewClick(object? s, RoutedEventArgs e) => AddCard();
    private void UndoClick(object? s, RoutedEventArgs e) => BoardControl.Undo();
    private void RedoClick(object? s, RoutedEventArgs e) => BoardControl.Redo();
    private void StressClick(object? s, RoutedEventArgs e) => Reset(10_000);
    private void ResetClick(object? s, RoutedEventArgs e) => Reset();
    private void UpdateMetrics() { if (_initialized) C<TextBlock>("Metrics").Text = $"{Items.Count:N0} cards  ·  {BoardControl.SelectedItems.Count} selected"; }
    public string ExportJson() => JsonSerializer.Serialize(new BoardDocument(1, BoardControl.Swimlanes.Count > 0,
        Items.Select(w => new CardDocument(w.Id, w.Title, w.Description, w.ColumnKey, w.LaneKey, w.Order, w.Assignee, w.Tags, w.Priority, w.IsLocked, w.DueDate, w.CompletedTasks, w.TotalTasks, w.Kind)).ToArray()), BoardJsonContext.Default.BoardDocument);
    public void ImportJson(string json)
    {
        if (BoardControl.IsReadOnly) throw new InvalidOperationException("Board is read-only.");
        if (json.Length > 16 * 1024 * 1024) throw new ArgumentException("Import exceeds the 16 MiB limit.");
        var document = JsonSerializer.Deserialize(json, BoardJsonContext.Default.BoardDocument) ?? throw new ArgumentException("Empty document.");
        if (document.SchemaVersion != 1 || document.Cards is null || document.Cards.Length > 50_000) throw new ArgumentException("Unsupported schema or too many cards.");
        if (document.Cards.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Title) || !double.IsFinite(c.Order) || c.CompletedTasks < 0 || c.TotalTasks < 0) || document.Cards.Select(c => c.Id).Distinct().Count() != document.Cards.Length)
            throw new ArgumentException("Invalid card data or duplicate IDs.");
        var keys = new[] { "backlog", "doing", "review", "done" };
        if (document.Cards.Any(c => !keys.Contains(c.ColumnKey) || (document.Swimlanes ? c.LaneKey is not ("product" or "platform") : c.LaneKey != ""))) throw new ArgumentException("Unknown column or swimlane.");
        // Construct and validate everything before replacing the live source.
        var incoming = document.Cards.Select(c => new WorkItem { Id = c.Id, Title = c.Title, Description = c.Description, ColumnKey = c.ColumnKey, LaneKey = c.LaneKey, Order = c.Order,
            Assignee = c.Assignee, Tags = c.Tags, Priority = c.Priority, IsLocked = c.IsLocked, DueDate = c.DueDate, CompletedTasks = c.CompletedTasks, TotalTasks = c.TotalTasks, Kind = c.Kind }).ToArray();
        using (BoardControl.Controller.DeferRefresh())
        {
            Items.Clear(); BoardControl.Swimlanes.Clear();
            if (document.Swimlanes) { BoardControl.Swimlanes.Add(new() { Key = "product", Header = "Product experience" }); BoardControl.Swimlanes.Add(new() { Key = "platform", Header = "Platform & quality" }); }
            foreach (var card in incoming) { Wire(card); Items.Add(card); }
        }
        _nextId = Items.Select(i => int.TryParse(i.Id.Replace("ATL-", ""), out int id) ? id + 1 : 100).DefaultIfEmpty(100).Max();
        _resetting = true; try { C<CheckBox>("LanesBox").IsChecked = document.Swimlanes; } finally { _resetting = false; }
        C<Border>("Editor").IsVisible = false; _editing = null;
        BoardControl.RefreshView(); UpdateMetrics();
    }
    private void ExportClick(object? s, RoutedEventArgs e) { C<TextBox>("JsonText").Text = ExportJson(); C<Border>("JsonPanel").IsVisible = true; C<TextBlock>("JsonError").Text = ""; }
    private void ImportClick(object? s, RoutedEventArgs e) { C<TextBox>("JsonText").Text = ""; C<Border>("JsonPanel").IsVisible = true; C<TextBlock>("JsonError").Text = "Paste an exported board, then apply import."; }
    private void ApplyImportClick(object? s, RoutedEventArgs e)
    { try { ImportJson(C<TextBox>("JsonText").Text ?? ""); C<Border>("JsonPanel").IsVisible = false; } catch (Exception ex) { C<TextBlock>("JsonError").Text = ex.Message; } }
    private void CloseJsonClick(object? s, RoutedEventArgs e) => C<Border>("JsonPanel").IsVisible = false;
}
