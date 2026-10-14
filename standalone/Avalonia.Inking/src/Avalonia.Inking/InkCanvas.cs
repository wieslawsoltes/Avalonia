using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Inking.Core;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
namespace Avalonia.Inking;

[TemplatePart("PART_Presenter", typeof(InkPresenter), IsRequired = true)]
[TemplatePart("PART_Adorner", typeof(InkAdornerPresenter), IsRequired = true)]
[PseudoClasses(":inking", ":selecting", ":erasing", ":panning", ":read-only", ":has-selection")]
public partial class InkCanvas : TemplatedControl
{
    private readonly InkSession _ownedSession = new();
    private InkSession? _subscribed;
    private InkPresenter? _presenter;
    private InkAdornerPresenter? _adorner;
    private bool _attached, _mutatingSession, _releasing;
    private enum Gesture { None, Ink, Shape, Erase, Box, Lasso, Move, Resize, Rotate, Pan, Pinch }
    private Gesture _gesture;
    private IPointer? _pointer;
    private InkSession? _gestureSession;
    private InkDocument? _gestureDocument;
    private InkTool _gestureTool;
    private InkBrush _gestureBrush = InkPresets.Pen;
    private InkStrokeBuilder? _builder;
    private InkStroke? _preview;
    private bool _previewDirty;
    private InkTransaction? _eraseTransaction;
    private InkVector _start, _last;
    private Point _startScreen, _cursor;
    private Vector _panStart;
    private InkRect _selectionStart;
    private InkTransform _previewTransform = InkTransform.Identity;
    private readonly List<InkVector> _lasso = [];
    private int _handle;
    private bool _additive, _spaceDown, _cursorInside;
    private readonly Dictionary<int, (IPointer Pointer, Point Point)> _touches = [];
    private double _pinchDistance, _pinchZoom;
    private InkVector _pinchAnchor;
    private long _lastPenTick;
    private bool _penActive;
    private StandardCursorType? _cursorType;
    public static readonly RoutedEvent<InkStrokeEventArgs> StrokeCollectingEvent = RoutedEvent.Register<InkCanvas, InkStrokeEventArgs>(nameof(StrokeCollecting), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<InkStrokeEventArgs> StrokeCollectedEvent = RoutedEvent.Register<InkCanvas, InkStrokeEventArgs>(nameof(StrokeCollected), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<InkDocumentEventArgs> DocumentChangedEvent = RoutedEvent.Register<InkCanvas, InkDocumentEventArgs>(nameof(DocumentChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent = RoutedEvent.Register<InkCanvas, RoutedEventArgs>(nameof(SelectionChanged), RoutingStrategies.Bubble);
    public event EventHandler<InkStrokeEventArgs>? StrokeCollecting { add => AddHandler(StrokeCollectingEvent, value); remove => RemoveHandler(StrokeCollectingEvent, value); }
    public event EventHandler<InkStrokeEventArgs>? StrokeCollected { add => AddHandler(StrokeCollectedEvent, value); remove => RemoveHandler(StrokeCollectedEvent, value); }
    public event EventHandler<InkDocumentEventArgs>? DocumentChanged { add => AddHandler(DocumentChangedEvent, value); remove => RemoveHandler(DocumentChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }
    public InkCanvas() { Focusable = true; ClipToBounds = true; InitializeCommands(); }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        CancelGesture();
        if (_presenter is not null) _presenter.Owner = null;
        if (_adorner is not null) _adorner.Owner = null;
        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<InkPresenter>("PART_Presenter"); _adorner = e.NameScope.Find<InkAdornerPresenter>("PART_Adorner");
        if (_presenter is not null) _presenter.Owner = this;
        if (_adorner is not null) _adorner.Owner = this;
        Refresh();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; SubscribeSession(); Refresh(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture(); _attached = false; SubscribeSession(); _presenter?.ClearCache(); base.OnDetachedFromVisualTree(e);
    }
    private void SubscribeSession()
    {
        if (_subscribed is not null) _subscribed.Changed -= SessionChanged;
        _subscribed = _attached ? Session : null;
        if (_subscribed is not null) _subscribed.Changed += SessionChanged;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Device telemetry must not re-evaluate every command or redraw retained ink for every sample.
        if (change.Property == LastPressureProperty || change.Property == StatusTextProperty || change.Property == CursorProperty) return;
        if (change.Property == SessionProperty) { CancelGesture(); SubscribeSession(); _presenter?.ClearCache(); }
        if (change.Property == ToolProperty || change.Property == IsReadOnlyProperty) CancelGesture();
        if (change.Property == ZoomProperty || change.Property == PanOffsetProperty) ViewportChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }
    private void SessionChanged(object? sender, InkSessionChangedEventArgs e)
    {
        if (!_mutatingSession && _gesture != Gesture.None && (e.DocumentChanged || EffectiveReadOnly))
            Dispatcher.UIThread.Post(() => { if (!_mutatingSession) CancelGesture(); });
        Refresh();
        if (e.DocumentChanged) RaiseEvent(new InkDocumentEventArgs(DocumentChangedEvent, Session.Document, e.Reason));
        if (e.SelectionChanged) RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
    }
    private void Mutate(Action action) { _mutatingSession = true; try { action(); } finally { _mutatingSession = false; } }
    private void Refresh()
    {
        if (_commands is null) return;
        foreach (var command in _commands) command.RaiseCanExecuteChanged();
        PseudoClasses.Set(":inking", _gesture is Gesture.Ink or Gesture.Shape);
        PseudoClasses.Set(":selecting", _gesture is Gesture.Box or Gesture.Lasso or Gesture.Move or Gesture.Resize or Gesture.Rotate);
        PseudoClasses.Set(":erasing", _gesture == Gesture.Erase); PseudoClasses.Set(":panning", _gesture is Gesture.Pan or Gesture.Pinch);
        PseudoClasses.Set(":read-only", EffectiveReadOnly); PseudoClasses.Set(":has-selection", Session.Selection.Count > 0);
        UpdateCursor(); _presenter?.InvalidateVisual(); _adorner?.InvalidateVisual();
    }
    private void UpdateCursor()
    {
        var type = _spaceDown || Tool == InkTool.Pan || _gesture is Gesture.Pan or Gesture.Pinch ? StandardCursorType.Hand : Tool is InkTool.Select or InkTool.Lasso ? StandardCursorType.Arrow : StandardCursorType.Cross;
        if (_cursorType != type) { _cursorType = type; Cursor = new Cursor(type); }
    }
    public void CancelGesture()
    {
        var active = _gesture != Gesture.None; _gesture = Gesture.None;
        var transaction = _eraseTransaction; _eraseTransaction = null; if (transaction is not null) Mutate(transaction.Dispose);
        _builder = null; _preview = null; _previewDirty = false; _lasso.Clear(); _previewTransform = InkTransform.Identity;
        _gestureSession = null; _gestureDocument = null; _penActive = false; ReleasePointers();
        if (active) { CancelledGestureCount++; SetStatus("Gesture cancelled"); }
        Refresh();
    }
    private void ReleasePointers()
    {
        _releasing = true;
        try { _pointer?.Capture(null); _pointer = null; foreach (var touch in _touches.Values) touch.Pointer.Capture(null); _touches.Clear(); }
        finally { _releasing = false; }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); if (!_releasing) CancelGesture(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _cursorInside = false; _adorner?.InvalidateVisual(); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _cursorInside = true; }
    private static double Distance(Point a, Point b) { var x = a.X - b.X; var y = a.Y - b.Y; return Math.Sqrt(x * x + y * y); }
    private InkVector Constrain(InkVector p, bool ruler)
    {
        if (ruler && RulerEnabled)
        {
            var angle = RulerAngle * Math.PI / 180; var d = new InkVector(Math.Cos(angle), Math.Sin(angle));
            var origin = new InkVector(RulerOrigin.X, RulerOrigin.Y); p = origin + d * InkVector.Dot(p - origin, d);
        }
        else if (SnapToGrid) p = new(Math.Round(p.X / GridSpacing) * GridSpacing, Math.Round(p.Y / GridSpacing) * GridSpacing);
        return p;
    }
    private InkPoint Sample(PointerPoint p, PointerType type, ulong timestamp, bool final = false)
    {
        var world = Constrain(ViewToDocument(p.Position), true); var properties = p.Properties;
        static double Safe(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        var pressure = type == PointerType.Pen ? Safe(properties.Pressure, 0, 1, .5) : 1;
        if (final && pressure == 0 && _builder is { Count: > 0 }) pressure = LastPressure;
        SetAndRaise(LastPressureProperty, ref _lastPressure, pressure);
        return new(world.X, world.Y, pressure, Safe(properties.XTilt, -90, 90, 0), Safe(properties.YTilt, -90, 90, 0), Safe(properties.Twist, 0, 360, 0), timestamp);
    }
    internal InkStroke? LiveStroke { get { if (_previewDirty && _builder is { Count: > 0 }) { _preview = _builder.Build(); _previewDirty = false; } return _preview; } }
    internal bool HasTransformPreview => _gesture is Gesture.Move or Gesture.Resize or Gesture.Rotate;
    internal InkTransform PreviewTransform => _previewTransform;
    internal IReadOnlyList<InkVector> LassoPreview => _gesture == Gesture.Lasso ? _lasso : [];
    internal InkRect BoxPreview => _gesture == Gesture.Box ? InkRect.Between(_start, _last) : InkRect.Empty;
    internal Point? EraserCursor => _cursorInside && (_gesture == Gesture.Erase || Tool is InkTool.PointEraser or InkTool.StrokeEraser) ? _cursor : null;
    internal static InkVector[] HandleWorldPoints(InkRect r) => [new(r.X, r.Y), new(r.Center.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Center.Y), new(r.Right, r.Bottom), new(r.Center.X, r.Bottom), new(r.X, r.Bottom), new(r.X, r.Center.Y)];
    public Point[] GetSelectionHandles()
    {
        var r = Session.SelectionBounds; if (r.IsEmpty) return [];
        if (HasTransformPreview) r = _previewTransform.Apply(r);
        var points = HandleWorldPoints(r).Select(DocumentToView).ToList();
        var top = DocumentToView(new(r.Center.X, r.Y)); points.Add(new(top.X, top.Y - 28)); return points.ToArray();
    }
    private int HitHandle(Point point)
    {
        if (!ShowSelection || EffectiveReadOnly) return -1;
        var handles = GetSelectionHandles(); for (var i = 0; i < handles.Length; i++) if (Distance(handles[i], point) <= 8) return i;
        return -1;
    }
}
