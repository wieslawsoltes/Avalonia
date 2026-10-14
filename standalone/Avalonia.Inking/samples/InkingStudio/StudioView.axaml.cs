using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Inking;
using Avalonia.Inking.Core;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace InkingStudio;

/// <summary>One sample UI shared unchanged by desktop and the real WebAssembly host.</summary>
public partial class StudioView : UserControl
{
    public InkCanvas InkSurface { get; }
    public InkSession Session => InkSurface.Session;
    public IStudioStorage? Storage { get; set; }
    public string RecoveryState { get; private set; } = "Recovery ready";
    public bool IsReady { get; private set; }
    private readonly DispatcherTimer _recovery = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _metrics = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _syncing, _initialized, _busy;
    private TaskCompletionSource<bool>? _confirmation;
    private InkTransaction? _layerOpacityTransaction;
    private T C<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException("Missing sample part " + name);
    public StudioView()
    {
        AvaloniaXamlLoader.Load(this); InkSurface = C<InkCanvas>("Canvas");
        WireButton("NewButton", async () => { if (await ConfirmDiscardAsync()) LoadDocument(new InkDocument()); });
        WireButton("OpenButton", OpenAsync);
        WireButton("SaveButton", SaveAsync);
        WireButton("SvgButton", () => ExportAsync(false));
        WireButton("PngButton", () => ExportAsync(true));
        WireButton("DemoButton", async () => { if (await ConfirmDiscardAsync()) LoadDocument(DemoDocuments.Showcase()); });
        C<Button>("ThemeButton").Click += (_, _) =>
        {
            var dark = Application.Current!.RequestedThemeVariant != ThemeVariant.Dark;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            C<Button>("ThemeButton").Content = dark ? "Light" : "Dark";
        };
        C<Button>("ConfirmCancel").Click += (_, _) => ResolveConfirmation(false);
        C<Button>("ConfirmDiscard").Click += (_, _) => ResolveConfirmation(true);
        foreach (var color in InkPresets.Palette)
        {
            var button = new Button { Width = 23, Height = 23, Padding = new Thickness(0), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromUInt32(color)), BorderThickness = new Thickness(2), BorderBrush = Brushes.White };
            AutomationProperties.SetName(button, "Ink color " + color.ToString("X8"));
            AutomationProperties.SetAutomationId(button, "color-" + color.ToString("X8"));
            button.Click += (_, _) => { InkSurface.InkColor = Color.FromUInt32(color); InkSurface.Focus(); };
            C<WrapPanel>("Palette").Children.Add(button);
        }
        Slider("SizeSlider", value => InkSurface.BrushSize = value);
        Slider("OpacitySlider", value => InkSurface.BrushOpacity = value);
        Slider("SmoothingSlider", value => InkSurface.Smoothing = value);
        Slider("RulerSlider", value => InkSurface.RulerAngle = value);
        Toggle("PressureToggle", value => InkSurface.UsePressure = value);
        Toggle("SnapToggle", value => InkSurface.SnapToGrid = value);
        Toggle("RulerToggle", value => InkSurface.RulerEnabled = value);
        Toggle("TouchToggle", value => InkSurface.AcceptsTouchInk = value);
        Toggle("ReadOnlyToggle", value => { InkSurface.CancelGesture(); Session.IsReadOnly = value; InkSurface.IsReadOnly = value; Sync(); });
        var grid = C<ComboBox>("GridCombo"); grid.ItemsSource = Enum.GetValues<InkGrid>(); grid.SelectedItem = InkGrid.Dots;
        grid.SelectionChanged += (_, _) => { if (!_syncing && grid.SelectedItem is InkGrid value) InkSurface.GridMode = value; };
        C<TextBox>("LayerNameBox").LostFocus += (_, _) => UpdateLayerName();
        C<TextBox>("LayerNameBox").KeyDown += (_, e) => { if (e.Key == Key.Enter) { UpdateLayerName(); InkSurface.Focus(); e.Handled = true; } };
        var layerSlider = C<Slider>("LayerOpacitySlider");
        layerSlider.PropertyChanged += (_, e) =>
        {
            if (_syncing || e.Property != RangeBase.ValueProperty || Session.IsReadOnly) return;
            try
            {
                _layerOpacityTransaction ??= Session.BeginTransaction("Layer opacity");
                Session.UpdateLayer(Session.ActiveLayerId, opacity: layerSlider.Value);
            }
            catch (Exception error) { Report(error); }
        };
        layerSlider.PointerReleased += (_, _) => CommitLayerOpacity();
        layerSlider.KeyUp += (_, _) => CommitLayerOpacity();
        layerSlider.LostFocus += (_, _) => CommitLayerOpacity();
        InkSurface.PropertyChanged += (_, e) =>
        {
            if (!_initialized) return;
            if (e.Property == InkCanvas.BrushSizeProperty || e.Property == InkCanvas.BrushOpacityProperty || e.Property == InkCanvas.InkColorProperty || e.Property == InkCanvas.ToolProperty || e.Property == InkCanvas.UsePressureProperty) Sync();
        };
        Session.Changed += (_, e) =>
        {
            Sync();
            if (e.DocumentChanged) { _recovery.Stop(); _recovery.Start(); }
        };
        InkSurface.OperationFailed += (_, e) => Report(e.Error);
        C<InkLayerPanel>("LayerPanel").OperationFailed += (_, e) => Report(e.Error);
        _recovery.Tick += (_, _) => SaveRecovery();
        _metrics.Tick += (_, _) => UpdateMetrics();
        _initialized = true;
        LoadDocument(DemoDocuments.Showcase());
    }
    private void WireButton(string name, Func<Task> action) => C<Button>(name).Click += async (_, _) => await SafeAsync(action);
    private async Task SafeAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { CommitLayerOpacity(); await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Report(e); }
        finally { _busy = false; }
    }
    private void Slider(string name, Action<double> action)
    {
        var slider = C<Slider>(name);
        slider.PropertyChanged += (_, e) => { if (!_syncing && e.Property == RangeBase.ValueProperty) action(slider.Value); };
    }
    private void Toggle(string name, Action<bool> action)
    {
        var toggle = C<CheckBox>(name);
        toggle.PropertyChanged += (_, e) => { if (!_syncing && e.Property == ToggleButton.IsCheckedProperty) action(toggle.IsChecked == true); };
    }
    private void CommitLayerOpacity() { var transaction = _layerOpacityTransaction; _layerOpacityTransaction = null; transaction?.Commit(); }
    private void UpdateLayerName()
    {
        if (Session.IsReadOnly) return;
        try { Session.UpdateLayer(Session.ActiveLayerId, name: C<TextBox>("LayerNameBox").Text?.Trim()); }
        catch (Exception e) { Report(e); Sync(); }
    }
    public void LoadDocument(InkDocument document)
    {
        InkSurface.CancelGesture(); _layerOpacityTransaction?.Dispose(); _layerOpacityTransaction = null;
        Session.Load(document); InkSurface.ResetView(); Sync();
    }
    public void ResetForValidation()
    {
        InkSurface.CancelGesture(); Session.IsReadOnly = false; InkSurface.IsReadOnly = false;
        InkSurface.SelectTool(InkTool.Pen); InkSurface.Smoothing = 0; InkSurface.GridMode = InkGrid.None;
        InkSurface.RulerEnabled = false; InkSurface.SnapToGrid = false; InkSurface.AcceptsTouchInk = false;
        LoadDocument(new InkDocument());
    }
    private void Sync()
    {
        if (!_initialized || _syncing) return;
        _syncing = true;
        try
        {
            C<TextBlock>("DocumentTitle").Text = Session.Document.Title;
            C<TextBlock>("SaveState").Text = (Session.IsDirty ? "Unsaved changes" : "Local document") + " · " + RecoveryState;
            C<StackPanel>("EmptyHint").IsVisible = Session.Document.StrokeCount == 0;
            C<TextBlock>("ToolLabel").Text = InkSurface.Tool.ToString();
            C<Slider>("SizeSlider").Value = Math.Clamp(InkSurface.BrushSize, .5, 48);
            C<Slider>("OpacitySlider").Value = InkSurface.BrushOpacity;
            C<CheckBox>("PressureToggle").IsChecked = InkSurface.UsePressure;
            C<TextBlock>("SizeLabel").Text = $"{InkSurface.BrushSize:0.#} px";
            C<TextBlock>("OpacityLabel").Text = $"{InkSurface.BrushOpacity:P0}";
            C<CheckBox>("ReadOnlyToggle").IsChecked = Session.IsReadOnly;
            C<TextBox>("LayerNameBox").IsEnabled = !Session.IsReadOnly;
            if (!C<TextBox>("LayerNameBox").IsFocused) C<TextBox>("LayerNameBox").Text = Session.ActiveLayer.Name;
            C<Slider>("LayerOpacitySlider").Value = Session.ActiveLayer.Opacity;
            C<Slider>("LayerOpacitySlider").IsEnabled = !Session.IsReadOnly;
            var brush = InkSurface.GetCurrentBrush();
            var points = Enumerable.Range(0, 64).Select(i => new InkPoint(15 + i * 2.9, 28 + 9 * Math.Sin(i * .1), .08 + .92 * i / 63d));
            C<InkCanvas>("BrushPreview").Session.Load(new InkDocument(layers: [new InkLayer(strokes: [new InkStroke(points, brush)])]));
            UpdateMetrics();
        }
        finally { _syncing = false; }
    }
    private void UpdateMetrics()
    {
        C<TextBlock>("Status").Text = InkSurface.StatusText;
        C<Button>("ZoomButton").Content = $"{InkSurface.Zoom:P0}";
        C<TextBlock>("Metrics").Text = $"{Session.Document.StrokeCount:N0} strokes   {Session.Document.PointCount:N0} samples   {Session.Selection.Count} selected   {InkSurface.LastPointerKind} {InkSurface.LastPressure:P0}";
    }
    private void SaveRecovery()
    {
        _recovery.Stop(); if (Storage is null || Session.IsEditing) return;
        try { Storage.WriteRecovery(InkSerializer.Serialize(Session.Document)); RecoveryState = "Recovery saved"; }
        catch (Exception e) { RecoveryState = "Recovery unavailable"; InkSurface.SetStatus(e.Message); }
        Sync();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _metrics.Start();
        if (!IsReady)
        {
            try
            {
                var recovery = Storage?.ReadRecovery();
                if (!string.IsNullOrEmpty(recovery)) { Session.Load(InkSerializer.Deserialize(recovery), false); RecoveryState = "Recovered local draft"; }
            }
            catch (Exception error) { RecoveryState = "Recovery could not be read"; Report(error); }
            IsReady = true; Sync();
        }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _metrics.Stop(); _recovery.Stop(); ResolveConfirmation(false);
        _layerOpacityTransaction?.Dispose(); _layerOpacityTransaction = null;
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_initialized) return;
        var narrow = e.NewSize.Width < 1040;
        C<Border>("Inspector").IsVisible = !narrow;
        C<Grid>("Workspace").ColumnDefinitions[2].Width = new GridLength(narrow ? 0 : 272);
        C<StackPanel>("DocumentHeading").IsVisible = e.NewSize.Width >= 1100;
        C<TextBlock>("ToolbarHint").IsVisible = e.NewSize.Width >= 1200;
        C<Button>("SvgButton").IsVisible = e.NewSize.Width >= 760;
        C<Button>("PngButton").IsVisible = e.NewSize.Width >= 760;
    }
    private void Report(Exception error) => InkSurface.SetStatus(error.Message);
    private Task<bool> ConfirmDiscardAsync()
    {
        InkSurface.CancelGesture();
        if (!Session.IsDirty) return Task.FromResult(true);
        _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        C<Border>("ConfirmationOverlay").IsVisible = true; C<Button>("ConfirmCancel").Focus();
        return _confirmation.Task;
    }
    private void ResolveConfirmation(bool discard)
    {
        C<Border>("ConfirmationOverlay").IsVisible = false;
        var source = _confirmation; _confirmation = null; source?.TrySetResult(discard);
    }
    private async Task OpenAsync()
    {
        var text = await (Storage ?? throw new InvalidOperationException("Storage is not ready.")).OpenInkAsync();
        if (text is null) return;
        var document = InkSerializer.Deserialize(text);
        if (await ConfirmDiscardAsync()) { LoadDocument(document); InkSurface.SetStatus("Opened ink document"); }
    }
    private async Task SaveAsync()
    {
        InkSurface.CancelGesture(); var snapshot = Session.Document;
        if (await (Storage ?? throw new InvalidOperationException("Storage is not ready.")).SaveAsync("document.ink.json", Encoding.UTF8.GetBytes(InkSerializer.Serialize(snapshot)), "application/json"))
        {
            Session.MarkSaved(snapshot); InkSurface.SetStatus(OperatingSystem.IsBrowser() ? "Ink download started" : "Ink document saved");
        }
    }
    private async Task ExportAsync(bool png)
    {
        var snapshot = Session.Document;
        var data = png ? InkExporter.ExportPng(snapshot, 2) : Encoding.UTF8.GetBytes(InkSerializer.ExportSvg(snapshot));
        if (await (Storage ?? throw new InvalidOperationException("Storage is not ready.")).SaveAsync(png ? "ink.png" : "ink.svg", data, png ? "image/png" : "image/svg+xml"))
            InkSurface.SetStatus(png ? "PNG export started" : "SVG export started");
    }
    /// <summary>Sample diagnostics use actual visual bounds, not hard-coded DOM coordinates.</summary>
    public Rect? FindAutomationBounds(string id)
    {
        var control = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);
        if (control is null) return null;
        var origin = control.TranslatePoint(default, this); return origin is { } point ? new Rect(point, control.Bounds.Size) : null;
    }
}
