using Avalonia.Media;

namespace Avalonia.Themes.MacOS;

/// <summary>Semantic palette tokens shared by all macOS control families.</summary>
public static class MacOSSemanticTokens
{
    /// <summary>Gets the contrast-safe accent color used for filled controls.</summary>
    public static readonly MacOSToken<Color> Accent = new("MacOS.Color.Accent");
    /// <summary>Gets the contrast-safe accent brush.</summary>
    public static readonly MacOSToken<IBrush> AccentBrush = new("MacOS.Brush.Accent");
    /// <summary>Gets the Window color token.</summary>
    public static readonly MacOSToken<Color> Window = new("MacOS.Color.Window");
    /// <summary>Gets the Window brush token.</summary>
    public static readonly MacOSToken<IBrush> WindowBrush = new("MacOS.Brush.Window");
    /// <summary>Gets the Surface color token.</summary>
    public static readonly MacOSToken<Color> Surface = new("MacOS.Color.Surface");
    /// <summary>Gets the Surface brush token.</summary>
    public static readonly MacOSToken<IBrush> SurfaceBrush = new("MacOS.Brush.Surface");
    /// <summary>Gets the Control color token.</summary>
    public static readonly MacOSToken<Color> Control = new("MacOS.Color.Control");
    /// <summary>Gets the Control brush token.</summary>
    public static readonly MacOSToken<IBrush> ControlBrush = new("MacOS.Brush.Control");
    /// <summary>Gets the ControlHover color token.</summary>
    public static readonly MacOSToken<Color> ControlHover = new("MacOS.Color.ControlHover");
    /// <summary>Gets the ControlHover brush token.</summary>
    public static readonly MacOSToken<IBrush> ControlHoverBrush = new("MacOS.Brush.ControlHover");
    /// <summary>Gets the ControlPressed color token.</summary>
    public static readonly MacOSToken<Color> ControlPressed = new("MacOS.Color.ControlPressed");
    /// <summary>Gets the ControlPressed brush token.</summary>
    public static readonly MacOSToken<IBrush> ControlPressedBrush = new("MacOS.Brush.ControlPressed");
    /// <summary>Gets the ControlDisabled color token.</summary>
    public static readonly MacOSToken<Color> ControlDisabled = new("MacOS.Color.ControlDisabled");
    /// <summary>Gets the ControlDisabled brush token.</summary>
    public static readonly MacOSToken<IBrush> ControlDisabledBrush = new("MacOS.Brush.ControlDisabled");
    /// <summary>Gets the Label color token.</summary>
    public static readonly MacOSToken<Color> Label = new("MacOS.Color.Label");
    /// <summary>Gets the Label brush token.</summary>
    public static readonly MacOSToken<IBrush> LabelBrush = new("MacOS.Brush.Label");
    /// <summary>Gets the SecondaryLabel color token.</summary>
    public static readonly MacOSToken<Color> SecondaryLabel = new("MacOS.Color.SecondaryLabel");
    /// <summary>Gets the SecondaryLabel brush token.</summary>
    public static readonly MacOSToken<IBrush> SecondaryLabelBrush = new("MacOS.Brush.SecondaryLabel");
    /// <summary>Gets the DisabledLabel color token.</summary>
    public static readonly MacOSToken<Color> DisabledLabel = new("MacOS.Color.DisabledLabel");
    /// <summary>Gets the DisabledLabel brush token.</summary>
    public static readonly MacOSToken<IBrush> DisabledLabelBrush = new("MacOS.Brush.DisabledLabel");
    /// <summary>Gets the Separator color token.</summary>
    public static readonly MacOSToken<Color> Separator = new("MacOS.Color.Separator");
    /// <summary>Gets the Separator brush token.</summary>
    public static readonly MacOSToken<IBrush> SeparatorBrush = new("MacOS.Brush.Separator");
    /// <summary>Gets the ControlStroke color token.</summary>
    public static readonly MacOSToken<Color> ControlStroke = new("MacOS.Color.ControlStroke");
    /// <summary>Gets the ControlStroke brush token.</summary>
    public static readonly MacOSToken<IBrush> ControlStrokeBrush = new("MacOS.Brush.ControlStroke");
    /// <summary>Gets the SelectionInactive color token.</summary>
    public static readonly MacOSToken<Color> SelectionInactive = new("MacOS.Color.SelectionInactive");
    /// <summary>Gets the SelectionInactive brush token.</summary>
    public static readonly MacOSToken<IBrush> SelectionInactiveBrush = new("MacOS.Brush.SelectionInactive");
    /// <summary>Gets the Hover color token.</summary>
    public static readonly MacOSToken<Color> Hover = new("MacOS.Color.Hover");
    /// <summary>Gets the Hover brush token.</summary>
    public static readonly MacOSToken<IBrush> HoverBrush = new("MacOS.Brush.Hover");
    /// <summary>Gets the Track color token.</summary>
    public static readonly MacOSToken<Color> Track = new("MacOS.Color.Track");
    /// <summary>Gets the Track brush token.</summary>
    public static readonly MacOSToken<IBrush> TrackBrush = new("MacOS.Brush.Track");
    /// <summary>Gets the Knob color token.</summary>
    public static readonly MacOSToken<Color> Knob = new("MacOS.Color.Knob");
    /// <summary>Gets the Knob brush token.</summary>
    public static readonly MacOSToken<IBrush> KnobBrush = new("MacOS.Brush.Knob");
    /// <summary>Gets the Destructive color token.</summary>
    public static readonly MacOSToken<Color> Destructive = new("MacOS.Color.Destructive");
    /// <summary>Gets the Destructive brush token.</summary>
    public static readonly MacOSToken<IBrush> DestructiveBrush = new("MacOS.Brush.Destructive");
    /// <summary>Gets the Success color token.</summary>
    public static readonly MacOSToken<Color> Success = new("MacOS.Color.Success");
    /// <summary>Gets the Success brush token.</summary>
    public static readonly MacOSToken<IBrush> SuccessBrush = new("MacOS.Brush.Success");
    /// <summary>Gets the Warning color token.</summary>
    public static readonly MacOSToken<Color> Warning = new("MacOS.Color.Warning");
    /// <summary>Gets the Warning brush token.</summary>
    public static readonly MacOSToken<IBrush> WarningBrush = new("MacOS.Brush.Warning");
    /// <summary>Gets the Material color token.</summary>
    public static readonly MacOSToken<Color> Material = new("MacOS.Color.Material");
    /// <summary>Gets the Material brush token.</summary>
    public static readonly MacOSToken<IBrush> MaterialBrush = new("MacOS.Brush.Material");
    /// <summary>Gets the MaterialOpaque color token.</summary>
    public static readonly MacOSToken<Color> MaterialOpaque = new("MacOS.Color.MaterialOpaque");
    /// <summary>Gets the MaterialOpaque brush token.</summary>
    public static readonly MacOSToken<IBrush> MaterialOpaqueBrush = new("MacOS.Brush.MaterialOpaque");
    /// <summary>Gets the OnAccent color token.</summary>
    public static readonly MacOSToken<Color> OnAccent = new("MacOS.Color.OnAccent");
    /// <summary>Gets the OnAccent brush token.</summary>
    public static readonly MacOSToken<IBrush> OnAccentBrush = new("MacOS.Brush.OnAccent");
    /// <summary>Gets the Transparent color token.</summary>
    public static readonly MacOSToken<Color> Transparent = new("MacOS.Color.Transparent");
    /// <summary>Gets the Transparent brush token.</summary>
    public static readonly MacOSToken<IBrush> TransparentBrush = new("MacOS.Brush.Transparent");
}
