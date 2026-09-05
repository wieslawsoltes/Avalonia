#!/usr/bin/env python3
"""Correct the failing token contract and preserve legible macOS accent labels."""
from pathlib import Path
from generate_10_tokens import commit
from generate_45_contracts import main as refresh_tokens

ROOT = Path(__file__).resolve().parents[2]
THEME = ROOT / 'src/Avalonia.Themes.MacOS'


def main():
    path = THEME / 'Tokens/Semantic.xaml'
    if 'MacOS.Color.Accent' in path.read_text():
        return
    text = path.read_text().rsplit('</ResourceDictionary>', 1)[0] + '''  <!-- A legacy compact-only key is public, so it also needs a normal fallback. -->
  <x:Double x:Key="MacOS.NavigationViewItemOnLeftMinHeight">36</x:Double>
  <Color x:Key="MacOS.Color.Accent">#006ADC</Color>
  <SolidColorBrush x:Key="MacOS.Brush.Accent" Color="{DynamicResource MacOS.Color.Accent}" />
</ResourceDictionary>
'''
    path.write_text(text)
    path = THEME / 'MacOSAccessibilityResources.cs'
    text = path.read_text()
    old = '        if (text == "MacOS.Color.OnAccent")'
    insertion = '''        if (text == "MacOS.Color.Accent")
        {
            // Explicit semantic overrides remain authoritative. Otherwise the raw
            // OS accent is preserved as SystemAccentColor, while filled surfaces
            // use a contrast-safe derivative for their small control labels.
            if (theme.Tokens.TryGetResource(text, variant, out _))
                return false;
            if (((IResourceNode)theme).TryGetResource("MacOS.SystemAccentColor", variant, out var raw)
                && raw is Color source)
            {
                var accent = source;
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

'''
    text = text.replace(old, insertion + old)
    before, after = text.split(old, 1)
    after = after.replace('TryGetResource("MacOS.SystemAccentColor", variant, out var resource)',
                          'TryGetResource("MacOS.Color.Accent", variant, out var resource)')
    text = before + old + after
    text = text.replace('    private static double Linear(byte component)',
        '    private static double Luminance(Color color) =>\n        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);\n\n    private static double Linear(byte component)')
    path.write_text(text)
    # Component fills use the semantic accent. Focus rings retain the raw OS accent.
    path = THEME / 'Accents/MacOSControlResources.xaml'
    text = path.read_text().replace('Color="{DynamicResource MacOS.SystemAccentColor}"',
                                   'Color="{DynamicResource MacOS.Color.Accent}"')
    path.write_text(text)
    path = THEME / 'MacOSSemanticTokens.cs'
    text = path.read_text().replace('public static class MacOSSemanticTokens\n{', '''public static class MacOSSemanticTokens
{
    /// <summary>Gets the contrast-safe accent color used for filled controls.</summary>
    public static readonly MacOSToken<Color> Accent = new("MacOS.Color.Accent");
    /// <summary>Gets the contrast-safe accent brush.</summary>
    public static readonly MacOSToken<IBrush> AccentBrush = new("MacOS.Brush.Accent");''')
    path.write_text(text)
    commit('fix(macos): make all public tokens resolve and provide legible accent surfaces', 'src/Avalonia.Themes.MacOS')
    refresh_tokens()

if __name__ == '__main__':
    main()
