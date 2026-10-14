using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kanban.Core;

namespace Kanban.Avalonia;

public partial class KanbanBoard
{
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
            var delta = _pointerPoint - _pressPoint;
            var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
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
            if (column.View is not { } view || !view.IsExpanded || view.Lane.IsCollapsed || !column.IsVisible || column.TranslatePoint(default, this) is not { } origin ||
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
