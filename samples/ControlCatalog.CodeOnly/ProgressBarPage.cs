using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace ControlCatalog.CodeOnly
{
    public class ProgressBarPage : UserControl
    {
        public ProgressBarPage()
        {
            var maximum = new NumericUpDown { Value = 100, VerticalAlignment = VerticalAlignment.Center };
            var minimum = new NumericUpDown { Value = 0, VerticalAlignment = VerticalAlignment.Center };
            var stringFormat = new TextBox { Text = "{0:0}%", VerticalAlignment = VerticalAlignment.Center };
            var showProgress = new CheckBox { Margin = new Thickness(10,16,0,0), Content = "Show Progress Text" };
            var isIndeterminate = new CheckBox { Margin = new Thickness(10,16,0,0), Content = "Toggle Indeterminate" };
            var hprogress = new Slider { Minimum = 0, Maximum = 100, Value = 40 };
            var vprogress = new Slider { Minimum = 0, Maximum = 100, Value = 60 };

            var horizontal = new ProgressBar();
            var vertical = new ProgressBar { Orientation = Orientation.Vertical };

            BindProgress(horizontal, hprogress, minimum, maximum, stringFormat, showProgress, isIndeterminate);
            BindProgress(vertical, vprogress, minimum, maximum, stringFormat, showProgress, isIndeterminate);

            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "A progress bar control" },
                    new StackPanel
                    {
                        Spacing = 5,
                        Children =
                        {
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                Children =
                                {
                                    new TextBlock { Text = "Maximum", VerticalAlignment = VerticalAlignment.Center },
                                    maximum
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                Children =
                                {
                                    new TextBlock { Text = "Minimum", VerticalAlignment = VerticalAlignment.Center },
                                    minimum
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                Children =
                                {
                                    new TextBlock { Text = "Progress Text Format", VerticalAlignment = VerticalAlignment.Center },
                                    stringFormat
                                }
                            },
                            showProgress,
                            isIndeterminate,
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Margin = new Thickness(0,16,0,0),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Spacing = 16,
                                Children =
                                {
                                    new StackPanel
                                    {
                                        Spacing = 16,
                                        Children = { horizontal }
                                    },
                                    vertical
                                }
                            },
                            new StackPanel
                            {
                                Margin = new Thickness(16),
                                Children =
                                {
                                    hprogress,
                                    vprogress
                                }
                            },
                            new StackPanel
                            {
                                Spacing = 10,
                                Children =
                                {
                                    new ProgressBar { Value = 5, Maximum = 10, VerticalAlignment = VerticalAlignment.Center },
                                    new ProgressBar { Value = 50, VerticalAlignment = VerticalAlignment.Center },
                                    new ProgressBar { Value = 50, Minimum = 25, Maximum = 75, VerticalAlignment = VerticalAlignment.Center }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static void BindProgress(ProgressBar bar, RangeBase slider, NumericUpDown min, NumericUpDown max, TextBox format, CheckBox show, CheckBox indeterminate)
        {
            bar.Bind(RangeBase.ValueProperty, new Binding { Source = slider, Path = nameof(Slider.Value) });
            bar.Bind(RangeBase.MinimumProperty, new Binding { Source = min, Path = nameof(NumericUpDown.Value) });
            bar.Bind(RangeBase.MaximumProperty, new Binding { Source = max, Path = nameof(NumericUpDown.Value) });
            bar.Bind(ProgressBar.ProgressTextFormatProperty, new Binding { Source = format, Path = nameof(TextBox.Text) });
            bar.Bind(ProgressBar.ShowProgressTextProperty, new Binding { Source = show, Path = nameof(ToggleButton.IsChecked) });
            bar.Bind(ProgressBar.IsIndeterminateProperty, new Binding { Source = indeterminate, Path = nameof(ToggleButton.IsChecked) });
        }
    }
}
