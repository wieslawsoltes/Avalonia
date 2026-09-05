#!/usr/bin/env python3
"""Correct the final isolated-package check and a dark-mode disclosure surface."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'eng/macos26/package-smoke.py'
    text = path.read_text().replace("projects = {p.stem: p for p in (ROOT / 'src').rglob('*.csproj')}",
        "projects = {p.stem: p for directory in ('src', 'packages')\n                for p in (ROOT / directory).rglob('*.csproj')}")
    path.write_text(text)
    path = ROOT / 'src/Avalonia.Themes.MacOS/MacOSAccessibilityResources.cs'
    text = path.read_text()
    if 'ExpanderHeaderBackground' not in text:
        anchor = '        if (text == "MacOS.Color.Accent")'
        text = text.replace(anchor, '''        // These inherited component resources are Color, not IBrush. Resolve
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

''' + anchor)
    path.write_text(text)
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text()
    if 'Expander_Color_Tokens_Keep_Their_Type_And_Follow_Live_Semantics' not in text:
        text = text.replace('    private sealed class Host : IDisposable', '''    [AvaloniaFact]
    public void Expander_Color_Tokens_Keep_Their_Type_And_Follow_Live_Semantics()
    {
        var expander = new Expander { Header = "Options" };
        using var host = new Host(expander);
        var toggle = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(expander)
            .OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
        var color = Color.Parse("#DDEEFF");
        Theme.SetToken(MacOSSemanticTokens.Surface, color);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(color, Resolve<Color>(MacOSTokens.ExpanderHeaderBackground.Key));
        Assert.Equal(color, Assert.IsAssignableFrom<ISolidColorBrush>(toggle.Background).Color);
        Theme.SetToken(MacOSTokens.ExpanderHeaderBackground, Colors.Bisque);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Bisque, Assert.IsAssignableFrom<ISolidColorBrush>(toggle.Background).Color);
    }

    private sealed class Host : IDisposable''')
    path.write_text(text)
    commit('fix(macos): complete isolated package dependencies and live disclosure color aliases',
           'eng/macos26/package-smoke.py', 'src/Avalonia.Themes.MacOS', 'tests/Avalonia.Themes.MacOS.UnitTests')

if __name__ == '__main__':
    main()
