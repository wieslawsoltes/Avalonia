using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Avalonia.Media.Imaging;
namespace ControlCatalog.CodeOnly

{
    public class BorderPage : UserControl
    {
        public BorderPage()
        {
            var accent = (Color)Application.Current!.Resources["SystemAccentColor"]!;
            var accentDark1 = (Color)Application.Current!.Resources["SystemAccentColorDark1"]!;
            var semiTransparentAccent = new SolidColorBrush(accent) { Opacity = 0.4 };

            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Classes = { "h2" },
                        Text = "A control which decorates a child with a border and background"
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(0,16,0,0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 16,
                        Children =
                        {
                            new Border
                            {
                                BorderBrush = new SolidColorBrush(accent),
                                BorderThickness = new Thickness(2),
                                Padding = new Thickness(16),
                                Child = new TextBlock { Text = "Border" }
                            },
                            new Border
                            {
                                Background = new SolidColorBrush(accentDark1),
                                BorderBrush = semiTransparentAccent,
                                BackgroundSizing = BackgroundSizing.CenterBorder,
                                BorderThickness = new Thickness(8),
                                Padding = new Thickness(12),
                                Child = new TextBlock { Text = "Background And CenterBorder" }
                            },
                            new Border
                            {
                                Background = new SolidColorBrush(accentDark1),
                                BorderBrush = semiTransparentAccent,
                                BackgroundSizing = BackgroundSizing.InnerBorderEdge,
                                BorderThickness = new Thickness(8),
                                Padding = new Thickness(12),
                                Child = new TextBlock { Text = "Background And InnerBorder" }
                            },
                            new Border
                            {
                                Background = new SolidColorBrush(accentDark1),
                                BorderBrush = semiTransparentAccent,
                                BackgroundSizing = BackgroundSizing.OuterBorderEdge,
                                BorderThickness = new Thickness(8),
                                Padding = new Thickness(12),
                                Child = new TextBlock { Text = "Background And OuterBorderEdge" }
                            },
                            new Border
                            {
                                BorderBrush = new SolidColorBrush(accent),
                                BorderThickness = new Thickness(4),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(16),
                                Child = new TextBlock { Text = "Rounded Corners" }
                            },
                            new Border
                            {
                                Background = new SolidColorBrush(accent),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(16),
                                Child = new TextBlock { Text = "Rounded Corners" }
                            },
                            new Border
                            {
                                Width = 100,
                                Height = 100,
                                BorderThickness = new Thickness(0),
                                Background = Brushes.White,
                                CornerRadius = new CornerRadius(100),
                                ClipToBounds = true,
                                Child = new Image { Source = new Bitmap("/Assets/maple-leaf-888807_640.jpg"), Stretch = Stretch.UniformToFill }
                            },
                            new TextBlock { Text = "Border with Clipping", HorizontalAlignment = HorizontalAlignment.Center }
                        }
                    }
                }
            };
        }
    }
}
