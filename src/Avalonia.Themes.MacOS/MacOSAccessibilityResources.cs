using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avalonia.Themes.MacOS;

// Owned by the same host as the theme. Native event subscriptions are detached
// with the theme, including when ControlCatalog switches themes at runtime.
internal sealed class MacOSAccessibilityResources(MacOSTheme theme) : ResourceProvider
{
    private IPlatformSettings? _settings;

    public override bool HasResources => true;

    internal void Invalidate() => RaiseResourcesChanged();

    public override bool TryGetResource(object key, ThemeVariant? variant, out object? value)
    {
        value = null;
        if (key is not string text)
            return false;

        var contrast = theme.IncreaseContrast ??
            (_settings?.GetColorValues().ContrastPreference == ColorContrastPreference.High);
        var dark = IsDark(variant);

        if (theme.ReduceMotion && (text is "MacOS.Motion.Interaction" or "MacOS.Motion.Switch"
            or "MacOS.SplitViewPaneAnimationOpenDuration" or "MacOS.SplitViewPaneAnimationCloseDuration"))
        {
            value = TimeSpan.Zero;
            return true;
        }

        // These inherited component resources are Color, not IBrush. Resolve
        // their semantic roles without changing the public CLR token contract.
        var expanderRole = text switch
        {
            "MacOS.ExpanderHeaderBackground" or "MacOS.ExpanderContentBackground" => "Surface",
            "MacOS.ExpanderHeaderBackgroundPointerOver" => "ControlHover",
            "MacOS.ExpanderHeaderBackgroundPressed" => "ControlPressed",
            "MacOS.ExpanderHeaderBackgroundDisabled" => "ControlDisabled",
            "MacOS.ExpanderHeaderBorderBrush" or "MacOS.ExpanderHeaderBorderBrushPointerOver"
                or "MacOS.ExpanderHeaderBorderBrushPressed" or "MacOS.ExpanderContentBorderBrush" => "Separator",
            "MacOS.ExpanderHeaderForeground" or "MacOS.ExpanderHeaderForegroundPointerOver"
                or "MacOS.ExpanderHeaderForegroundPressed" or "MacOS.ExpanderChevronForeground"
                or "MacOS.ExpanderChevronForegroundPointerOver" or "MacOS.ExpanderChevronForegroundPressed" => "Label",
            _ => null
        };
        if (expanderRole is not null && !theme.Tokens.TryGetResource(text, variant, out _))
            return ((IResourceNode)theme).TryGetResource("MacOS.Color." + expanderRole, variant, out value);

        if (text == "MacOS.Color.Accent")
        {
            // Explicit semantic overrides remain authoritative. Otherwise the raw
            // OS accent is preserved as SystemAccentColor, while filled surfaces
            // use a contrast-safe derivative for their small control labels.
            if (theme.Tokens.TryGetResource(text, variant, out _))
                return false;
            if (((IResourceNode)theme).TryGetResource("MacOS.SystemAccentColor", variant, out var raw)
                && raw is Color source)
            {
                var accent = Color.FromRgb(source.R, source.G, source.B);
                var luminance = Luminance(accent);
                // Bright accents retain black labels; medium/dark accents favor
                // white labels with at least 4.5:1 contrast on opaque surfaces.
                if (luminance < 0.4)
                {
                    while (Luminance(accent) > 0.175)
                        accent = Color.FromArgb(255, (byte)(accent.R * 0.96),
                            (byte)(accent.G * 0.96), (byte)(accent.B * 0.96));
                }
                value = accent;
                return true;
            }
        }

        if (text == "MacOS.Color.OnAccent")
        {
            // Prefer the actual resolved accent (including per-variant overrides)
            // to keep text legible when the user chooses e.g. a yellow OS accent.
            if (((IResourceNode)theme).TryGetResource("MacOS.Color.Accent", variant, out var resource)
                && resource is Color accent)
            {
                var luminance = 0.2126 * Linear(accent.R) + 0.7152 * Linear(accent.G) + 0.0722 * Linear(accent.B);
                value = luminance > 0.179 ? Colors.Black : Colors.White;
                return true;
            }
        }

        if ((theme.ReduceTransparency || contrast) && text == "MacOS.Color.Material")
        {
            value = Color.Parse(dark ? "#303034" : "#FAFAFC");
            return true;
        }

        if (contrast)
        {
            value = text switch
            {
                "MacOS.Color.Label" => dark ? Colors.White : Colors.Black,
                "MacOS.Color.SecondaryLabel" => Color.Parse(dark ? "#E5E5EA" : "#333336"),
                "MacOS.Color.DisabledLabel" => Color.Parse(dark ? "#B9B9C0" : "#616167"),
                "MacOS.Color.Separator" or "MacOS.Color.ControlStroke" => Color.Parse(dark ? "#DDDDDF" : "#414145"),
                "MacOS.SystemControlFocusVisualPrimaryThickness" => new Thickness(4),
                _ => null
            };
        }
        return value is not null;
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte component)
    {
        var value = component / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static bool IsDark(ThemeVariant? variant)
    {
        for (var current = variant; current is not null; current = current.InheritVariant)
        {
            if (current == ThemeVariant.Dark)
                return true;
            if (current == ThemeVariant.Light)
                return false;
        }
        return false;
    }

    protected override void OnAddOwner(IResourceHost owner)
    {
        base.OnAddOwner(owner);
        _settings = owner switch
        {
            Application app => app.PlatformSettings,
            Visual visual => visual.GetPlatformSettings(),
            _ => null
        };
        if (_settings is not null)
            _settings.ColorValuesChanged += OnColorsChanged;
        RaiseResourcesChanged();
    }

    protected override void OnRemoveOwner(IResourceHost owner)
    {
        if (_settings is not null)
            _settings.ColorValuesChanged -= OnColorsChanged;
        _settings = null;
        base.OnRemoveOwner(owner);
    }

    private void OnColorsChanged(object? sender, PlatformColorValues e) => RaiseResourcesChanged();
}
