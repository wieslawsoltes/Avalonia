using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia;

namespace ControlCatalog.CodeOnly
{
    public class ToggleSwitchPage : UserControl
    {
        public ToggleSwitchPage()
        {
            Styles.Add(new Style(x => x.OfType<TextBox>().Class("CodeBox"))
            {
                Setters =
                {
                    new Setter(TextBox.PaddingProperty, new Thickness(10)),
                    new Setter(TextBox.IsReadOnlyProperty, true),
                    new Setter(TextBox.BorderBrushProperty, Brushes.Transparent),
                    new Setter(TextBox.FontSizeProperty, 14.0),
                    new Setter(TextBox.IsEnabledProperty, true)
                }
            });

            Styles.Add(new Style(x => x.OfType<TextBlock>().Class("header"))
            {
                Setters =
                {
                    new Setter(TextBlock.FontSizeProperty, 18.0),
                    new Setter(TextBlock.MarginProperty, new Thickness(0,20,0,20))
                }
            });

            Styles.Add(new Style(x => x.OfType<Border>().Class("Thin"))
            {
                Setters =
                {
                    new Setter(Border.BorderBrushProperty, Brushes.Gray),
                    new Setter(Border.BorderThicknessProperty, new Thickness(0.5)),
                    new Setter(Border.CornerRadiusProperty, new CornerRadius(2))
                }
            });

            Content = new StackPanel
            {
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    new TextBlock { Text = "Simple ToggleSwitch", Classes = { "header" } },
                    new Border
                    {
                        Classes = { "Thin" },
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new ToggleSwitch { Margin = new Thickness(10) },
                                new TextBox { Text = "<ToggleSwitch/>", Classes = { "CodeBox" } }
                            }
                        }
                    },
                    new TextBlock { Text = "Headered ToggleSwitch", Classes = { "header" } },
                    new Border
                    {
                        Classes = { "Thin" },
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new ToggleSwitch { Content = "h_eadered", IsChecked = true, Margin = new Thickness(10) },
                                new TextBox { Text = "<ToggleSwitch>headered</ToggleSwitch>", Classes = { "CodeBox" } }
                            }
                        }
                    },
                    new TextBlock { Text = "Custom content ToggleSwitch", Classes = { "header" } },
                    new Border
                    {
                        Classes = { "Thin" },
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new ToggleSwitch
                                {
                                    Content = "_Custom",
                                    OnContent = "On",
                                    OffContent = "Off",
                                    Margin = new Thickness(10)
                                },
                                new TextBox
                                {
                                    Text = "<ToggleSwitch Content=\"Custom\" ContentOn=\"On\" ContentOff=\"Off\" />",
                                    Classes = { "CodeBox" }
                                }
                            }
                        }
                    },
                    new TextBlock { Text = "Image content ToggleSwitch", Classes = { "header" } },
                    new Border
                    {
                        Classes = { "Thin" },
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new ToggleSwitch
                                {
                                    Content = "_Just Click!",
                                    Margin = new Thickness(10),
                                    OnContent = new Image { Source = new Bitmap("/Assets/hirsch-899118_640.jpg"), Height = 32 },
                                    OffContent = new Image { Source = new Bitmap("/Assets/delicate-arch-896885_640.jpg"), Height = 32 }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
