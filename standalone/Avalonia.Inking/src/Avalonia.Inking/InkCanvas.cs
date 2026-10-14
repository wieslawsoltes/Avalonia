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

    public static readonly RoutedEvent<InkStrokeEventArgs> StrokeCollectingEvent = RoutedEvent.Register<InkCanvas, InkStrokeEventArgs>(nameof(StrokeCollecting), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<InkStrokeEventArgs> StrokeCollectedEvent = RoutedEvent.Register<InkCanvas, InkStrokeEventArgs>(nameof(StrokeCollected), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<InkDocumentEventArgs> DocumentChangedEvent = RoutedEvent.Register<InkCanvas, InkDocumentEventArgs>(nameof(DocumentChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent = RoutedEvent.Register<InkCanvas, RoutedEventArgs>(nameof(SelectionChanged), RoutingStrategies.Bubble);
    public event EventHandler<InkStrokeEventArgs>? StrokeCollecting { add => AddHandler(StrokeCollectingEvent, value); remove => RemoveHandler(StrokeCollectingEvent, value); }
    public event EventHandler<InkStrokeEventArgs>? StrokeCollected { add => AddHandler(StrokeCollectedEvent, value); remove => RemoveHandler(StrokeCollectedEvent, value); }
    public event EventHandler<InkDocumentEventArgs>? DocumentChanged { add => AddHandler(DocumentChangedEvent, value); remove => RemoveHandler(DocumentChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }

    public InkCanvas()
    {
        Focusable = true; ClipToBounds = true;
        InitializeCommands();
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        CancelGesture();
        if (_presenter is not null) _presenter.Owner = null;
        if (_adorner is not null) _adorner.Owner = null;
        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<InkPresenter>("PART_Presenter");
        _adorner = e.NameScope.Find<InkAdornerPresenter>("PART_Adorner");
        if (_presenter is not null) _presenter.Owner = this;
        if (_adorner is not null) _adorner.Owner = this;
        Refresh();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _attached = true; SubscribeSession(); Refresh();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture(); _attached = false; SubscribeSession();
        _presenter?.ClearCache(); base.OnDetachedFromVisualTree(e);
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
    private void Mutate(Action action)
    {
        _mutatingSession = true;
        try { action(); } finally { _mutatingSession = false; }
    }
    private void Refresh()
    {
        if (_commands is null) return;
        foreach (var command in _commands) command.RaiseCanExecuteChanged();
        PseudoClasses.Set(":inking", _gesture is Gesture.Ink or Gesture.Shape);
        PseudoClasses.Set(":selecting", _gesture is Gesture.Box or Gesture.Lasso or Gesture.Move or Gesture.Resize or Gesture.Rotate);
        PseudoClasses.Set(":erasing", _gesture == Gesture.Erase);
        PseudoClasses.Set(":panning", _gesture is Gesture.Pan or Gesture.Pinch);
        PseudoClasses.Set(":read-only", EffectiveReadOnly);
        PseudoClasses.Set(":has-selection", Session.Selection.Count > 0);
        UpdateCursor(); _presenter?.InvalidateVisual(); _adorner?.InvalidateVisual();
    }
    private void UpdateCursor()
    {
        var type = _spaceDown || Tool == InkTool.Pan || _gesture is Gesture.Pan or Gesture.Pinch ? StandardCursorType.Hand
            : Tool is InkTool.Select or InkTool.Lasso ? StandardCursorType.Arrow : StandardCursorType.Cross;
        if (_cursorType != type) { _cursorType = type; Cursor = new Cursor(type); }
    }
    private StandardCursorType? _cursorType;

    public void CancelGesture()
    {
        var active = _gesture != Gesture.None;
        _gesture = Gesture.None;
        var transaction = _eraseTransaction; _eraseTransaction = null;
        if (transaction is not null) Mutate(transaction.Dispose);
        _builder = null; _preview = null; _previewDirty = false; _lasso.Clear(); _previewTransform = InkTransform.Identity;
        _gestureSession = null; _gestureDocument = null; _penActive = false;
        ReleasePointers();
        if (active) { CancelledGestureCount++; SetStatus("Gesture cancelled"); }
        Refresh();
    }
    private void ReleasePointers()
    {
        _releasing = true;
        try
        {
            _pointer?.Capture(null); _pointer = null;
            foreach (var touch in _touches.Values) touch.Pointer.Capture(null);
            _touches.Clear();
        }
        finally { _releasing = false; }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_releasing) CancelGesture();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _cursorInside = false; _adorner?.InvalidateVisual(); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _cursorInside = true; }

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
        // Many pen drivers report zero on release; retain the last contact pressure at the final endpoint.
        if (final && pressure == 0 && _builder is { Count: > 0 }) pressure = LastPressure;
        SetAndRaise(LastPressureProperty, ref _lastPressure, pressure);
        return new(world.X, world.Y, pressure, Safe(properties.XTilt, -90, 90, 0), Safe(properties.YTilt, -90, 90, 0), Safe(properties.Twist, 0, 360, 0), timestamp);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Run("Pointer press", () => BeginPointer(e));
    }
    private void BeginPointer(PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this); var type = e.Pointer.Type;
        if (type == PointerType.Mouse && !p.Properties.IsLeftButtonPressed && !p.Properties.IsMiddleButtonPressed) return;
        if (type == PointerType.Touch)
        {
            var contact = p.Properties.ContactRect;
            if (PalmRejection && (_penActive || ((Environment.TickCount64 - _lastPenTick) < 350 && Math.Max(contact.Width, contact.Height) > 25))) return;
            if (_gesture != Gesture.None && _pointer?.Type != PointerType.Touch) return;
            _touches[e.Pointer.Id] = (e.Pointer, p.Position); e.Pointer.Capture(this);
            if (_touches.Count >= 2)
            {
                _builder = null; _preview = null; _previewDirty = false;
                var transaction = _eraseTransaction; _eraseTransaction = null; if (transaction is not null) Mutate(transaction.Dispose);
                _gesture = Gesture.Pinch;
                var pair = _touches.Values.Take(2).Select(t => t.Point).ToArray();
                _pinchDistance = Math.Max(1, (pair[1] - pair[0]).Length); _pinchZoom = Zoom;
                _pinchAnchor = ViewToDocument(new((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2));
                e.Handled = true; Refresh(); return;
            }
        }
        else if (_gesture != Gesture.None) return;
        Focus(); _pointer = e.Pointer; e.Pointer.Capture(this);
        _gestureSession = Session; _gestureDocument = Session.Document;
        _startScreen = p.Position; _cursor = p.Position; _start = _last = ViewToDocument(p.Position); _panStart = PanOffset;
        _additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift); _previewTransform = InkTransform.Identity;
        LastPointerKind = type.ToString();
        if (type == PointerType.Pen) { _penActive = true; _lastPenTick = Environment.TickCount64; }
        _gestureTool = type == PointerType.Pen && (p.Properties.IsEraser || p.Properties.IsInverted || p.Properties.IsBarrelButtonPressed) ? InkTool.PointEraser : Tool;
        if (_spaceDown || _gestureTool == InkTool.Pan || p.Properties.IsMiddleButtonPressed || (type == PointerType.Touch && !AcceptsTouchInk))
        { _gesture = Gesture.Pan; e.Handled = true; Refresh(); return; }
        if (_gestureTool is InkTool.Select or InkTool.Lasso)
        {
            var handle = HitHandle(p.Position);
            if (!EffectiveReadOnly && handle >= 0)
            {
                _handle = handle; _selectionStart = Session.SelectionBounds; _gesture = handle == 8 ? Gesture.Rotate : Gesture.Resize;
            }
            else
            {
                var hit = Session.HitTest(_start, 5 / Zoom);
                if (_gestureTool == InkTool.Select && hit is { } id)
                {
                    Mutate(() => Session.SetSelection(_additive ? Session.Selection.Contains(id) ? Session.Selection.Remove(id) : Session.Selection.Add(id) : Session.Selection.Contains(id) ? Session.Selection : [id]));
                    _selectionStart = Session.SelectionBounds; _gesture = EffectiveReadOnly ? Gesture.None : Gesture.Move;
                }
                else
                {
                    if (!_additive) Mutate(Session.ClearSelection);
                    _gesture = _gestureTool == InkTool.Lasso ? Gesture.Lasso : Gesture.Box;
                    _lasso.Clear(); _lasso.Add(_start);
                }
            }
        }
        else if (!EffectiveReadOnly)
        {
            if (_gestureTool is InkTool.PointEraser or InkTool.StrokeEraser)
            {
                _gesture = Gesture.Erase;
                Mutate(() => _eraseTransaction = Session.BeginTransaction("Erase gesture"));
                Mutate(() => Session.Erase(_start, _start, EraserSize / 2, _gestureTool == InkTool.PointEraser));
            }
            else if (Session.CanWriteActiveLayer)
            {
                _start = Constrain(_start, false);
                if (_gestureTool is InkTool.Line or InkTool.Rectangle or InkTool.Ellipse)
                { _gesture = Gesture.Shape; _preview = InkGeometry.Shape(_gestureTool, _start, _start, GetCurrentBrush()); }
                else
                {
                    _gesture = Gesture.Ink; _builder = new(GetCurrentBrush(), Smoothing);
                    _builder.Add(Sample(p, type, e.Timestamp)); AcceptedInputSamples++; _previewDirty = true;
                }
            }
            else SetStatus("Choose a visible, unlocked layer");
        }
        if (_gesture == Gesture.None) ReleasePointers();
        e.Handled = true; Refresh();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); _cursor = e.GetPosition(this); _cursorInside = true;
        Run("Pointer move", () => MovePointer(e));
    }
    private void MovePointer(PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch && _touches.ContainsKey(e.Pointer.Id))
        {
            _touches[e.Pointer.Id] = (e.Pointer, e.GetPosition(this));
            if (_gesture == Gesture.Pinch)
            {
                var pair = _touches.Values.Take(2).Select(t => t.Point).ToArray();
                if (pair.Length == 2)
                {
                    var zoom = Math.Clamp(_pinchZoom * (pair[1] - pair[0]).Length / _pinchDistance, .1, 8);
                    var center = new Point((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2);
                    SetCurrentValue(ZoomProperty, zoom); SetCurrentValue(PanOffsetProperty, new Vector(center.X - _pinchAnchor.X * zoom, center.Y - _pinchAnchor.Y * zoom));
                }
                e.Handled = true; return;
            }
        }
        if (_pointer?.Id != e.Pointer.Id || _gesture == Gesture.None) { _adorner?.InvalidateVisual(); return; }
        var p = e.GetCurrentPoint(this); var world = ViewToDocument(p.Position); var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_gesture == Gesture.Pan) SetCurrentValue(PanOffsetProperty, _panStart + (p.Position - _startScreen));
        else if (_gesture == Gesture.Ink && _builder is not null)
        {
            foreach (var sample in e.GetIntermediatePoints(this)) if (_builder.Add(Sample(sample, e.Pointer.Type, e.Timestamp))) { AcceptedInputSamples++; _previewDirty = true; }
            if (_builder.Add(Sample(p, e.Pointer.Type, e.Timestamp))) { AcceptedInputSamples++; _previewDirty = true; }
        }
        else if (_gesture == Gesture.Shape) _preview = InkGeometry.Shape(_gestureTool, _start, Constrain(world, false), GetCurrentBrush(), shift);
        else if (_gesture == Gesture.Erase) Mutate(() => Session.Erase(_last, world, EraserSize / 2, _gestureTool == InkTool.PointEraser));
        else if (_gesture == Gesture.Lasso && (world - _lasso[^1]).Length * Zoom > 2) _lasso.Add(world);
        else if (_gesture == Gesture.Move)
        {
            var delta = world - _start;
            if (shift) delta = Math.Abs(delta.X) > Math.Abs(delta.Y) ? new(delta.X, 0) : new(0, delta.Y);
            if (SnapToGrid) delta = new(Math.Round(delta.X / GridSpacing) * GridSpacing, Math.Round(delta.Y / GridSpacing) * GridSpacing);
            _previewTransform = InkTransform.Translation(delta.X, delta.Y);
        }
        else if (_gesture == Gesture.Resize)
        {
            var handles = HandleWorldPoints(_selectionStart); var anchor = handles[(_handle + 4) % 8]; var initial = handles[_handle];
            var sx = _handle is 1 or 5 ? 1 : Math.Clamp((world.X - anchor.X) / Math.Max(.001, Math.Abs(initial.X - anchor.X)) * Math.Sign(initial.X - anchor.X), .05, 100);
            var sy = _handle is 3 or 7 ? 1 : Math.Clamp((world.Y - anchor.Y) / Math.Max(.001, Math.Abs(initial.Y - anchor.Y)) * Math.Sign(initial.Y - anchor.Y), .05, 100);
            if (shift) sx = sy = Math.Max(sx, sy);
            _previewTransform = InkTransform.Scale(sx, sy, anchor);
        }
        else if (_gesture == Gesture.Rotate)
        {
            var center = _selectionStart.Center; var a = world - center; var b = _start - center;
            var angle = Math.Atan2(a.Y, a.X) - Math.Atan2(b.Y, b.X);
            if (shift) angle = Math.Round(angle / (Math.PI / 12)) * Math.PI / 12;
            _previewTransform = InkTransform.Rotation(angle, center);
        }
        _last = world; e.Handled = true; _presenter?.InvalidateVisual(); _adorner?.InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); Run("Pointer release", () => EndPointer(e));
    }
    private void EndPointer(PointerReleasedEventArgs e)
    {
        if (_gesture == Gesture.Pinch && _touches.Remove(e.Pointer.Id))
        {
            _releasing = true; e.Pointer.Capture(null); _releasing = false;
            if (_touches.Count == 1)
            {
                var remaining = _touches.Values.First(); _pointer = remaining.Pointer; _startScreen = remaining.Point; _panStart = PanOffset; _gesture = Gesture.Pan;
            }
            else if (_touches.Count == 0) { _gesture = Gesture.None; ReleasePointers(); }
            e.Handled = true; Refresh(); return;
        }
        if (_pointer?.Id != e.Pointer.Id) return;
        var kind = _gesture; var point = e.GetCurrentPoint(this); var world = ViewToDocument(point.Position);
        if (kind == Gesture.Ink && _builder is not null)
        { _builder.Add(Sample(point, e.Pointer.Type, e.Timestamp, true), true); _preview = _builder.Build(); }
        if (kind == Gesture.Shape) _preview = InkGeometry.Shape(_gestureTool, _start, Constrain(world, false), GetCurrentBrush(), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (kind == Gesture.Erase) Mutate(() => Session.Erase(_last, world, EraserSize / 2, _gestureTool == InkTool.PointEraser));
        var stroke = _preview;
        _gesture = Gesture.None;
        if (kind is Gesture.Ink or Gesture.Shape && stroke is not null && !EffectiveReadOnly)
        {
            var collecting = new InkStrokeEventArgs(StrokeCollectingEvent, stroke); RaiseEvent(collecting);
            if (!collecting.Cancel && ReferenceEquals(Session, _gestureSession) && ReferenceEquals(Session.Document, _gestureDocument) && Session.CanWriteActiveLayer)
            {
                Mutate(() => Session.AddStroke(stroke)); CollectedStrokeCount++;
                RaiseEvent(new InkStrokeEventArgs(StrokeCollectedEvent, stroke)); SetStatus($"Collected {stroke.Points.Length:N0} samples");
            }
        }
        else if (kind == Gesture.Erase) { var t = _eraseTransaction; _eraseTransaction = null; if (t is not null) Mutate(t.Commit); }
        else if (kind == Gesture.Box) Mutate(() => Session.SelectRectangle(InkRect.Between(_start, world), _additive));
        else if (kind == Gesture.Lasso) { _lasso.Add(world); Mutate(() => Session.SelectPolygon(_lasso, _additive)); }
        else if (kind is Gesture.Move or Gesture.Resize or Gesture.Rotate && !EffectiveReadOnly) Mutate(() => Session.TransformSelection(_previewTransform));
        _builder = null; _preview = null; _previewDirty = false; _lasso.Clear(); _previewTransform = InkTransform.Identity;
        _gestureSession = null; _gestureDocument = null; _penActive = false; ReleasePointers(); e.Handled = true; Refresh();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_gesture != Gesture.None) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) ZoomAt(e.GetPosition(this), Math.Pow(1.13, e.Delta.Y));
        else SetCurrentValue(PanOffsetProperty, PanOffset + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y * 36, 0) : new Vector(e.Delta.X * 36, e.Delta.Y * 36)));
        e.Handled = true;
    }

    internal InkStroke? LiveStroke
    {
        get { if (_previewDirty && _builder is { Count: > 0 }) { _preview = _builder.Build(); _previewDirty = false; } return _preview; }
    }
    internal bool HasTransformPreview => _gesture is Gesture.Move or Gesture.Resize or Gesture.Rotate;
    internal InkTransform PreviewTransform => _previewTransform;
    internal IReadOnlyList<InkVector> LassoPreview => _gesture == Gesture.Lasso ? _lasso : [];
    internal InkRect BoxPreview => _gesture == Gesture.Box ? InkRect.Between(_start, _last) : InkRect.Empty;
    internal Point? EraserCursor => _cursorInside && (_gesture == Gesture.Erase || Tool is InkTool.PointEraser or InkTool.StrokeEraser) ? _cursor : null;
    internal static InkVector[] HandleWorldPoints(InkRect r) => [new(r.X, r.Y), new(r.Center.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Center.Y), new(r.Right, r.Bottom), new(r.Center.X, r.Bottom), new(r.X, r.Bottom), new(r.X, r.Center.Y)];
    public Point[] GetSelectionHandles()
    {
        var r = Session.SelectionBounds;
        if (r.IsEmpty) return [];
        if (HasTransformPreview) r = _previewTransform.Apply(r);
        var points = HandleWorldPoints(r).Select(DocumentToView).ToList();
        var top = DocumentToView(new(r.Center.X, r.Y)); points.Add(new(top.X, top.Y - 28)); return points.ToArray();
    }
    private int HitHandle(Point point)
    {
        if (!ShowSelection || EffectiveReadOnly) return -1;
        var handles = GetSelectionHandles();
        for (var i = 0; i < handles.Length; i++) if ((handles[i] - point).Length <= 8) return i;
        return -1;
    }
}
