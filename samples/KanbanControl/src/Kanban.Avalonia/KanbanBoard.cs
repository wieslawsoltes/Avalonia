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
public partial class KanbanBoard : TemplatedControl
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
    private bool _isDragging = false;
    public static readonly DirectProperty<KanbanBoard, bool> IsDraggingProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, bool>(nameof(IsDragging), o => o.IsDragging);
    public bool IsDragging { get => _isDragging; private set => SetAndRaise(IsDraggingProperty, ref _isDragging, value); }
    private string _dragLabel = "";
    public static readonly DirectProperty<KanbanBoard, string> DragLabelProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, string>(nameof(DragLabel), o => o.DragLabel);
    public string DragLabel { get => _dragLabel; private set => SetAndRaise(DragLabelProperty, ref _dragLabel, value); }
    private string _statusText = "Ready";
    public static readonly DirectProperty<KanbanBoard, string> StatusTextProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, string>(nameof(StatusText), o => o.StatusText);
    public string StatusText { get => _statusText; private set => SetAndRaise(StatusTextProperty, ref _statusText, value); }
    private int _visibleCount = 0;
    public static readonly DirectProperty<KanbanBoard, int> VisibleCountProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, int>(nameof(VisibleCount), o => o.VisibleCount);
    public int VisibleCount { get => _visibleCount; private set => SetAndRaise(VisibleCountProperty, ref _visibleCount, value); }
    private bool _canUndo = false;
    public static readonly DirectProperty<KanbanBoard, bool> CanUndoProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, bool>(nameof(CanUndo), o => o.CanUndo);
    public bool CanUndo { get => _canUndo; private set => SetAndRaise(CanUndoProperty, ref _canUndo, value); }
    private bool _canRedo = false;
    public static readonly DirectProperty<KanbanBoard, bool> CanRedoProperty = AvaloniaProperty.RegisterDirect<KanbanBoard, bool>(nameof(CanRedo), o => o.CanRedo);
    public bool CanRedo { get => _canRedo; private set => SetAndRaise(CanRedoProperty, ref _canRedo, value); }

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
}
