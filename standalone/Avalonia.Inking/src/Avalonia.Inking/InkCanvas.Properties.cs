using Avalonia.Inking.Core;
using Avalonia.Media;

namespace Avalonia.Inking;

public partial class InkCanvas
{
    public static readonly StyledProperty<InkSession?> SessionProperty = AvaloniaProperty.Register<InkCanvas, InkSession?>(nameof(Session));
    public static readonly StyledProperty<InkTool> ToolProperty = AvaloniaProperty.Register<InkCanvas, InkTool>(nameof(Tool), InkTool.Pen, validate: Enum.IsDefined);
    public static readonly StyledProperty<Color> InkColorProperty = AvaloniaProperty.Register<InkCanvas, Color>(nameof(InkColor), Color.FromRgb(70, 87, 213));
    public static readonly StyledProperty<double> BrushSizeProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(BrushSize), 3, validate: v => double.IsFinite(v) && v is >= .1 and <= 512);
    public static readonly StyledProperty<double> BrushOpacityProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(BrushOpacity), 1, validate: v => double.IsFinite(v) && v is >= 0 and <= 1);
    public static readonly StyledProperty<bool> UsePressureProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(UsePressure), true);
    public static readonly StyledProperty<double> PressureCurveProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(PressureCurve), .7, validate: v => double.IsFinite(v) && v is >= .1 and <= 4);
    public static readonly StyledProperty<double> SmoothingProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(Smoothing), .4, validate: v => double.IsFinite(v) && v is >= 0 and <= 1);
    public static readonly StyledProperty<double> EraserSizeProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(EraserSize), 24, validate: v => double.IsFinite(v) && v is >= 1 and <= 512);
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> AcceptsTouchInkProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(AcceptsTouchInk));
    public static readonly StyledProperty<bool> PalmRejectionProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(PalmRejection), true);
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(Zoom), 1, validate: v => double.IsFinite(v) && v is >= .1 and <= 8);
    public static readonly StyledProperty<Vector> PanOffsetProperty = AvaloniaProperty.Register<InkCanvas, Vector>(nameof(PanOffset), default, validate: v => double.IsFinite(v.X) && double.IsFinite(v.Y) && Math.Abs(v.X) <= 100_000_000 && Math.Abs(v.Y) <= 100_000_000);
    public static readonly StyledProperty<InkGrid> GridModeProperty = AvaloniaProperty.Register<InkCanvas, InkGrid>(nameof(GridMode), InkGrid.Dots, validate: Enum.IsDefined);
    public static readonly StyledProperty<double> GridSpacingProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(GridSpacing), 24, validate: v => double.IsFinite(v) && v is >= 4 and <= 256);
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<InkCanvas, IBrush?>(nameof(GridBrush), new SolidColorBrush(Color.FromRgb(213, 218, 229)));
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty = AvaloniaProperty.Register<InkCanvas, IBrush?>(nameof(SelectionBrush), new SolidColorBrush(Color.FromRgb(70, 87, 213)));
    public static readonly StyledProperty<bool> ShowSelectionProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(ShowSelection), true);
    public static readonly StyledProperty<bool> SnapToGridProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(SnapToGrid));
    public static readonly StyledProperty<bool> RulerEnabledProperty = AvaloniaProperty.Register<InkCanvas, bool>(nameof(RulerEnabled));
    public static readonly StyledProperty<double> RulerAngleProperty = AvaloniaProperty.Register<InkCanvas, double>(nameof(RulerAngle), 0, validate: double.IsFinite);
    public static readonly StyledProperty<Point> RulerOriginProperty = AvaloniaProperty.Register<InkCanvas, Point>(nameof(RulerOrigin), new Point(0, 240));
    public static readonly DirectProperty<InkCanvas, string> StatusTextProperty = AvaloniaProperty.RegisterDirect<InkCanvas, string>(nameof(StatusText), c => c.StatusText);
    public static readonly DirectProperty<InkCanvas, double> LastPressureProperty = AvaloniaProperty.RegisterDirect<InkCanvas, double>(nameof(LastPressure), c => c.LastPressure);
    private string _statusText = "Ready";
    private double _lastPressure;
    public InkSession Session { get => GetValue(SessionProperty) ?? _ownedSession; set => SetValue(SessionProperty, value); }
    public InkTool Tool { get => GetValue(ToolProperty); set => SetValue(ToolProperty, value); }
    public Color InkColor { get => GetValue(InkColorProperty); set => SetValue(InkColorProperty, value); }
    public double BrushSize { get => GetValue(BrushSizeProperty); set => SetValue(BrushSizeProperty, value); }
    public double BrushOpacity { get => GetValue(BrushOpacityProperty); set => SetValue(BrushOpacityProperty, value); }
    public bool UsePressure { get => GetValue(UsePressureProperty); set => SetValue(UsePressureProperty, value); }
    public double PressureCurve { get => GetValue(PressureCurveProperty); set => SetValue(PressureCurveProperty, value); }
    public double Smoothing { get => GetValue(SmoothingProperty); set => SetValue(SmoothingProperty, value); }
    public double EraserSize { get => GetValue(EraserSizeProperty); set => SetValue(EraserSizeProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool AcceptsTouchInk { get => GetValue(AcceptsTouchInkProperty); set => SetValue(AcceptsTouchInkProperty, value); }
    public bool PalmRejection { get => GetValue(PalmRejectionProperty); set => SetValue(PalmRejectionProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public Vector PanOffset { get => GetValue(PanOffsetProperty); set => SetValue(PanOffsetProperty, value); }
    public InkGrid GridMode { get => GetValue(GridModeProperty); set => SetValue(GridModeProperty, value); }
    public double GridSpacing { get => GetValue(GridSpacingProperty); set => SetValue(GridSpacingProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public bool ShowSelection { get => GetValue(ShowSelectionProperty); set => SetValue(ShowSelectionProperty, value); }
    public bool SnapToGrid { get => GetValue(SnapToGridProperty); set => SetValue(SnapToGridProperty, value); }
    public bool RulerEnabled { get => GetValue(RulerEnabledProperty); set => SetValue(RulerEnabledProperty, value); }
    public double RulerAngle { get => GetValue(RulerAngleProperty); set => SetValue(RulerAngleProperty, value); }
    public Point RulerOrigin { get => GetValue(RulerOriginProperty); set => SetValue(RulerOriginProperty, value); }
    public string StatusText => _statusText;
    public double LastPressure => _lastPressure;
    public bool EffectiveReadOnly => IsReadOnly || Session.IsReadOnly;
    public bool CanEdit => !EffectiveReadOnly && !Session.IsEditing && _gesture == Gesture.None;
    public string LastPointerKind { get; private set; } = "Mouse";
    public int CollectedStrokeCount { get; private set; }
    public int CancelledGestureCount { get; private set; }
    public long AcceptedInputSamples { get; private set; }
    public InkPresenter? Presenter => _presenter;
    public InkAdornerPresenter? Adorner => _adorner;
}
