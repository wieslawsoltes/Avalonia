using Avalonia.Controls;
using Avalonia.Inking.Core;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
namespace Avalonia.Inking;

public partial class InkCanvas
{
    private readonly List<InkCommand> _commands = [];
    public InkCommand UndoCommand { get; private set; } = null!;
    public InkCommand RedoCommand { get; private set; } = null!;
    public InkCommand SelectAllCommand { get; private set; } = null!;
    public InkCommand DeleteCommand { get; private set; } = null!;
    public InkCommand DuplicateCommand { get; private set; } = null!;
    public InkCommand ClearLayerCommand { get; private set; } = null!;
    public InkCommand RotateLeftCommand { get; private set; } = null!;
    public InkCommand RotateRightCommand { get; private set; } = null!;
    public InkCommand BringToFrontCommand { get; private set; } = null!;
    public InkCommand SendToBackCommand { get; private set; } = null!;
    public InkCommand FitCommand { get; private set; } = null!;
    public InkCommand ResetViewCommand { get; private set; } = null!;
    public InkCommand ZoomInCommand { get; private set; } = null!;
    public InkCommand ZoomOutCommand { get; private set; } = null!;
    public InkCommand SelectToolCommand { get; private set; } = null!;
    public InkCommand CopyCommand { get; private set; } = null!;
    public InkCommand CutCommand { get; private set; } = null!;
    public InkCommand PasteCommand { get; private set; } = null!;
    public event EventHandler<InkOperationFailedEventArgs>? OperationFailed;
    public event EventHandler? ViewportChanged;
    public int OperationFailureCount { get; private set; }
    private InkCommand Command(string name, Action action, Func<bool>? canExecute = null)
    {
        var command = new InkCommand(_ => Run(name, action), _ => canExecute?.Invoke() ?? true); _commands.Add(command); return command;
    }
    private void InitializeCommands()
    {
        bool SelectionEditable() => CanEdit && Session.Selection.Count > 0;
        UndoCommand = Command("Undo", () => Session.Undo(), () => !EffectiveReadOnly && _gesture == Gesture.None && Session.CanUndo);
        RedoCommand = Command("Redo", () => Session.Redo(), () => !EffectiveReadOnly && _gesture == Gesture.None && Session.CanRedo);
        SelectAllCommand = Command("Select all", () => { SelectTool(InkTool.Select); Session.SelectAll(); }, () => _gesture == Gesture.None);
        DeleteCommand = Command("Delete", () => Session.DeleteSelection(), SelectionEditable);
        DuplicateCommand = Command("Duplicate", () => Session.DuplicateSelection(), () => SelectionEditable() && Session.CanWriteActiveLayer);
        ClearLayerCommand = Command("Clear layer", () => Session.ClearActiveLayer(), () => CanEdit && Session.CanWriteActiveLayer && Session.ActiveLayer.Strokes.Length > 0);
        RotateLeftCommand = Command("Rotate left", () => Session.TransformSelection(InkTransform.Rotation(-Math.PI / 12, Session.SelectionBounds.Center)), SelectionEditable);
        RotateRightCommand = Command("Rotate right", () => Session.TransformSelection(InkTransform.Rotation(Math.PI / 12, Session.SelectionBounds.Center)), SelectionEditable);
        BringToFrontCommand = Command("Bring to front", () => Session.ReorderSelection(true), SelectionEditable);
        SendToBackCommand = Command("Send to back", () => Session.ReorderSelection(false), SelectionEditable);
        FitCommand = Command("Fit", FitToContent, () => _gesture == Gesture.None);
        ResetViewCommand = Command("Reset view", ResetView, () => _gesture == Gesture.None);
        ZoomInCommand = Command("Zoom in", () => ZoomAt(new(Bounds.Width / 2, Bounds.Height / 2), 1.2), () => _gesture == Gesture.None && Zoom < 8);
        ZoomOutCommand = Command("Zoom out", () => ZoomAt(new(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2), () => _gesture == Gesture.None && Zoom > .1);
        SelectToolCommand = new InkCommand(value => { if (value is InkTool tool) SelectTool(tool); else if (Enum.TryParse<InkTool>(value?.ToString(), out var parsed)) SelectTool(parsed); });
        _commands.Add(SelectToolCommand);
        CopyCommand = Command("Copy", () => _ = RunAsync("Copy", () => CopySelectionAsync(false)), () => _gesture == Gesture.None && Session.Selection.Count > 0);
        CutCommand = Command("Cut", () => _ = RunAsync("Cut", () => CopySelectionAsync(true)), SelectionEditable);
        PasteCommand = Command("Paste", () => _ = RunAsync("Paste", PasteAsync), () => CanEdit && Session.CanWriteActiveLayer);
    }
    public void SelectTool(InkTool tool)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentException("Unknown tool.");
        CancelGesture(); SetCurrentValue(ToolProperty, tool);
        if (tool is InkTool.Pen or InkTool.Pencil or InkTool.Highlighter)
        {
            var preset = tool == InkTool.Pencil ? InkPresets.Pencil : tool == InkTool.Highlighter ? InkPresets.Highlighter : InkPresets.Pen;
            SetCurrentValue(BrushSizeProperty, preset.Width); SetCurrentValue(BrushOpacityProperty, preset.Opacity);
            SetCurrentValue(InkColorProperty, Color.FromUInt32(preset.Color)); SetCurrentValue(UsePressureProperty, preset.UsePressure);
            SetCurrentValue(PressureCurveProperty, preset.PressureCurve);
        }
        Focus(); SetStatus(tool.ToString());
    }
    public void SetStatus(string text) => SetAndRaise(StatusTextProperty, ref _statusText, text);
    private void Run(string name, Action action)
    {
        try { action(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            CancelGesture(); OperationFailureCount++; SetStatus(error.Message); OperationFailed?.Invoke(this, new(name, error));
        }
    }
    private async Task RunAsync(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { OperationFailureCount++; SetStatus(error.Message); OperationFailed?.Invoke(this, new(name, error)); }
    }
    public async Task CopySelectionAsync(bool cut = false)
    {
        if (Session.Selection.Count == 0 || (cut && !CanEdit)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new NotSupportedException("The host has no clipboard.");
        var session = Session; var document = session.Document; var ids = session.Selection;
        await clipboard.SetTextAsync(session.CopySelectionJson());
        if (cut && CanEdit && ReferenceEquals(Session, session) && ReferenceEquals(Session.Document, document)) session.DeleteStrokes(ids);
        SetStatus(cut ? "Cut ink" : "Copied ink");
    }
    public async Task PasteAsync()
    {
        if (!CanEdit || !Session.CanWriteActiveLayer) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new NotSupportedException("The host has no clipboard.");
        var session = Session; var document = session.Document; var layer = session.ActiveLayerId;
        var json = await clipboard.TryGetTextAsync(); if (json is null) return;
        if (!CanEdit || !ReferenceEquals(Session, session) || !ReferenceEquals(session.Document, document) || layer != session.ActiveLayerId) throw new InvalidOperationException("The document changed while reading the clipboard.");
        session.PasteJson(json); SelectTool(InkTool.Select); SetStatus("Pasted ink");
    }
    public Point DocumentToView(InkVector p) => new(p.X * Zoom + PanOffset.X, p.Y * Zoom + PanOffset.Y);
    public InkVector ViewToDocument(Point p) => new((p.X - PanOffset.X) / Zoom, (p.Y - PanOffset.Y) / Zoom);
    public void ZoomAt(Point screenAnchor, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
        var point = ViewToDocument(screenAnchor); var zoom = Math.Clamp(Zoom * multiplier, .1, 8);
        SetCurrentValue(ZoomProperty, zoom); SetCurrentValue(PanOffsetProperty, new Vector(screenAnchor.X - point.X * zoom, screenAnchor.Y - point.Y * zoom));
    }
    public void FitToContent()
    {
        var r = Session.Document.ContentBounds;
        if (r.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0) { ResetView(); return; }
        var zoom = Math.Clamp(Math.Min(Math.Max(1, Bounds.Width - 100) / Math.Max(1, r.Width), Math.Max(1, Bounds.Height - 100) / Math.Max(1, r.Height)), .1, 8);
        SetCurrentValue(ZoomProperty, zoom); SetCurrentValue(PanOffsetProperty, new Vector(Bounds.Width / 2 - r.Center.X * zoom, Bounds.Height / 2 - r.Center.Y * zoom));
    }
    public void ResetView() { SetCurrentValue(ZoomProperty, 1d); SetCurrentValue(PanOffsetProperty, default(Vector)); }
    public InkBrush GetCurrentBrush()
    {
        var preset = Tool == InkTool.Pencil ? InkPresets.Pencil : Tool == InkTool.Highlighter ? InkPresets.Highlighter : InkPresets.Pen;
        return preset with { Color = InkColor.ToUInt32(), Width = BrushSize, Opacity = BrushOpacity, UsePressure = UsePressure, PressureCurve = PressureCurve };
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; return; }
        if (e.Key == Key.Space) { _spaceDown = true; UpdateCursor(); e.Handled = true; return; }
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        InkCommand? action = null;
        if (command)
            action = e.Key switch { Key.Z => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? RedoCommand : UndoCommand, Key.Y => RedoCommand, Key.A => SelectAllCommand, Key.D => DuplicateCommand, Key.C => CopyCommand, Key.X => CutCommand, Key.V => PasteCommand, Key.D0 => ResetViewCommand, _ => null };
        else if (e.Key is Key.Delete or Key.Back) action = DeleteCommand;
        if (action is not null) { action.Execute(null); e.Handled = true; return; }
        if ((e.Key is Key.Left or Key.Right or Key.Up or Key.Down) && CanEdit && Session.Selection.Count > 0)
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            Run("Nudge", () => Session.TransformSelection(InkTransform.Translation(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0)));
            e.Handled = true; return;
        }
        if (command || _gesture != Gesture.None) return;
        InkTool? tool = e.Key switch { Key.B => InkTool.Pen, Key.P => InkTool.Pencil, Key.H => InkTool.Highlighter, Key.E => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? InkTool.StrokeEraser : InkTool.PointEraser, Key.V => InkTool.Select, Key.L => InkTool.Lasso, Key.I => InkTool.Line, Key.R => InkTool.Rectangle, Key.O => InkTool.Ellipse, _ => null };
        if (tool is { } selected) { SelectTool(selected); e.Handled = true; }
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e); if (e.Key == Key.Space) { _spaceDown = false; UpdateCursor(); e.Handled = true; }
    }
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e); _spaceDown = false; UpdateCursor();
    }
}
