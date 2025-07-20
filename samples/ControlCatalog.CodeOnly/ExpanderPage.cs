using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Controls.Primitives;

namespace ControlCatalog.CodeOnly
{
    public class ExpanderPage : UserControl
    {
        private class ExpanderPageViewModel : INotifyPropertyChanged
        {
            private bool _rounded;
            private CornerRadius? _cornerRadius;

            public event PropertyChangedEventHandler? PropertyChanged;

            public bool Rounded
            {
                get => _rounded;
                set
                {
                    if (_rounded != value)
                    {
                        _rounded = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rounded)));
                        CornerRadius = _rounded ? new CornerRadius(25) : (CornerRadius?)null;
                    }
                }
            }

            public CornerRadius? CornerRadius
            {
                get => _cornerRadius;
                private set
                {
                    if (_cornerRadius != value)
                    {
                        _cornerRadius = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CornerRadius)));
                    }
                }
            }
        }

        public ExpanderPage()
        {
            var vm = new ExpanderPageViewModel();
            DataContext = vm;

            var expanderUp = CreateExpander("Expand Up", ExpandDirection.Up);
            var expanderDown = CreateExpander("Expand Down", ExpandDirection.Down);
            var expanderLeft = CreateExpander("Expand Left", ExpandDirection.Left);
            var expanderRight = CreateExpander("Expand Right", ExpandDirection.Right);
            var headerControl = new Button { Content = "Control in Header" };
            var expanderHeaderControl = CreateExpander(headerControl, ExpandDirection.Down);
            var disabled = CreateExpander("Disabled", ExpandDirection.Down);
            disabled.IsEnabled = false;

            var collapsingDisabled = CreateExpander("Collapsing Disabled", ExpandDirection.Down);
            collapsingDisabled.IsExpanded = true;
            collapsingDisabled.Collapsing += (_, e) => e.Cancel = true;

            var expandingDisabled = CreateExpander("Expanding Disabled", ExpandDirection.Down);
            expandingDisabled.Expanding += (_, e) => e.Cancel = true;

            var rounded = new CheckBox { Content = "Rounded" };
            rounded.Bind(ToggleButton.IsCheckedProperty, new Binding("Rounded") { Mode = BindingMode.TwoWay });

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "Expands to show nested content" },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(0,16,0,0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 16,
                        Children =
                        {
                            expanderUp,
                            expanderDown,
                            expanderLeft,
                            expanderRight,
                            expanderHeaderControl,
                            disabled,
                            rounded,
                            collapsingDisabled,
                            expandingDisabled
                        }
                    }
                }
            };

            Content = panel;
        }

        private Expander CreateExpander(object header, ExpandDirection dir)
        {
            var expander = new Expander { Header = header, ExpandDirection = dir };
            expander.Content = new StackPanel { Children = { new TextBlock { Text = "Expanded content" } } };
            expander.Bind(Expander.CornerRadiusProperty, new Binding("CornerRadius"));
            return expander;
        }
    }
}
