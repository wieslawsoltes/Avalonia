using Avalonia.Controls;
using Avalonia.Layout;

using Avalonia;
namespace ControlCatalog.CodeOnly
{
    public class RadioButtonPage : UserControl
    {
        public RadioButtonPage()
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "Allows the selection of a single option of many" },
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
                                    new RadioButton { Content = "_Option 1", IsChecked = true },
                                    new RadioButton { Content = "O_ption 2" },
                                    new RadioButton { Content = "Op_tion 3", IsChecked = null },
                                    new RadioButton { Content = "Disabled", IsEnabled = false }
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Vertical,
                                Spacing = 16,
                                Children =
                                {
                                    new RadioButton { Content = "Three States: Option 1", IsThreeState = true, IsChecked = true },
                                    new RadioButton { Content = "Three States: Option 2", IsThreeState = true, IsChecked = false },
                                    new RadioButton { Content = "Three States: Option 3", IsThreeState = true, IsChecked = null },
                                    new RadioButton { Content = "Disabled", IsThreeState = true, IsChecked = null, IsEnabled = false }
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Vertical,
                                Spacing = 16,
                                Children =
                                {
                                    new RadioButton { GroupName = "A", Content = "Group A: Option 1", IsChecked = true },
                                    new RadioButton { GroupName = "A", Content = "Group A: Disabled", IsEnabled = false },
                                    new RadioButton { GroupName = "B", Content = "Group B: Option 1" },
                                    new RadioButton { GroupName = "B", Content = "Group B: Option 3", IsChecked = null }
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Vertical,
                                Spacing = 16,
                                Children =
                                {
                                    new RadioButton { GroupName = "A", Content = "Group A: Option 2", IsChecked = true },
                                    new RadioButton { GroupName = "B", Content = "Group B: Option 2" },
                                    new RadioButton { GroupName = "B", Content = "Group B: Option 4", IsChecked = null }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
