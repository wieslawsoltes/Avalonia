using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Inking.Core;
namespace Avalonia.Inking;

/// <summary>Lookless layer management. The host's DataContext is never replaced.</summary>
public class InkLayerPanel : TemplatedControl
{
    public static readonly StyledProperty<InkSession?> SessionProperty = AvaloniaProperty.Register<InkLayerPanel, InkSession?>(nameof(Session));
    public InkSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public ObservableCollection<InkLayerItem> Layers { get; } = [];
    public InkCommand AddLayerCommand { get; }
    public InkCommand MoveUpCommand { get; }
    public InkCommand MoveDownCommand { get; }
    public InkCommand RemoveLayerCommand { get; }
    public event EventHandler<InkOperationFailedEventArgs>? OperationFailed;
    private InkSession? _observed;
    private bool _attached;
    public InkLayerPanel()
    {
        bool Writable() => Session is { IsReadOnly: false, IsEditing: false };
        AddLayerCommand = new(_ => Run(() => Session?.AddLayer()), _ => Writable() && Session!.Document.Layers.Length < InkLimits.MaxLayers);
        MoveUpCommand = new(_ => Run(() => Session!.MoveLayer(Session.ActiveLayerId, 1)), _ => Writable());
        MoveDownCommand = new(_ => Run(() => Session!.MoveLayer(Session.ActiveLayerId, -1)), _ => Writable());
        RemoveLayerCommand = new(_ => Run(() => Session!.RemoveLayer(Session.ActiveLayerId)), _ => Writable() && Session!.Document.Layers.Length > 1 && !Session.ActiveLayer.IsLocked);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; Observe(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached = false; Observe(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == SessionProperty) Observe(); }
    private void Observe()
    {
        if (_observed is not null) _observed.Changed -= Changed;
        _observed = _attached ? Session : null;
        if (_observed is not null) _observed.Changed += Changed;
        Refresh();
    }
    private void Changed(object? sender, InkSessionChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        var desired = Session?.Document.Layers.Reverse().ToArray() ?? [];
        if (!Layers.Select(l => l.Id).SequenceEqual(desired.Select(l => l.Id)))
        {
            Layers.Clear(); foreach (var layer in desired) Layers.Add(new(this, layer));
        }
        else for (var i = 0; i < desired.Length; i++) Layers[i].Update(desired[i]);
        foreach (var item in Layers) item.Refresh();
        AddLayerCommand?.RaiseCanExecuteChanged(); MoveUpCommand?.RaiseCanExecuteChanged(); MoveDownCommand?.RaiseCanExecuteChanged(); RemoveLayerCommand?.RaiseCanExecuteChanged();
    }
    internal void Run(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { OperationFailed?.Invoke(this, new("Layer operation", e)); }
    }
}
public sealed class InkLayerItem : INotifyPropertyChanged
{
    private readonly InkLayerPanel _owner;
    private InkLayer _layer;
    public Guid Id => _layer.Id;
    public string Name => _layer.Name;
    public string Detail => $"{_layer.Strokes.Length:N0} strokes · {_layer.Opacity:P0}";
    public string VisibilityText => _layer.IsVisible ? "Hide" : "Show";
    public string LockText => _layer.IsLocked ? "Unlock" : "Lock";
    public bool IsActive => _owner.Session?.ActiveLayerId == Id;
    public InkCommand ActivateCommand { get; }
    public InkCommand ToggleVisibilityCommand { get; }
    public InkCommand ToggleLockCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal InkLayerItem(InkLayerPanel owner, InkLayer layer)
    {
        _owner = owner; _layer = layer;
        bool Writable() => owner.Session is { IsReadOnly: false, IsEditing: false };
        ActivateCommand = new(_ => owner.Run(() => owner.Session?.SetActiveLayer(Id)), _ => owner.Session is { IsEditing: false });
        ToggleVisibilityCommand = new(_ => owner.Run(() => owner.Session!.UpdateLayer(Id, visible: !_layer.IsVisible)), _ => Writable());
        ToggleLockCommand = new(_ => owner.Run(() => owner.Session!.UpdateLayer(Id, locked: !_layer.IsLocked)), _ => Writable());
    }
    internal void Update(InkLayer layer) { _layer = layer; Refresh(); }
    internal void Refresh()
    {
        PropertyChanged?.Invoke(this, new(null)); ActivateCommand.RaiseCanExecuteChanged(); ToggleVisibilityCommand.RaiseCanExecuteChanged(); ToggleLockCommand.RaiseCanExecuteChanged();
    }
}
