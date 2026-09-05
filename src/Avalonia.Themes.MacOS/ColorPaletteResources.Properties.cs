using Avalonia.Media;

namespace Avalonia.Themes.MacOS;

public partial class ColorPaletteResources
{
    private bool _hasAccentColor;
    private Color _accentColor;
    private Color _accentColorDark1, _accentColorDark2, _accentColorDark3;
    private Color _accentColorLight1, _accentColorLight2, _accentColorLight3;

    public static readonly DirectProperty<ColorPaletteResources, Color> AccentProperty
        = AvaloniaProperty.RegisterDirect<ColorPaletteResources, Color>(nameof(Accent), r => r.Accent, (r, v) => r.Accent = v);

    /// <summary>
    /// Gets or sets the Accent color value.
    /// </summary>
    public Color Accent
    {
        get => _accentColor;
        set => SetAndRaise(AccentProperty, ref _accentColor, value);
    }

    /// <summary>
    /// Gets or sets the AltHigh color value.
    /// </summary>
    public Color AltHigh { get => GetColor("MacOS.SystemAltHighColor"); set => SetColor("MacOS.SystemAltHighColor", value); }

    /// <summary>
    /// Gets or sets the AltLow color value.
    /// </summary>
    public Color AltLow { get => GetColor("MacOS.SystemAltLowColor"); set => SetColor("MacOS.SystemAltLowColor", value); }

    /// <summary>
    /// Gets or sets the AltMedium color value.
    /// </summary>
    public Color AltMedium { get => GetColor("MacOS.SystemAltMediumColor"); set => SetColor("MacOS.SystemAltMediumColor", value); }

    /// <summary>
    /// Gets or sets the AltMediumHigh color value.
    /// </summary>
    public Color AltMediumHigh { get => GetColor("MacOS.SystemAltMediumHighColor"); set => SetColor("MacOS.SystemAltMediumHighColor", value); }

    /// <summary>
    /// Gets or sets the AltMediumLow color value.
    /// </summary>
    public Color AltMediumLow { get => GetColor("MacOS.SystemAltMediumLowColor"); set => SetColor("MacOS.SystemAltMediumLowColor", value); }

    /// <summary>
    /// Gets or sets the BaseHigh color value.
    /// </summary>
    public Color BaseHigh { get => GetColor("MacOS.SystemBaseHighColor"); set => SetColor("MacOS.SystemBaseHighColor", value); }

    /// <summary>
    /// Gets or sets the BaseLow color value.
    /// </summary>
    public Color BaseLow { get => GetColor("MacOS.SystemBaseLowColor"); set => SetColor("MacOS.SystemBaseLowColor", value); }

    /// <summary>
    /// Gets or sets the BaseMedium color value.
    /// </summary>
    public Color BaseMedium { get => GetColor("MacOS.SystemBaseMediumColor"); set => SetColor("MacOS.SystemBaseMediumColor", value); }

    /// <summary>
    /// Gets or sets the BaseMediumHigh color value.
    /// </summary>
    public Color BaseMediumHigh { get => GetColor("MacOS.SystemBaseMediumHighColor"); set => SetColor("MacOS.SystemBaseMediumHighColor", value); }

    /// <summary>
    /// Gets or sets the BaseMediumLow color value.
    /// </summary>
    public Color BaseMediumLow { get => GetColor("MacOS.SystemBaseMediumLowColor"); set => SetColor("MacOS.SystemBaseMediumLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeAltLow color value.
    /// </summary>
    public Color ChromeAltLow { get => GetColor("MacOS.SystemChromeAltLowColor"); set => SetColor("MacOS.SystemChromeAltLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeBlackHigh color value.
    /// </summary>
    public Color ChromeBlackHigh { get => GetColor("MacOS.SystemChromeBlackHighColor"); set => SetColor("MacOS.SystemChromeBlackHighColor", value); }

    /// <summary>
    /// Gets or sets the ChromeBlackLow color value.
    /// </summary>
    public Color ChromeBlackLow { get => GetColor("MacOS.SystemChromeBlackLowColor"); set => SetColor("MacOS.SystemChromeBlackLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeBlackMedium color value.
    /// </summary>
    public Color ChromeBlackMedium { get => GetColor("MacOS.SystemChromeBlackMediumColor"); set => SetColor("MacOS.SystemChromeBlackMediumColor", value); }

    /// <summary>
    /// Gets or sets the ChromeBlackMediumLow color value.
    /// </summary>
    public Color ChromeBlackMediumLow { get => GetColor("MacOS.SystemChromeBlackMediumLowColor"); set => SetColor("MacOS.SystemChromeBlackMediumLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeDisabledHigh color value.
    /// </summary>
    public Color ChromeDisabledHigh { get => GetColor("MacOS.SystemChromeDisabledHighColor"); set => SetColor("MacOS.SystemChromeDisabledHighColor", value); }

    /// <summary>
    /// Gets or sets the ChromeDisabledLow color value.
    /// </summary>
    public Color ChromeDisabledLow { get => GetColor("MacOS.SystemChromeDisabledLowColor"); set => SetColor("MacOS.SystemChromeDisabledLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeGray color value.
    /// </summary>
    public Color ChromeGray { get => GetColor("MacOS.SystemChromeGrayColor"); set => SetColor("MacOS.SystemChromeGrayColor", value); }

    /// <summary>
    /// Gets or sets the ChromeHigh color value.
    /// </summary>
    public Color ChromeHigh { get => GetColor("MacOS.SystemChromeHighColor"); set => SetColor("MacOS.SystemChromeHighColor", value); }

    /// <summary>
    /// Gets or sets the ChromeLow color value.
    /// </summary>
    public Color ChromeLow { get => GetColor("MacOS.SystemChromeLowColor"); set => SetColor("MacOS.SystemChromeLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeMedium color value.
    /// </summary>
    public Color ChromeMedium { get => GetColor("MacOS.SystemChromeMediumColor"); set => SetColor("MacOS.SystemChromeMediumColor", value); }

    /// <summary>
    /// Gets or sets the ChromeMediumLow color value.
    /// </summary>
    public Color ChromeMediumLow { get => GetColor("MacOS.SystemChromeMediumLowColor"); set => SetColor("MacOS.SystemChromeMediumLowColor", value); }

    /// <summary>
    /// Gets or sets the ChromeWhite color value.
    /// </summary>
    public Color ChromeWhite { get => GetColor("MacOS.SystemChromeWhiteColor"); set => SetColor("MacOS.SystemChromeWhiteColor", value); }

    /// <summary>
    /// Gets or sets the ErrorText color value.
    /// </summary>
    public Color ErrorText { get => GetColor("MacOS.SystemErrorTextColor"); set => SetColor("MacOS.SystemErrorTextColor", value); }

    /// <summary>
    /// Gets or sets the ListLow color value.
    /// </summary>
    public Color ListLow { get => GetColor("MacOS.SystemListLowColor"); set => SetColor("MacOS.SystemListLowColor", value); }

    /// <summary>
    /// Gets or sets the ListMedium color value.
    /// </summary>
    public Color ListMedium { get => GetColor("MacOS.SystemListMediumColor"); set => SetColor("MacOS.SystemListMediumColor", value); }
    
    /// <summary>
    /// Gets or sets the RegionColor color value.
    /// </summary>
    public Color RegionColor { get => GetColor("MacOS.SystemRegionColor"); set => SetColor("MacOS.SystemRegionColor", value); }
}
