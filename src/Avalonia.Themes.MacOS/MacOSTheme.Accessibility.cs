using Avalonia.Threading;

namespace Avalonia.Themes.MacOS;

public partial class MacOSTheme
{
    private readonly MacOSAccessibilityResources _accessibilityResources;
    private bool? _increaseContrast;
    private bool _reduceMotion;
    private bool _reduceTransparency;

    /// <summary>Defines the IncreaseContrast property.</summary>
    public static readonly DirectProperty<MacOSTheme, bool?> IncreaseContrastProperty =
        AvaloniaProperty.RegisterDirect<MacOSTheme, bool?>(nameof(IncreaseContrast),
            o => o.IncreaseContrast, (o, v) => o.IncreaseContrast = v);

    /// <summary>Defines the ReduceMotion property.</summary>
    public static readonly DirectProperty<MacOSTheme, bool> ReduceMotionProperty =
        AvaloniaProperty.RegisterDirect<MacOSTheme, bool>(nameof(ReduceMotion),
            o => o.ReduceMotion, (o, v) => o.ReduceMotion = v);

    /// <summary>Defines the ReduceTransparency property.</summary>
    public static readonly DirectProperty<MacOSTheme, bool> ReduceTransparencyProperty =
        AvaloniaProperty.RegisterDirect<MacOSTheme, bool>(nameof(ReduceTransparency),
            o => o.ReduceTransparency, (o, v) => o.ReduceTransparency = v);

    /// <summary>Gets or sets increased contrast. Null follows Avalonia's platform preference.</summary>
    public bool? IncreaseContrast
    {
        get => _increaseContrast;
        set { Dispatcher.UIThread.VerifyAccess(); SetAndRaise(IncreaseContrastProperty, ref _increaseContrast, value); }
    }

    /// <summary>Gets or sets reduced interaction motion. Functional progress indicators remain active.
    /// Bind this to the application's accessibility-preference service.</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set { Dispatcher.UIThread.VerifyAccess(); SetAndRaise(ReduceMotionProperty, ref _reduceMotion, value); }
    }

    /// <summary>Gets or sets opaque popup and material surfaces.
    /// Bind this to the application's accessibility-preference service.</summary>
    public bool ReduceTransparency
    {
        get => _reduceTransparency;
        set { Dispatcher.UIThread.VerifyAccess(); SetAndRaise(ReduceTransparencyProperty, ref _reduceTransparency, value); }
    }
}
