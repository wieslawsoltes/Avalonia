using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ProGpuPackageApp;

internal sealed class App : Application
{
    private static readonly Color Surface = Color.Parse("#22252B");
    private static readonly Color Border = Color.Parse("#343840");
    private static readonly Color PrimaryText = Color.Parse("#F7F7F2");
    private static readonly Color SecondaryText = Color.Parse("#AEB4BE");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = CreateWindow();
            desktop.MainWindow = window;

            if (Environment.GetEnvironmentVariable("PROGPU_INTEGRATION_SMOKE") == "1")
            {
                window.WindowState = WindowState.Maximized;
                DispatcherTimer.RunOnce(() => desktop.Shutdown(), TimeSpan.FromSeconds(2));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateWindow()
    {
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "ProGPU + Avalonia",
                    Foreground = new SolidColorBrush(PrimaryText),
                    FontSize = 30,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Package-only integration smoke app",
                    Foreground = new SolidColorBrush(SecondaryText),
                    FontSize = 15,
                    Margin = new Thickness(0, 0, 0, 18)
                },
                CreateStatusRow("Renderer", "ProGPU / WebGPU", Color.Parse("#38D4C8")),
                CreateStatusRow("Windowing", "Silk.NET", Color.Parse("#FF6B5E")),
                CreateStatusRow("Integration", "12.0.5-preview.19", Color.Parse("#F4C95D")),
                new TextBlock
                {
                    Text = "Direct ProGPU + WGSL through IProGpuApiLeaseFeature",
                    Foreground = new SolidColorBrush(SecondaryText),
                    FontSize = 13,
                    Margin = new Thickness(0, 18, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                },
                new ProGpuLeaseView { Height = 92 }
            }
        };

        return new Window
        {
            Title = "ProGPU Package Integration",
            Width = 680,
            Height = 540,
            MinWidth = 520,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#17191D")),
            Content = new Border
            {
                Padding = new Thickness(48),
                Child = content
            }
        };
    }

    private static Border CreateStatusRow(string label, string value, Color accent)
    {
        return new Border
        {
            Background = new SolidColorBrush(Surface),
            BorderBrush = new SolidColorBrush(Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 13),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Width = 9,
                        Height = 9,
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(accent),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = label,
                        Width = 150,
                        Foreground = new SolidColorBrush(SecondaryText),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = value,
                        Foreground = new SolidColorBrush(PrimaryText),
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
    }
}
