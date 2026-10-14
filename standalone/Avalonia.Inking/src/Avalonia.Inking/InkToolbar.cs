using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Inking.Core;
using Avalonia.Layout;
using Avalonia.Media;
namespace Avalonia.Inking;

public class InkToolbar : TemplatedControl
{
    public static readonly StyledProperty<InkCanvas?> TargetProperty = AvaloniaProperty.Register<InkToolbar, InkCanvas?>(nameof(Target));
    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<InkToolbar, Orientation>(nameof(Orientation), Orientation.Vertical);
    public InkCanvas? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public IReadOnlyList<InkToolItem> Tools { get; }
    private InkCanvas? _observed;
    private bool _attached;
    public InkToolbar()
    {
        Tools = new[]
        {
            new InkToolItem(this, InkTool.Pen, "Pen", "B", "M3 17 L3 21 7 21 19 9 15 5 Z M16 4 L18 2 22 6 20 8 Z"),
            new InkToolItem(this, InkTool.Pencil, "Pencil", "P", "M3 17 L2 22 7 21 20 8 16 4 Z M17 3 L19 1 23 5 21 7 Z"),
            new InkToolItem(this, InkTool.Highlighter, "Highlighter", "H", "M3 17 L7 21 12 16 8 12 Z M9 11 L16 4 22 10 15 17 Z M2 22 L10 22 10 24 2 24 Z"),
            new InkToolItem(this, InkTool.PointEraser, "Point eraser", "E", "M3 14 L13 4 21 12 11 22 9 22 Z M5 14 L10 19 14 15 9 10 Z"),
            new InkToolItem(this, InkTool.StrokeEraser, "Stroke eraser", "Shift+E", "M3 13 L12 4 20 12 11 21 9 21 Z M14 19 L23 19 23 21 14 21 Z"),
            new InkToolItem(this, InkTool.Select, "Select and transform", "V", "M4 2 L4 21 9 16 13 23 16 21 12 15 20 14 Z"),
            new InkToolItem(this, InkTool.Lasso, "Lasso select", "L", "M12 3 C1 3 1 16 12 16 C23 16 23 3 12 3 M11 14 L13 14 C14 21 9 24 6 20 L7 18 C9 22 12 20 11 14 Z"),
            new InkToolItem(this, InkTool.Line, "Straight line", "I", "M3 19 L19 3 21 5 5 21 Z"),
            new InkToolItem(this, InkTool.Rectangle, "Rectangle", "R", "M3 4 L21 4 21 20 3 20 Z M5 6 L5 18 19 18 19 6 Z"),
            new InkToolItem(this, InkTool.Ellipse, "Ellipse", "O", "M12 2 A10 10 0 1 1 11.99 2 Z M12 4 A8 8 0 1 0 12.01 4 Z"),
            new InkToolItem(this, InkTool.Pan, "Pan canvas", "Space", "M10 2 L14 2 14 10 22 10 22 14 14 14 14 22 10 22 10 14 2 14 2 10 10 10 Z")
        };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; Observe(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached = false; Observe(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == TargetProperty) Observe(); }
    private void Observe()
    {
        if (_observed is not null) _observed.PropertyChanged -= TargetChanged;
        _observed = _attached ? Target : null;
        if (_observed is not null) _observed.PropertyChanged += TargetChanged;
        foreach (var item in Tools) item.Refresh();
    }
    private void TargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == InkCanvas.ToolProperty) foreach (var item in Tools) item.Refresh();
    }
}
public sealed class InkToolItem : INotifyPropertyChanged
{
    private readonly InkToolbar _owner;
    public InkTool Tool { get; }
    public string Label { get; }
    public string Hint { get; }
    public Geometry Icon { get; }
    public string AutomationId => "tool-" + Tool;
    public bool IsActive => _owner.Target?.Tool == Tool;
    public InkCommand Command { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal InkToolItem(InkToolbar owner, InkTool tool, string label, string shortcut, string icon)
    {
        _owner = owner; Tool = tool; Label = label; Hint = label + " (" + shortcut + ")"; Icon = Geometry.Parse(icon);
        Command = new InkCommand(_ => owner.Target?.SelectTool(tool), _ => owner.Target is not null);
    }
    internal void Refresh() { PropertyChanged?.Invoke(this, new(nameof(IsActive))); Command.RaiseCanExecuteChanged(); }
}
