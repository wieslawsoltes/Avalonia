#!/usr/bin/env python3
"""Add resource-provider-based accessibility; no global mutable brushes."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def main():
    target = THEME / 'MacOSAccessibilityResources.cs'
    if target.exists():
        return
    target.write_text('''using System;
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

        if (text == "MacOS.Color.OnAccent")
        {
            // Prefer the actual resolved accent (including per-variant overrides)
            // to keep text legible when the user chooses e.g. a yellow OS accent.
            if (((IResourceNode)theme).TryGetResource("MacOS.SystemAccentColor", variant, out var resource)
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
''')
    (THEME / 'MacOSTheme.Accessibility.cs').write_text('''using Avalonia.Threading;

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
''')
    path = THEME / 'MacOSTheme.xaml.cs'
    text = path.read_text().replace('AvaloniaXamlLoader.Load(sp, this);',
        '_accessibilityResources = new MacOSAccessibilityResources(this);\n            AvaloniaXamlLoader.Load(sp, this);')
    text = text.replace('Resources.MergedDictionaries.Add(Tokens);',
        'Resources.MergedDictionaries.Add(Tokens);\n            Resources.MergedDictionaries.Add(_accessibilityResources);')
    text = text.replace('if (change.Property == DensityStyleProperty)',
        'if (change.Property == IncreaseContrastProperty || change.Property == ReduceMotionProperty\n                || change.Property == ReduceTransparencyProperty)\n                _accessibilityResources.Invalidate();\n\n            if (change.Property == DensityStyleProperty)')
    text = text.replace('if (Tokens.TryGetResource(key, theme, out value))',
        'if (_accessibilityResources.TryGetResource(key, theme, out value))\n                return true;\n            if (Tokens.TryGetResource(key, theme, out value))')
    path.write_text(text)
    # Fix the inherited palette's missing invalidation for non-accent changes.
    path = THEME / 'ColorPaletteResources.cs'
    text = path.read_text().replace('_colors[key] = value;\n        }\n    }',
        '_colors[key] = value;\n        }\n        RaiseResourcesChanged();\n    }')
    path.write_text(text)
    commit('feat(macos): add live accessibility overrides and contrast-aware accent labels', 'src/Avalonia.Themes.MacOS')

if __name__ == '__main__':
    main()
