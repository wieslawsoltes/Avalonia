using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace ControlCatalog.CodeOnly
{
    public class SliderPage : UserControl
    {
        public SliderPage()
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "A control that lets the user select from a range of values by moving a Thumb control along a Track." },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0,16,0,0),
                        Spacing = 16,
                        Children =
                        {
                            CreateVerticalStack(),
                            CreateVerticalSlider(),
                            CreateVerticalSlider(isReversed: true)
                        }
                    }
                }
            };
        }

        private Control CreateVerticalStack()
        {
            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 300 },
                    new Slider { Minimum = 0, Maximum = 100, TickPlacement = TickPlacement.BottomRight, IsSnapToTickEnabled = true, Ticks = new AvaloniaList<double> {0,20,25,40,75,100}, Width = 300 },
                    new Slider { Minimum = 0, Maximum = 100, TickPlacement = TickPlacement.BottomRight, IsSnapToTickEnabled = true, IsDirectionReversed = true, Ticks = new AvaloniaList<double> {0,20,25,40,75,100}, Width = 300 },
                    CreateTooltipSlider(),
                    CreateValidationSlider(),
                    new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsDirectionReversed = true, Width = 300 }
                }
            };
        }

        private Control CreateVerticalSlider(bool isReversed = false)
        {
            return new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Orientation = Orientation.Vertical,
                IsSnapToTickEnabled = true,
                TickPlacement = TickPlacement.Outside,
                TickFrequency = 10,
                IsDirectionReversed = isReversed,
                Height = 300
            };
        }

        private Slider CreateValidationSlider()
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 300 };
            DataValidationErrors.SetError(slider, new System.Exception());
            return slider;
        }

        private Slider CreateTooltipSlider()
        {
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 300
            };

            slider.Styles.Add(new Style(x => x.OfType<Thumb>())
            {
                Setters =
                {
                    new Setter(ToolTip.TipProperty, new Binding { Path = "$parent[Slider].Value", StringFormat = "Value {0:f}" }),
                    new Setter(ToolTip.PlacementProperty, PlacementMode.Top),
                    new Setter(ToolTip.VerticalOffsetProperty, -10.0),
                    new Setter(ToolTip.HorizontalOffsetProperty, -30.0)
                }
            });

            return slider;
        }
    }
}
