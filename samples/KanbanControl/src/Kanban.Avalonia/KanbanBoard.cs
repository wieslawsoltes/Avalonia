using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kanban.Core;

namespace Kanban.Avalonia;

/// <summary>Fully templated, data-model-independent board. Mutate the source and its models on the UI thread.</summary>
[TemplatePart("PART_ScrollViewer", typeof(ScrollViewer))]
[TemplatePart("PART_Rows", typeof(ItemsControl))]
[TemplatePart("PART_DragGhost", typeof(Control))]
[PseudoClasses(":dragging", ":readonly", ":empty", ":invalid")]
public class KanbanBoard : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<KanbanBoard, IEnumerable?>(nameof(ItemsSource), null);
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public static readonly StyledProperty<IKanbanAdapter> ItemAdapterProperty = AvaloniaProperty.Register<KanbanBoard, IKanbanAdapter>(nameof(ItemAdapter), DefaultKanbanAdapter.Instance, validate: v => v is not null);
    public IKanbanAdapter ItemAdapter { get => GetValue(ItemAdapterProperty); set => SetValue(ItemAdapterProperty, value); }
    public static readonly StyledProperty<IDataTemplate?> CardTemplateProperty = AvaloniaProperty.Register<KanbanBoard, IDataTemplate?>(nameof(CardTemplate), null);
    public IDataTemplate? CardTemplate { get => GetValue(CardTemplateProperty); set => SetValue(CardTemplateProperty, value); }
    public static readonly StyledProperty<IDataTemplate?> ColumnHeaderTemplateProperty = AvaloniaProperty.Register<KanbanBoard, IDataTemplate?>(nameof(ColumnHeaderTemplate), null);
    public IDataTemplate? ColumnHeaderTemplate { get => GetValue(ColumnHeaderTemplateProperty); set => SetValue(ColumnHeaderTemplateProperty, value); }
    public static readonly StyledProperty<IDataTemplate?> ColumnFooterTemplateProperty = AvaloniaProperty.Register<KanbanBoard, IDataTemplate?>(nameof(ColumnFooterTemplate), null);
    public IDataTemplate? ColumnFooterTemplate { get => GetValue(ColumnFooterTemplateProperty); set => SetValue(ColumnFooterTemplateProperty, value); }
    public static readonly StyledProperty<IDataTemplate?> LaneHeaderTemplateProperty = AvaloniaProperty.Register<KanbanBoard, IDataTemplate?>(nameof(LaneHeaderTemplate), null);
    public IDataTemplate? LaneHeaderTemplate { get => GetValue(LaneHeaderTemplateProperty); set => SetValue(LaneHeaderTemplateProperty, value); }
    public static readonly StyledProperty<IDataTemplate?> EmptyTemplateProperty = AvaloniaProperty.Register<KanbanBoard, IDataTemplate?>(nameof(EmptyTemplate), null);
    public IDataTemplate? EmptyTemplate { get => GetValue(EmptyTemplateProperty); set => SetValue(EmptyTemplateProperty, value); }
    public static readonly StyledProperty<ControlTheme?> CardThemeProperty = AvaloniaProperty.Register<KanbanBoard, ControlTheme?>(nameof(CardTheme), null);
    public ControlTheme? CardTheme { get => GetValue(CardThemeProperty); set => SetValue(CardThemeProperty, value); }
    public static readonly StyledProperty<ControlTheme?> ColumnThemeProperty = AvaloniaProperty.Register<KanbanBoard, ControlTheme?>(nameof(ColumnTheme), null);
    public ControlTheme? ColumnTheme { get => GetValue(ColumnThemeProperty); set => SetValue(ColumnThemeProperty, value); }
    public static readonly StyledProperty<ControlTheme?> SwimlaneThemeProperty = AvaloniaProperty.Register<KanbanBoard, ControlTheme?>(nameof(SwimlaneTheme), null);
    public ControlTheme? SwimlaneTheme { get => GetValue(SwimlaneThemeProperty); set => SetValue(SwimlaneThemeProperty, value); }
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<KanbanBoard, object?>(nameof(Header), null);
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<KanbanBoard, object?>(nameof(Footer), null);
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public static readonly StyledProperty<string> SearchTextProperty = AvaloniaProperty.Register<KanbanBoard, string>(nameof(SearchText), "");
    public string SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public static readonly StyledProperty<Predicate<object>?> FilterProperty = AvaloniaProperty.Register<KanbanBoard, Predicate<object>?>(nameof(Filter), null);
    public Predicate<object>? Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    public static readonly StyledProperty<IComparer<object>?> SortComparerProperty = AvaloniaProperty.Register<KanbanBoard, IComparer<object>?>(nameof(SortComparer), null);
    public IComparer<object>? SortComparer { get => GetValue(SortComparerProperty); set => SetValue(SortComparerProperty, value); }
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(IsReadOnly), false);
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public static readonly StyledProperty<bool> CanDragCardsProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(CanDragCards), true);
    public bool CanDragCards { get => GetValue(CanDragCardsProperty); set => SetValue(CanDragCardsProperty, value); }
    public static readonly StyledProperty<bool> CanResizeColumnsProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(CanResizeColumns), true);
    public bool CanResizeColumns { get => GetValue(CanResizeColumnsProperty); set => SetValue(CanResizeColumnsProperty, value); }
    public static readonly StyledProperty<bool> AllowLaneChangeProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(AllowLaneChange), true);
    public bool AllowLaneChange { get => GetValue(AllowLaneChangeProperty); set => SetValue(AllowLaneChangeProperty, value); }
    public static readonly StyledProperty<WipLimitMode> WipLimitModeProperty = AvaloniaProperty.Register<KanbanBoard, WipLimitMode>(nameof(WipLimitMode), WipLimitMode.Enforce);
    public WipLimitMode WipLimitMode { get => GetValue(WipLimitModeProperty); set => SetValue(WipLimitModeProperty, value); }
    public static readonly StyledProperty<double> LaneHeightProperty = AvaloniaProperty.Register<KanbanBoard, double>(nameof(LaneHeight), 520d, validate: v => double.IsFinite(v) && v >= 160 && v <= 4000);
    public double LaneHeight { get => GetValue(LaneHeightProperty); set => SetValue(LaneHeightProperty, value); }
    public static readonly StyledProperty<object?> SelectedItemProperty = AvaloniaProperty.Register<KanbanBoard, object?>(nameof(SelectedItem), null, defaultBindingMode: BindingMode.TwoWay);
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public static readonly StyledProperty<ICommand?> AddCardCommandProperty = AvaloniaProperty.Register<KanbanBoard, ICommand?>(nameof(AddCardCommand), null);
    public ICommand? AddCardCommand { get => GetValue(AddCardCommandProperty); set => SetValue(AddCardCommandProperty, value); }
    public static readonly StyledProperty<ICommand?> CardInvokedCommandProperty = AvaloniaProperty.Register<KanbanBoard, ICommand?>(nameof(CardInvokedCommand), null);
    public ICommand? CardInvokedCommand { get => GetValue(CardInvokedCommandProperty); set => SetValue(CardInvokedCommandProperty, value); }
    public static readonly StyledProperty<ICommand?> DeleteCardsCommandProperty = AvaloniaProperty.Register<KanbanBoard, ICommand?>(nameof(DeleteCardsCommand), null);
    public ICommand? DeleteCardsCommand { get => GetValue(DeleteCardsCommandProperty); set => SetValue(DeleteCardsCommandProperty, value); }
    public static readonly StyledProperty<ICommand?> CardsMovedCommandProperty = AvaloniaProperty.Register<KanbanBoard, ICommand?>(nameof(CardsMovedCommand), null);
    public ICommand? CardsMovedCommand { get => GetValue(CardsMovedCommandProperty); set => SetValue(CardsMovedCommandProperty, value); }
    public static readonly StyledProperty<bool> IsDraggingProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(IsDragging), false);
    public bool IsDragging { get => GetValue(IsDraggingProperty); private set => SetValue(IsDraggingProperty, value); }
    public static readonly StyledProperty<string> DragLabelProperty = AvaloniaProperty.Register<KanbanBoard, string>(nameof(DragLabel), "");
    public string DragLabel { get => GetValue(DragLabelProperty); private set => SetValue(DragLabelProperty, value); }
    public static readonly StyledProperty<string> StatusTextProperty = AvaloniaProperty.Register<KanbanBoard, string>(nameof(StatusText), "Ready");
    public string StatusText { get => GetValue(StatusTextProperty); private set => SetValue(StatusTextProperty, value); }
    public static readonly StyledProperty<int> VisibleCountProperty = AvaloniaProperty.Register<KanbanBoard, int>(nameof(VisibleCount), 0);
    public int VisibleCount { get => GetValue(VisibleCountProperty); private set => SetValue(VisibleCountProperty, value); }
    public static readonly StyledProperty<bool> CanUndoProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(CanUndo), false);
    public bool CanUndo { get => GetValue(CanUndoProperty); private set => SetValue(CanUndoProperty, value); }
    public static readonly StyledProperty<bool> CanRedoProperty = AvaloniaProperty.Register<KanbanBoard, bool>(nameof(CanRedo), false);
    public bool CanRedo { get => GetValue(CanRedoProperty); private set => SetValue(CanRedoProperty, value); }

    public ObservableCollection<KanbanColumnDefinition> Columns { get; } = new();
    public ObservableCollection<KanbanLaneDefinition> Swimlanes { get; } = new();
    public ObservableCollection<KanbanLaneView> Rows { get; } = new();
    private readonly ObservableCollection<object> _selected = new();
    public ReadOnlyObservableCollection<object> SelectedItems { get; }
    public KanbanController Controller { get; private set; } = new();
    public event EventHandler<KanbanMovingEventArgs>? CardMoving;
    public event EventHandler<KanbanMovedEventArgs>? CardsMoved;
    public event EventHandler<KanbanMoveResult>? MoveRejected;
    public event EventHandler? BoardSelectionChanged;
    public event EventHandler<object>? CardInvoked;
    public event EventHandler<KanbanAddRequest>? AddCardRequested;

    private readonly KanbanLaneDefinition _defaultLane = new() { Key = "", Header = "All work" };
    private readonly DispatcherTimer _scrollTimer;
    private bool _ready, _disposed, _queued, _syncSelection, _resettingDrag;
    private ScrollViewer? _scroll;
    private Control? _ghost;
    private KanbanCard? _pressedCard, _anchorCard;
    private object? _pressedItem;
    private Point _pressPoint, _pointerPoint;
    private IPointer? _pointer;
    private string[] _dragIds = [];
    private long _dragRevision;
    private KanbanColumnView? _dropCell;
    private KanbanMoveRequest? _dropRequest;
    private readonly IDataTemplate _fallbackCard;

    public KanbanBoard()
    {
        SelectedItems = new(_selected);
        _fallbackCard = new FuncDataTemplate<object>((item, _) => new TextBlock
        { Text = ItemAdapter.GetTitle(item), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(12) });
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(24) };
        _scrollTimer.Tick += (_, _) => AutoScroll();
        AddHandler(PointerMovedEvent, PointerMovedHandler, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, PointerReleasedHandler, RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, CaptureLostHandler, RoutingStrategies.Bubble, true);
        AddHandler(KeyDownEvent, KeyDownHandler, RoutingStrategies.Tunnel, true);
        AddHandler(DoubleTappedEvent, DoubleTappedHandler, RoutingStrategies.Bubble, true);
        _ready = true; WireController(); Rebind();
    }
    public IDataTemplate EffectiveCardTemplate => CardTemplate ?? _fallbackCard;
    private void WireController()
    {
        Controller.Changed += ControllerChanged;
        Controller.Moving += (_, e) => CardMoving?.Invoke(this, e);
        Controller.Moved += (_, e) => { CardsMoved?.Invoke(this, e); Execute(CardsMovedCommand, e); };
    }
    private void Rebind()
    {
        if (!_ready || _disposed) return;
        CancelDrag(); SyncOptions(); Controller.Bind(ItemsSource, Columns, Swimlanes, ItemAdapter);
    }
    private void SyncOptions()
    { Controller.IsReadOnly = IsReadOnly; Controller.WipLimitMode = WipLimitMode; Controller.AllowLaneChange = AllowLaneChange; }
    private void ControllerChanged(object? sender, EventArgs e) => QueueProjection();
    private void QueueProjection()
    {
        if (_queued || _disposed) return; _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; if (!_disposed) RefreshView(); }, DispatcherPriority.Background);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change); if (!_ready) return;
        if (change.Property == ItemsSourceProperty || change.Property == ItemAdapterProperty) Rebind();
        else if (change.Property == SelectedItemProperty && !_syncSelection) SelectOnly(SelectedItem);
        else if (change.Property == IsReadOnlyProperty || change.Property == WipLimitModeProperty || change.Property == AllowLaneChangeProperty)
        { SyncOptions(); if (IsReadOnly) CancelDrag(); QueueProjection(); }
        else if (change.Property == SearchTextProperty || change.Property == FilterProperty || change.Property == SortComparerProperty ||
                 change.Property == LaneHeightProperty || change.Property == AddCardCommandProperty || change.Property == CardTemplateProperty)
            QueueProjection();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed) { _disposed = false; Controller = new(); WireController(); Rebind(); }
        QueueProjection();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { CancelDrag(); Controller.Dispose(); _disposed = true; base.OnDetachedFromVisualTree(e); }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        CancelDrag(); base.OnApplyTemplate(e);
        _scroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        _ghost = e.NameScope.Find<Control>("PART_DragGhost");
        QueueProjection();
    }
    /// <summary>Re-projects the indexed source. Call Controller.Refresh() first for non-notifying models.</summary>
    public void RefreshView()
    {
        Dispatcher.UIThread.VerifyAccess(); if (_disposed) return;
        var lanes = Swimlanes.Count == 0 ? new[] { _defaultLane } : Swimlanes.ToArray();
        var selection = _selected.ToArray(); _syncSelection = true;
        try
        {
            bool rebuild = Rows.Count != lanes.Length || Rows.Where((r, i) => !ReferenceEquals(r.Definition, lanes[i]) ||
                r.Columns.Count != Columns.Count || r.Columns.Where((c, j) => !ReferenceEquals(c.Definition, Columns[j])).Any()).Any();
            if (rebuild)
            {
                Rows.Clear(); foreach (var lane in lanes)
                { var row = new KanbanLaneView(this, lane); foreach (var col in Columns) row.Columns.Add(new(this, col, lane)); Rows.Add(row); }
            }
            int visible = 0;
            foreach (var row in Rows)
            {
                foreach (var cell in row.Columns)
                {
                    var all = Controller.GetCell(cell.Definition.Key, row.Definition.Key);
                    var items = all.Where(e => (string.IsNullOrWhiteSpace(SearchText) || ItemAdapter.GetSearchText(e.Item).Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                        (Filter?.Invoke(e.Item) ?? true)).Select(e => e.Item).ToList();
                    if (SortComparer is not null) items.Sort(SortComparer);
                    cell.Items.ReplaceWith(items); visible += items.Count;
                    cell.TotalCount = all.Count; cell.ColumnCount = Controller.GetColumnCount(cell.Definition.Key); cell.RefreshLabels();
                }
                row.RefreshLabels();
            }
            VisibleCount = visible;
            var current = Controller.Entries.Select(e => e.Item).ToHashSet(ReferenceEqualityComparer.Instance);
            _selected.Clear(); foreach (var item in selection.Where(current.Contains)) _selected.Add(item);
            SetCurrentValue(SelectedItemProperty, _selected.FirstOrDefault());
            CanUndo = Controller.CanUndo; CanRedo = Controller.CanRedo;
            PseudoClasses.Set(":empty", visible == 0); PseudoClasses.Set(":invalid", Controller.Diagnostics.Count > 0); PseudoClasses.Set(":readonly", IsReadOnly);
            if (Controller.Diagnostics.Count > 0) StatusText = Controller.Diagnostics[0];
        }
        finally { _syncSelection = false; }
        Dispatcher.UIThread.Post(SyncRealizedSelection, DispatcherPriority.Loaded);
    }
    private IEnumerable<KanbanColumn> RealizedColumns() => this.GetVisualDescendants().OfType<KanbanColumn>();
    private IEnumerable<KanbanListBox> Lists() => RealizedColumns().Select(c => c.ItemsList).OfType<KanbanListBox>();
    private void SyncRealizedSelection()
    {
        if (_disposed) return;
        _syncSelection = true;
        try
        {
            foreach (var list in Lists())
            {
                if (list.SelectedItems is not { } selected) continue;
                var desired = list.View?.Items.Where(i => _selected.Contains(i)).ToArray() ?? [];
                foreach (var item in selected.Cast<object>().Where(i => !desired.Contains(i)).ToArray()) selected.Remove(item);
                foreach (var item in desired) if (!selected.Contains(item)) selected.Add(item);
                if (list.ItemTemplate != EffectiveCardTemplate) list.ItemTemplate = EffectiveCardTemplate;
            }
        }
        finally { _syncSelection = false; }
    }
    public void SelectOnly(object? item)
    {
        _selected.Clear(); if (item is not null && Controller.Entries.Any(e => ReferenceEquals(e.Item, item))) _selected.Add(item);
        SelectionChanged(); SyncRealizedSelection();
    }
    public void SelectAllVisible()
    {
        _selected.Clear(); foreach (var item in Rows.Where(r => r.IsExpanded).SelectMany(r => r.Columns).Where(c => c.IsExpanded).SelectMany(c => c.Items)) _selected.Add(item);
        SelectionChanged(); SyncRealizedSelection();
    }
    internal void ClearOtherSelections(KanbanListBox keep)
    {
        if (_syncSelection) return;
        foreach (var item in _selected.Where(i => keep.View?.Items.Contains(i) != true).ToArray()) _selected.Remove(item);
        SyncRealizedSelection(); SelectionChanged();
    }
    internal void OnCellSelectionChanged(KanbanListBox list, SelectionChangedEventArgs e)
    {
        if (_syncSelection) return;
        foreach (var item in e.RemovedItems.Cast<object>()) _selected.Remove(item);
        foreach (var item in e.AddedItems.Cast<object>()) if (!_selected.Contains(item)) _selected.Add(item);
        SelectionChanged();
    }
    private void SelectionChanged()
    {
        _syncSelection = true; try { SetCurrentValue(SelectedItemProperty, _selected.FirstOrDefault()); } finally { _syncSelection = false; }
        BoardSelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void RequestAdd(KanbanColumnView cell)
    {
        if (!cell.CanAdd) return;
        var request = new KanbanAddRequest(cell.Definition.Key, cell.Lane.Key);
        AddCardRequested?.Invoke(this, request); Execute(AddCardCommand, request);
    }
    public void InvokeCard(object item) { CardInvoked?.Invoke(this, item); Execute(CardInvokedCommand, item); }
    private static void Execute(ICommand? command, object? argument) { if (command?.CanExecute(argument) == true) command.Execute(argument); }
    private KanbanMoveResult Report(KanbanMoveResult result)
    { StatusText = result.Message; if (!result.Succeeded) MoveRejected?.Invoke(this, result); CanUndo = Controller.CanUndo; CanRedo = Controller.CanRedo; return result; }
    public KanbanMoveResult Move(KanbanMoveRequest request)
    {
        SyncOptions();
        if (SortComparer is not null && request.BeforeId is not null)
            return Report(KanbanMoveResult.Reject(KanbanMoveFailure.Policy, "Manual ordering is unavailable while sorting."));
        return Report(Controller.TryMove(request));
    }
    public KanbanMoveResult Undo() { CancelDrag(); SyncOptions(); return Report(Controller.Undo()); }
    public KanbanMoveResult Redo() { CancelDrag(); SyncOptions(); return Report(Controller.Redo()); }
    public KanbanMoveResult MoveSelection(int columnDelta, int orderDelta = 0, int laneDelta = 0)
    {
        var entries = _selected.Select(i => Controller.Find(ItemAdapter.GetId(i))).OfType<KanbanEntry>().ToArray();
        if (entries.Length == 0) return Report(KanbanMoveResult.Reject(KanbanMoveFailure.EmptySelection, "Select a card first."));
        var first = entries[0];
        int ci = Columns.ToList().FindIndex(c => c.Key == first.Placement.ColumnKey);
        var lanes = Swimlanes.Count == 0 ? new[] { _defaultLane } : Swimlanes.ToArray();
        int li = Array.FindIndex(lanes, l => l.Key == first.Placement.LaneKey);
        if (ci + columnDelta < 0 || ci + columnDelta >= Columns.Count || li + laneDelta < 0 || li + laneDelta >= lanes.Length)
            return Report(KanbanMoveResult.Reject(KanbanMoveFailure.UnknownTarget, "Already at the board edge."));
        string column = Columns[ci + columnDelta].Key, lane = lanes[li + laneDelta].Key;
        string[] ids = entries.OrderBy(e => e.Placement.Order).Select(e => e.Id).ToArray();
        string? anchor = null;
        if (orderDelta != 0)
        {
            if (SortComparer is not null) return Report(KanbanMoveResult.Reject(KanbanMoveFailure.Policy, "Clear sorting before reordering."));
            if (entries.Any(e => e.Placement.ColumnKey != column || e.Placement.LaneKey != lane))
                return Report(KanbanMoveResult.Reject(KanbanMoveFailure.Policy, "Reorder cards within a single cell."));
            var full = Controller.GetCell(column, lane).ToArray();
            int firstAt = Array.FindIndex(full, e => ids.Contains(e.Id));
            int remainingBefore = full.Take(firstAt).Count(e => !ids.Contains(e.Id));
            var rest = full.Where(e => !ids.Contains(e.Id)).ToArray();
            int at = Math.Clamp(remainingBefore + Math.Sign(orderDelta), 0, rest.Length);
            anchor = at < rest.Length ? rest[at].Id : null;
        }
        var result = Move(new(ids, column, lane, anchor));
        if (result.Succeeded) Dispatcher.UIThread.Post(() => FocusItem(entries[0].Item), DispatcherPriority.Background);
        return result;
    }
    private void FocusItem(object item)
    {
        foreach (var list in Lists()) if (list.View?.Items.Contains(item) == true)
        { list.ScrollIntoView(item); Dispatcher.UIThread.Post(() => (list.ContainerFromItem(item) as Control)?.Focus(), DispatcherPriority.Loaded); break; }
    }
    private static bool EmbeddedEditor(object? source)
    {
        var visual = source as Visual;
        return visual is TextBox or Button or ComboBox || visual?.GetVisualAncestors().TakeWhile(v => v is not KanbanCard).Any(v => v is TextBox or Button or ComboBox) == true;
    }
    private void DoubleTappedHandler(object? sender, TappedEventArgs e)
    {
        if (EmbeddedEditor(e.Source)) return;
        var card = CardFrom(e.Source); if (card?.Content is { } item) { InvokeCard(item); e.Handled = true; }
    }
    private static KanbanCard? CardFrom(object? source) => source as KanbanCard ?? (source as Visual)?.GetVisualAncestors().OfType<KanbanCard>().FirstOrDefault();
    private void KeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_pressedCard is not null || IsDragging)) { CancelDrag(); e.Handled = true; return; }
        if (EmbeddedEditor(e.Source)) return;
        bool primary = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (primary && e.Key == Key.A) { SelectAllVisible(); e.Handled = true; }
        else if (primary && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; }
        else if (primary && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (e.Key is Key.Left or Key.Right) MoveSelection(e.Key == Key.Left ? -1 : 1);
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) MoveSelection(0, 0, e.Key == Key.Up ? -1 : 1);
            else MoveSelection(0, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _selected.FirstOrDefault() is { } item) { InvokeCard(item); e.Handled = true; }
        else if (e.Key == Key.Delete && !IsReadOnly && _selected.Count > 0 && _selected.All(ItemAdapter.CanMove))
        { Execute(DeleteCardsCommand, _selected.ToArray()); e.Handled = true; }
    }

    internal void PrepareDrag(KanbanCard card, KanbanColumnView cell, object item, PointerPressedEventArgs e)
    {
        if (!CanDragCards || IsReadOnly || !ItemAdapter.CanMove(item) || EmbeddedEditor(e.Source)) return;
        CancelDrag(); _pressedCard = card; _pressedItem = item; _pointer = e.Pointer;
        _pressPoint = _pointerPoint = e.GetPosition(this);
        // Touch scroll remains native. A deliberate long press is required to begin a touch drag.
        _touchPressedAt = DateTime.UtcNow; _isTouch = e.Pointer.Type == PointerType.Touch;
    }
    private DateTime _touchPressedAt;
    private bool _isTouch;
    private void PointerMovedHandler(object? sender, PointerEventArgs e)
    {
        if (_pressedCard is null || e.Pointer != _pointer) return;
        _pointerPoint = e.GetPosition(this);
        if (!IsDragging)
        {
            var distance = (_pointerPoint - _pressPoint).Length;
            if (distance < 6) return;
            if (_isTouch && (DateTime.UtcNow - _touchPressedAt).TotalMilliseconds < 350) { CancelDrag(); return; }
            if (_pressedItem is null || !_selected.Contains(_pressedItem)) SelectOnly(_pressedItem);
            _dragIds = _selected.Select(ItemAdapter.GetId).ToArray(); _dragRevision = Controller.Revision;
            IsDragging = true; DragLabel = $"{_dragIds.Length} card{(_dragIds.Length == 1 ? "" : "s")}";
            PseudoClasses.Set(":dragging", true); _pointer?.Capture(this); _scrollTimer.Start();
            foreach (var c in this.GetVisualDescendants().OfType<KanbanCard>()) if (c.Content is { } i) c.SetDragging(_selected.Contains(i));
        }
        UpdateDrop(); e.Handled = true;
    }
    private void UpdateDrop()
    {
        if (!IsDragging) return;
        if (_ghost is not null) { Canvas.SetLeft(_ghost, Math.Clamp(_pointerPoint.X + 14, 0, Math.Max(0, Bounds.Width - 180))); Canvas.SetTop(_ghost, Math.Clamp(_pointerPoint.Y + 14, 0, Math.Max(0, Bounds.Height - 50))); }
        _anchorCard?.SetDropBefore(false); _anchorCard = null;
        if (_dropCell is not null) _dropCell.IsDropTarget = false;
        _dropCell = null; _dropRequest = null;
        foreach (var column in RealizedColumns())
        {
            if (column.View is not { } view || !view.IsExpanded || column.TranslatePoint(default, this) is not { } origin ||
                !new Rect(origin, column.Bounds.Size).Contains(_pointerPoint)) continue;
            if (column.ItemsList is not { } list) continue;
            _dropCell = view;
            string? before = null;
            foreach (var card in list.GetVisualDescendants().OfType<KanbanCard>().OrderBy(c => c.TranslatePoint(default, this)?.Y ?? 0))
            {
                if (card.Content is not { } item || _dragIds.Contains(ItemAdapter.GetId(item)) || card.TranslatePoint(default, this) is not { } point) continue;
                if (_pointerPoint.Y < point.Y + card.Bounds.Height / 2) { before = ItemAdapter.GetId(item); _anchorCard = card; break; }
            }
            _dropRequest = new(_dragIds, view.Definition.Key, view.Lane.Key, SortComparer is null ? before : null, _dragRevision);
            SyncOptions(); var valid = Controller.Validate(_dropRequest);
            view.IsDropTarget = true; view.IsDropAllowed = valid.Succeeded; view.DropText = valid.Succeeded ? "Release to move" : valid.Message;
            if (valid.Succeeded) _anchorCard?.SetDropBefore(true);
            StatusText = view.DropText; break;
        }
    }
    private void AutoScroll()
    {
        if (!IsDragging) return;
        static double Velocity(double p, double size) => p < 40 ? -18 : p > size - 40 ? 18 : 0;
        if (_scroll is not null) _scroll.Offset = new(Math.Clamp(_scroll.Offset.X + Velocity(_pointerPoint.X, Bounds.Width), 0, Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)),
            Math.Clamp(_scroll.Offset.Y + Velocity(_pointerPoint.Y, Bounds.Height), 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
        var column = RealizedColumns().FirstOrDefault(c => ReferenceEquals(c.View, _dropCell));
        var sv = column?.ItemsList?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv is not null && sv.TranslatePoint(default, this) is { } origin)
            sv.Offset = new(sv.Offset.X, Math.Clamp(sv.Offset.Y + Velocity(_pointerPoint.Y - origin.Y, sv.Bounds.Height), 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height)));
        UpdateDrop();
    }
    private void PointerReleasedHandler(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointer != e.Pointer) return;
        bool dragging = IsDragging; var request = _dropRequest; CancelDrag();
        if (dragging) { if (request is not null) Move(request); else StatusText = "Move canceled: no destination"; e.Handled = true; }
    }
    private void CaptureLostHandler(object? sender, PointerCaptureLostEventArgs e) { if (!_resettingDrag && IsDragging) CancelDrag(); }
    public void CancelDrag()
    {
        if (_resettingDrag) return; _resettingDrag = true;
        try
        {
            _scrollTimer?.Stop(); _anchorCard?.SetDropBefore(false); _anchorCard = null;
            if (_dropCell is not null) _dropCell.IsDropTarget = false;
            foreach (var card in this.GetVisualDescendants().OfType<KanbanCard>()) card.SetDragging(false);
            if (_pointer?.Captured == this) _pointer.Capture(null);
            _pointer = null; _pressedCard = null; _pressedItem = null; _dropCell = null; _dropRequest = null; _dragIds = [];
            IsDragging = false; PseudoClasses.Set(":dragging", false);
        }
        finally { _resettingDrag = false; }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new KanbanGroupPeer(this, () => "Kanban board");
}
