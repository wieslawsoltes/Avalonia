using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ControlCatalog.CodeOnly
{
    public class CheckBoxPage : UserControl
    {
        public CheckBoxPage()
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "A check box control" },
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
                                Orientation = Orientation.Vertical,
                                Spacing = 16,
                                Children =
                                {
                                    new CheckBox { Content = "_Unchecked" },
                                    new CheckBox { Content = "_Checked", IsChecked = true },
                                    new CheckBox { Content = "_Indeterminate", IsChecked = null },
                                    new CheckBox { Content = "Disabled", IsChecked = true, IsEnabled = false },
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Vertical,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Spacing = 16,
                                Children =
                                {
                                    new CheckBox { Content = "Three State: Unchecked", IsThreeState = true, IsChecked = false },
                                    new CheckBox { Content = "Three State: Checked", IsThreeState = true, IsChecked = true },
                                    new CheckBox { Content = "Three State: Indeterminate", IsThreeState = true, IsChecked = null },
                                    new CheckBox { Content = "Three State: Disabled", IsThreeState = true, IsChecked = null, IsEnabled = false },
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
