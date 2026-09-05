#!/usr/bin/env python3
"""Finalize isolated packaging prerequisites and custom-accent pressed states."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'eng/macos26/package-smoke.py'
    text = path.read_text()
    if "run('dotnet', 'restore', str(ROOT / 'src/tools/Avalonia.Designer.HostApp" not in text:
        text = text.replace("    pending, built =", "    # The aggregate package's custom pack target builds the designer host\n    # outside the normal ProjectReference restore graph.\n    run('dotnet', 'restore', str(ROOT / 'src/tools/Avalonia.Designer.HostApp/Avalonia.Designer.HostApp.csproj'))\n    pending, built =")
    path.write_text(text)
    path = ROOT / 'src/Avalonia.Themes.MacOS/Accents/MacOSControlResources.xaml'
    text = path.read_text()
    for key in ('AccentButtonBackgroundPressed', 'ToggleButtonBackgroundCheckedPressed', 'ToggleButtonBackgroundIndeterminatePressed'):
        old = f'<SolidColorBrush x:Key="MacOS.{key}" Color="{{DynamicResource MacOS.SystemAccentColorDark1}}" />'
        new = f'<SolidColorBrush x:Key="MacOS.{key}" Color="{{DynamicResource MacOS.Color.Accent}}" />'
        text = text.replace(old, new)
    path.write_text(text)
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text()
    if 'Custom_Accent_Does_Not_Revert_To_The_OS_Shade_When_Pressed' not in text:
        text = text.replace('    private sealed class Host : IDisposable', '''    [AvaloniaTheory]
    [InlineData("#FFCC00")]
    [InlineData("#8E44AD")]
    public void Custom_Accent_Does_Not_Revert_To_The_OS_Shade_When_Pressed(string value)
    {
        Theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse(value));
        Dispatcher.UIThread.RunJobs();
        var accent = Resolve<Color>(MacOSSemanticTokens.Accent.Key);
        Assert.Equal(accent, Resolve<ISolidColorBrush>(MacOSTokens.AccentButtonBackgroundPressed.Key).Color);
        Assert.Equal(accent, Resolve<ISolidColorBrush>(MacOSTokens.ToggleButtonBackgroundCheckedPressed.Key).Color);
        Assert.Equal(accent, Resolve<ISolidColorBrush>(MacOSTokens.ToggleButtonBackgroundIndeterminatePressed.Key).Color);
    }

    private sealed class Host : IDisposable''')
    path.write_text(text)
    commit('fix(macos): preserve custom accents when pressed and restore aggregate pack prerequisites',
           'eng/macos26/package-smoke.py', 'src/Avalonia.Themes.MacOS', 'tests/Avalonia.Themes.MacOS.UnitTests')

if __name__ == '__main__':
    main()
