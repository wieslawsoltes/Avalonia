using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Kanban.Avalonia;
using Kanban.Core;

namespace Kanban.Sample.Browser;

/// <summary>Sample-only diagnostics. Input tests send real browser pointer/key events; this reports the resulting managed state.</summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserBridge
{
    private static MainView View => MainView.Current ?? throw new InvalidOperationException("The sample has not started.");
    private static object Bounds(Control c)
    {
        var p = c.TranslatePoint(default, View) ?? default;
        return new { x = p.X, y = p.Y, width = c.Bounds.Width, height = c.Bounds.Height };
    }
    [JSExport]
    public static string Snapshot()
    {
        var view = View; var board = view.BoardControl;
        var cards = view.GetVisualDescendants().OfType<KanbanCard>().Where(c => c.Content is KanbanItem).Select(c =>
        {
            var item = (KanbanItem)c.Content!; var p = c.TranslatePoint(default, view) ?? default;
            return new { id = item.Id, x = p.X, y = p.Y, width = c.Bounds.Width, height = c.Bounds.Height, selected = c.IsSelected,
                visible = c.IsVisible && c.GetVisualAncestors().All(a => a.IsVisible), column = item.ColumnKey, lane = item.LaneKey };
        }).ToArray();
        var columns = board.GetVisualDescendants().OfType<KanbanColumn>().Where(c => c.View is not null).Select(c =>
        {
            var p = c.TranslatePoint(default, view) ?? default; var v = c.View!;
            var list = c.ItemsList;
            var lp = list?.TranslatePoint(default, view) ?? default;
            return new { key = v.Definition.Key, lane = v.Lane.Key, x = p.X, y = p.Y, width = c.Bounds.Width, height = c.Bounds.Height,
                listX = lp.X, listY = lp.Y, listHeight = list?.Bounds.Height ?? 0,
                visibleCount = v.VisibleCount, totalCount = v.TotalCount, columnCount = v.ColumnCount, countLabel = v.CountLabel,
                collapsed = v.Definition.IsCollapsed, dropTarget = v.IsDropTarget, dropAllowed = v.IsDropAllowed,
                buttons = c.GetVisualDescendants().OfType<Button>().Where(b => b.Name is not null).Select(b => new { name = b.Name, bounds = Bounds(b), enabled = b.IsEnabled }).ToArray() };
        }).ToArray();
        return JsonSerializer.Serialize(new
        {
            ready = true, total = view.Items.Count, visible = board.VisibleCount,
            revision = board.Controller.Revision, canUndo = board.CanUndo, canRedo = board.CanRedo, history = board.Controller.UndoCount,
            selected = board.SelectedItems.Cast<KanbanItem>().Select(i => i.Id).ToArray(),
            dragging = board.IsDragging, status = board.StatusText, diagnostics = board.Controller.Diagnostics,
            source = view.Items.Select(i => new { id = i.Id, title = i.Title, column = i.ColumnKey, lane = i.LaneKey, order = i.Order, locked = i.IsLocked }).ToArray(),
            cards, columns, renderedCards = cards.Length, lastLoadMilliseconds = view.LastLoadMilliseconds,
            controls = view.GetVisualDescendants().OfType<Control>().Where(c => c.Name is not null && !c.Name.StartsWith("PART_", StringComparison.Ordinal)).Select(c => new { name = c.Name, bounds = Bounds(c), visible = c.IsVisible, enabled = c.IsEnabled }).ToArray(),
            board = Bounds(board), theme = Application.Current?.RequestedThemeVariant?.ToString()
        });
    }
    [JSExport]
    public static string Action(string name, string value)
    {
        var view = View; var board = view.BoardControl;
        switch (name)
        {
            case "reset": view.Reset(string.IsNullOrEmpty(value) ? 16 : int.Parse(value)); break;
            case "dark": view.SetDark(value == "true"); break;
            case "compact": view.SetCompact(value == "true"); break;
            case "lanes": view.SetSwimlanes(value == "true"); break;
            case "readonly": board.IsReadOnly = value == "true"; break;
            case "search": board.SearchText = value; break;
            case "select": board.SelectOnly(view.Items.FirstOrDefault(i => i.Id == value)); break;
            case "limit": { var a = value.Split(':'); board.Columns.First(c => c.Key == a[0]).MaximumWorkInProgress = a[1] == "none" ? null : int.Parse(a[1]); break; }
            case "wip": board.WipLimitMode = Enum.Parse<WipLimitMode>(value); break;
            case "undo": return JsonSerializer.Serialize(board.Undo());
            case "redo": return JsonSerializer.Serialize(board.Redo());
            case "external": view.Items.First(i => i.Id == value).Title += " updated"; break;
            case "remove": view.Items.Remove(view.Items.First(i => i.Id == value)); break;
            case "policy": board.Controller.MovePolicy = value == "reject" ? _ => KanbanMoveResult.Reject(KanbanMoveFailure.Policy, "Sample policy rejected this transition.") : null; break;
            case "export": return view.ExportJson();
            case "import": view.ImportJson(value); break;
            case "cancel": board.CancelDrag(); break;
            default: throw new ArgumentException($"Unknown sample action: {name}");
        }
        board.RefreshView(); return "ok";
    }
}
