#!/usr/bin/env python3
"""Expose real application-level appearance controls and companion regression tests."""
from pathlib import Path
from generate_10_tokens import commit

ROOT = Path(__file__).resolve().parents[2]


def main():
    path = ROOT / 'samples/ControlCatalog/Pages/MacOSThemePage.xaml.cs'
    if 'OnCustomizeAppearance' in path.read_text():
        return
    path.write_text('''using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.MacOS;

namespace ControlCatalog.Pages;

public partial class MacOSThemePage : ContentPage
{
    public MacOSThemePage() => InitializeComponent();

    private void OnCustomizeAppearance(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button || Application.Current is not App application)
            return;
        var theme = application.MacOSTheme;
        var window = TopLevel.GetTopLevel(this) as Window;
        var panel = new StackPanel { Width = 280, Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Appearance", FontWeight = FontWeight.SemiBold, FontSize = 16 });
        var appearance = new ComboBox
        {
            ItemsSource = new[] { "Follow system", "Light", "Dark" },
            SelectedIndex = window?.RequestedThemeVariant == ThemeVariant.Light ? 1
                : window?.RequestedThemeVariant == ThemeVariant.Dark ? 2 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        appearance.SelectionChanged += (_, _) =>
        {
            if (window is not null)
                window.RequestedThemeVariant = appearance.SelectedIndex switch
                {
                    1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default
                };
        };
        panel.Children.Add(appearance);
        var compact = new CheckBox { Content = "Compact controls", IsChecked = theme.DensityStyle == DensityStyle.Compact };
        compact.IsCheckedChanged += (_, _) => theme.DensityStyle = compact.IsChecked == true ? DensityStyle.Compact : DensityStyle.Normal;
        panel.Children.Add(compact);
        var contrast = new CheckBox { Content = "Increase contrast (mixed = system)", IsThreeState = true, IsChecked = theme.IncreaseContrast };
        contrast.IsCheckedChanged += (_, _) => theme.IncreaseContrast = contrast.IsChecked;
        panel.Children.Add(contrast);
        var motion = new CheckBox { Content = "Reduce motion", IsChecked = theme.ReduceMotion };
        motion.IsCheckedChanged += (_, _) => theme.ReduceMotion = motion.IsChecked == true;
        panel.Children.Add(motion);
        var transparency = new CheckBox { Content = "Reduce transparency", IsChecked = theme.ReduceTransparency };
        transparency.IsCheckedChanged += (_, _) => theme.ReduceTransparency = transparency.IsChecked == true;
        panel.Children.Add(transparency);
        panel.Children.Add(new Separator());
        var accents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var choice in new[] { ("Blue", "#007AFF"), ("Purple", "#8E44AD"), ("Gold", "#F5CE42") })
        {
            var color = Color.Parse(choice.Item2);
            var swatch = new Button { Content = choice.Item1 };
            swatch.Click += (_, _) => theme.SetToken(MacOSTokens.SystemAccentColor, color);
            accents.Children.Add(swatch);
        }
        panel.Children.Add(accents);
        var resetAccent = new Button { Content = "Use system accent", HorizontalAlignment = HorizontalAlignment.Stretch };
        resetAccent.Click += (_, _) => theme.ResetToken(MacOSTokens.SystemAccentColor);
        panel.Children.Add(resetAccent);
        button.Flyout = new Flyout { Content = panel };
        button.Flyout.ShowAt(button);
    }
}
''')
    path = ROOT / 'samples/ControlCatalog/Pages/MacOSThemePage.xaml'
    path.write_text(path.read_text().replace('Content="Customize appearance"', 'Content="Customize appearance" Click="OnCustomizeAppearance"'))
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/Avalonia.Themes.MacOS.UnitTests.csproj'
    path.write_text(path.read_text().replace('<ItemGroup>', '<ItemGroup>\n    <ProjectReference Include="../../src/Avalonia.Controls.ColorPicker/Avalonia.Controls.ColorPicker.csproj" />', 1))
    path = ROOT / 'tests/Avalonia.Themes.MacOS.UnitTests/ThemeTests.cs'
    text = path.read_text()
    text = text.replace('    private sealed class Host : IDisposable', '''    [AvaloniaFact]
    public void Optional_ColorPicker_Companion_Uses_The_Public_Token_Overrides()
    {
        Application.Current!.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.ColorPicker/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/MacOS/MacOS.xaml")
        });
        var control = new Avalonia.Controls.Primitives.ColorSlider();
        using var host = new Host(control);
        Theme.SetToken(MacOSTokens.ColorPicker_ColorSliderSize, 34d);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(34d, control.MinHeight);
    }

    [AvaloniaTheory]
    [InlineData("#007AFF")]
    [InlineData("#0A84FF")]
    [InlineData("#FFCC00")]
    [InlineData("#8E44AD")]
    public void Accent_Fill_And_Label_Maintain_At_Least_4_Point_5_Contrast(string value)
    {
        Theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse(value));
        var background = Resolve<Color>(MacOSSemanticTokens.Accent.Key);
        var foreground = Resolve<Color>(MacOSSemanticTokens.OnAccent.Key);
        static double Linear(byte component)
        {
            var c = component / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        var a = Luminance(background);
        var b = Luminance(foreground);
        var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        Assert.True(ratio >= 4.5, $"{value}: contrast was {ratio:F3}:1");
    }

    private sealed class Host : IDisposable''')
    path.write_text(text)
    path = ROOT / 'src/Avalonia.Themes.MacOS/MacOSAccessibilityResources.cs'
    path.write_text(path.read_text().replace('var accent = source;', 'var accent = Color.FromRgb(source.R, source.G, source.B);'))
    path = ROOT / 'nukebuild/Build.cs'
    text = path.read_text().replace('RunCoreTest("Avalonia.Skia.UnitTests");',
        'RunCoreTest("Avalonia.Skia.UnitTests");\n            RunCoreTest("Avalonia.Themes.MacOS.UnitTests");')
    path.write_text(text)
    commit('feat(catalog): add live macOS appearance controls and companion contrast tests',
           'samples/ControlCatalog', 'tests/Avalonia.Themes.MacOS.UnitTests', 'src/Avalonia.Themes.MacOS', 'nukebuild/Build.cs')

if __name__ == '__main__':
    main()
