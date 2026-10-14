using Avalonia.Inking.Core;
using Avalonia.Input;
namespace Avalonia.Inking;
public partial class InkCanvas
{
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Run("Pointer press", () => BeginPointer(e)); }
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
                _pinchDistance = Math.Max(1, Distance(pair[1], pair[0])); _pinchZoom = Zoom;
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
        _gestureBrush = GetCurrentBrush();
        if (_spaceDown || _gestureTool == InkTool.Pan || p.Properties.IsMiddleButtonPressed || (type == PointerType.Touch && !AcceptsTouchInk))
        { _gesture = Gesture.Pan; e.Handled = true; Refresh(); return; }
        if (_gestureTool is InkTool.Select or InkTool.Lasso)
        {
            var handle = HitHandle(p.Position);
            if (!EffectiveReadOnly && handle >= 0) { _handle = handle; _selectionStart = Session.SelectionBounds; _gesture = handle == 8 ? Gesture.Rotate : Gesture.Resize; }
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
                    _gesture = _gestureTool == InkTool.Lasso ? Gesture.Lasso : Gesture.Box; _lasso.Clear(); _lasso.Add(_start);
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
                { _gesture = Gesture.Shape; _preview = InkGeometry.Shape(_gestureTool, _start, _start, _gestureBrush); }
                else
                {
                    _gesture = Gesture.Ink; _builder = new(_gestureBrush, Smoothing);
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
        if (e.Pointer.Type == PointerType.Pen) _lastPenTick = Environment.TickCount64;
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
                    var zoom = Math.Clamp(_pinchZoom * Distance(pair[1], pair[0]) / _pinchDistance, .1, 8);
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
        else if (_gesture == Gesture.Shape) _preview = InkGeometry.Shape(_gestureTool, _start, Constrain(world, false), _gestureBrush, shift);
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
        _last = world; e.Handled = true;
        // Retained ink does not change while collecting a new stroke. Only the overlay needs a frame.
        if (_gesture is not Gesture.Ink and not Gesture.Shape and not Gesture.Box and not Gesture.Lasso) _presenter?.InvalidateVisual();
        _adorner?.InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); Run("Pointer release", () => EndPointer(e)); }
    private void EndPointer(PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Pen) _lastPenTick = Environment.TickCount64;
        if (_gesture == Gesture.Pinch && _touches.Remove(e.Pointer.Id))
        {
            _releasing = true; e.Pointer.Capture(null); _releasing = false;
            if (_touches.Count == 1) { var remaining = _touches.Values.First(); _pointer = remaining.Pointer; _startScreen = remaining.Point; _panStart = PanOffset; _gesture = Gesture.Pan; }
            else if (_touches.Count == 0) { _gesture = Gesture.None; ReleasePointers(); }
            e.Handled = true; Refresh(); return;
        }
        if (_pointer?.Id != e.Pointer.Id) return;
        var kind = _gesture; var point = e.GetCurrentPoint(this); var world = ViewToDocument(point.Position);
        if (kind == Gesture.Ink && _builder is not null) { _builder.Add(Sample(point, e.Pointer.Type, e.Timestamp, true), true); _preview = _builder.Build(); }
        if (kind == Gesture.Shape) _preview = InkGeometry.Shape(_gestureTool, _start, Constrain(world, false), _gestureBrush, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (kind == Gesture.Erase) Mutate(() => Session.Erase(_last, world, EraserSize / 2, _gestureTool == InkTool.PointEraser));
        var stroke = _preview; _gesture = Gesture.None;
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
        base.OnPointerWheelChanged(e); if (_gesture != Gesture.None) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) ZoomAt(e.GetPosition(this), Math.Pow(1.13, e.Delta.Y));
        else SetCurrentValue(PanOffsetProperty, PanOffset + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y * 36, 0) : new Vector(e.Delta.X * 36, e.Delta.Y * 36)));
        e.Handled = true;
    }
}
