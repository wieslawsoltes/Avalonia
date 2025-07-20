using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;

namespace ControlCatalog.CodeOnly
{
    public class ButtonsPage : UserControl
    {
        private int _repeatClicks;

        public ButtonsPage()
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 450,
                Children =
                {
                    CreateHeader("Button", "A standard button control"),
                    CreateButtonSection(),
                    CreateHeader("ToggleButton", "A button control with multiple states: checked, unchecked or indeterminate."),
                    new Border
                    {
                        Classes = { "thin" },
                        Padding = new Thickness(15),
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Children = { new ToggleButton { Content = "Toggle Button" } }
                        }
                    },
                    CreateHeader("RepeatButton", "A button control that raises its Click event repeatedly when it is pressed and held."),
                    CreateRepeatSection(),
                    CreateHeader("HyperlinkButton", "A button control that functions as a navigable hyperlink."),
                    CreateHyperlinkSection()
                }
            };
        }

        private Border CreateHeader(string text, string description)
        {
            return new Border
            {
                Classes = { "header-border" },
                Child = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = text, Classes = { "header" } },
                        new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
        }

        private Border CreateButtonSection()
        {
            var column1 = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Width = 200,
                Children =
                {
                    new Button { Content = "Standard _XAML Button" },
                    new Button { Content = "Foreground", Foreground = Brushes.White },
                    new Button { Content = "Background", Background = new SolidColorBrush((Color)Application.Current!.Resources["SystemAccentColor"]!) },
                    new Button { Content = "Disabled", IsEnabled = false },
                    new Button { Content = "Accent", Classes = { "accent" } }
                }
            };

            var column2 = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Width = 200,
                Children =
                {
                    new Button { Content = "No Border", BorderThickness = new Thickness(0) },
                    new Button { Content = "Border Color", BorderBrush = new SolidColorBrush((Color)Application.Current!.Resources["SystemAccentColor"]!) },
                    new Button { Content = "Thick Border", BorderBrush = new SolidColorBrush((Color)Application.Current!.Resources["SystemAccentColor"]!), BorderThickness = new Thickness(4) },
                    new Button { Content = "Disabled", BorderBrush = new SolidColorBrush((Color)Application.Current!.Resources["SystemAccentColor"]!), BorderThickness = new Thickness(4), IsEnabled = false },
                    new Button { Content = "IsTabStop=False", BorderBrush = new SolidColorBrush((Color)Application.Current!.Resources["SystemAccentColor"]!), IsTabStop = false }
                }
            };

            return new Border
            {
                Classes = { "thin" },
                Padding = new Thickness(15),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10,
                    Children = { column1, column2 }
                }
            };
        }

        private Border CreateRepeatSection()
        {
            var text = new TextBlock { Name = "RepeatButtonTextBlock", Text = "Repeat Button: 0" };
            var button = new RepeatButton { Name = "RepeatButton", Content = text };
            button.Click += (_, _) => text.Text = $"Repeat Button: {++_repeatClicks}";

            return new Border
            {
                Classes = { "thin" },
                Padding = new Thickness(15),
                Child = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 8,
                    Children = { button }
                }
            };
        }

        private Border CreateHyperlinkSection()
        {
            var hyperlink = new HyperlinkButton
            {
                Name = "EnabledHyperlinkButton",
                NavigateUri = new System.Uri("https://docs.avaloniaui.net/docs/welcome"),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = "Avalonia Docs" }
            };

            var checkBox = new CheckBox
            {
                Content = "IsVisited",
                Margin = new Thickness(10,0,0,0),
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.Bind(ToggleButton.IsCheckedProperty, new Binding
            {
                Path = "IsVisited",
                Source = hyperlink
            });

            return new Border
            {
                Classes = { "thin" },
                Padding = new Thickness(15),
                Child = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 8,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children = { hyperlink, checkBox }
                        },
                        new HyperlinkButton
                        {
                            IsEnabled = false,
                            NavigateUri = new System.Uri("https://docs.avaloniaui.net/docs/welcome"),
                            Content = new TextBlock { Text = "Avalonia Docs" }
                        }
                    }
                }
            };
        }
    }
}
