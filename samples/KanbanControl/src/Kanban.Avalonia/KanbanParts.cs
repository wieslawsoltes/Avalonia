using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Kanban.Avalonia;

[PseudoClasses(":dragging", ":drop-before")]
public class KanbanCard : ListBoxItem
{
    internal void SetDragging(bool value) => PseudoClasses.Set(":dragging", value);
    internal void SetDropBefore(bool value) => PseudoClasses.Set(":drop-before", value);
    protected override Type StyleKeyOverride => typeof(KanbanCard);
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var board = this.FindAncestorOfType<KanbanBoard>();
        var column = this.FindAncestorOfType<KanbanColumn>();
        bool selectedGroup = IsSelected && board?.SelectedItems.Count > 1 && e.KeyModifiers == KeyModifiers.None;
        if (!selectedGroup) base.OnPointerPressed(e);
        else { Focus(); e.Handled = true; }
        if (column?.View is { } view && Content is { } item && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            board?.PrepareDrag(this, view, item, e);
    }
}

public class KanbanListBox : ListBox
{
    public KanbanListBox() { SelectionChanged += (_, e) => View?.Board.OnCellSelectionChanged(this, e); }
    public KanbanColumnView? View { get; set; }
    protected override Type StyleKeyOverride => typeof(ListBox);
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new KanbanCard();
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) => NeedsContainer<KanbanCard>(item, out recycleKey);
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is KanbanCard card && item is not null && View is { } view)
        {
            AutomationProperties.SetName(card, view.Board.ItemAdapter.GetTitle(item));
            AutomationProperties.SetAutomationId(card, "card-" + view.Board.ItemAdapter.GetId(item));
            AutomationProperties.SetHelpText(card, $"{view.Definition.Header}, {view.Lane.Header}. Alt+Left/Right moves between columns. Alt+Up/Down reorders. Enter opens card.");
            card.SetDragging(false); card.SetDropBefore(false);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && View is { } view)
        {
            var card = e.Source as KanbanCard ?? (e.Source as Visual)?.GetVisualAncestors().OfType<KanbanCard>().FirstOrDefault();
            if (card?.IsSelected != true) view.Board.ClearOtherSelections(this);
        }
        base.OnPointerPressed(e);
    }
}

[TemplatePart("PART_Items", typeof(KanbanListBox))]
[TemplatePart("PART_Collapse", typeof(Button))]
[TemplatePart("PART_Add", typeof(Button))]
[TemplatePart("PART_Resize", typeof(Thumb))]
public class KanbanColumn : TemplatedControl
{
    public static readonly StyledProperty<KanbanColumnView?> ViewProperty = AvaloniaProperty.Register<KanbanColumn, KanbanColumnView?>(nameof(View));
    public KanbanColumnView? View { get => GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    public KanbanListBox? ItemsList { get; private set; }
    private Button? _collapse, _add;
    private Thumb? _resize;
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_collapse is not null) _collapse.Click -= Collapse;
        if (_add is not null) _add.Click -= Add;
        if (_resize is not null) _resize.DragDelta -= Resize;
        base.OnApplyTemplate(e);
        ItemsList = e.NameScope.Find<KanbanListBox>("PART_Items");
        if (ItemsList is not null) ItemsList.View = View;
        _collapse = e.NameScope.Find<Button>("PART_Collapse"); if (_collapse is not null) _collapse.Click += Collapse;
        _add = e.NameScope.Find<Button>("PART_Add"); if (_add is not null) _add.Click += Add;
        _resize = e.NameScope.Find<Thumb>("PART_Resize"); if (_resize is not null) _resize.DragDelta += Resize;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == ViewProperty && ItemsList is not null) ItemsList.View = View; }
    private void Collapse(object? s, RoutedEventArgs e) { if (View is { } v) v.Definition.IsCollapsed = !v.Definition.IsCollapsed; }
    private void Add(object? s, RoutedEventArgs e) { if (View is { } v) v.Board.RequestAdd(v); }
    private void Resize(object? s, VectorEventArgs e)
    { if (View is { } v && !v.Definition.IsCollapsed && v.Board.CanResizeColumns) v.Definition.Width = Math.Clamp(v.Definition.Width + e.Vector.X, 160, 1200); }
    protected override AutomationPeer OnCreateAutomationPeer() => new KanbanGroupPeer(this, () => View?.Definition.Header ?? "Kanban column");
}

[TemplatePart("PART_Collapse", typeof(Button))]
public class KanbanSwimlane : TemplatedControl
{
    public static readonly StyledProperty<KanbanLaneView?> ViewProperty = AvaloniaProperty.Register<KanbanSwimlane, KanbanLaneView?>(nameof(View));
    public KanbanLaneView? View { get => GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    private Button? _collapse;
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_collapse is not null) _collapse.Click -= Collapse;
        base.OnApplyTemplate(e); _collapse = e.NameScope.Find<Button>("PART_Collapse");
        if (_collapse is not null) _collapse.Click += Collapse;
    }
    private void Collapse(object? s, RoutedEventArgs e) { if (View is { } v) v.Definition.IsCollapsed = !v.Definition.IsCollapsed; }
    protected override AutomationPeer OnCreateAutomationPeer() => new KanbanGroupPeer(this, () => View?.Definition.Header ?? "Swimlane");
}
internal sealed class KanbanGroupPeer(Control owner, Func<string> name) : ControlAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    protected override string GetClassNameCore() => Owner.GetType().Name;
    protected override string? GetNameCore() => AutomationProperties.GetName(Owner) ?? name();
}
