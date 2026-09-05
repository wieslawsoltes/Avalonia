using Avalonia;
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
