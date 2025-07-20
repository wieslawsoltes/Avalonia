using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace ControlCatalog.CodeOnly
{
    public class ViewboxPage : UserControl
    {
        public ViewboxPage()
        {
            var width = new Slider { Minimum = 10, Maximum = 200, Value = 100, TickFrequency = 25, TickPlacement = TickPlacement.TopLeft };
            var height = new Slider { Minimum = 10, Maximum = 200, Value = 100, TickFrequency = 25, TickPlacement = TickPlacement.TopLeft };

            var stretchSelector = new ComboBox
            {
                Items = { Stretch.Uniform, Stretch.UniformToFill, Stretch.Fill, Stretch.None },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0,0,0,2)
            };

            var stretchDirSelector = new ComboBox
            {
                Items = { StretchDirection.Both, StretchDirection.DownOnly, StretchDirection.UpOnly },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var viewbox = new Viewbox();
            viewbox.Bind(Viewbox.StretchProperty, new Avalonia.Data.Binding { Source = stretchSelector, Path = nameof(ComboBox.SelectedItem) });
            viewbox.Bind(Viewbox.StretchDirectionProperty, new Avalonia.Data.Binding { Source = stretchDirSelector, Path = nameof(ComboBox.SelectedItem) });
            viewbox.Child = new Ellipse { Width = 50, Height = 50, Fill = Brushes.CornflowerBlue };

            var innerBorder = new Border
            {
                BorderBrush = Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1)
            };
            innerBorder.Bind(WidthProperty, new Avalonia.Data.Binding { Source = width, Path = nameof(Slider.Value) });
            innerBorder.Bind(HeightProperty, new Avalonia.Data.Binding { Source = height, Path = nameof(Slider.Value) });
            innerBorder.Child = viewbox;

            var outerBorder = new Border
            {
                BorderBrush = Brushes.Orange,
                BorderThickness = new Thickness(1),
                Width = 200,
                Height = 200,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = innerBorder
            };

            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,*"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        Children = { new TextBlock { Classes = { "h2" }, Text = "A control used to scale single child." } }
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        [Grid.RowProperty] = 1,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0,10,0,0),
                        Children =
                        {
                            outerBorder,
                            new StackPanel
                            {
                                Orientation = Orientation.Vertical,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                Margin = new Thickness(8,0,0,0),
                                Width = 150,
                                [Grid.ColumnProperty] = 1,
                                Children =
                                {
                                    new TextBlock { Text = "Width" },
                                    width,
                                    new TextBlock { Text = "Height" },
                                    height,
                                    new TextBlock { Text = "Stretch" },
                                    stretchSelector,
                                    new TextBlock { Text = "Stretch Direction" },
                                    stretchDirSelector
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
