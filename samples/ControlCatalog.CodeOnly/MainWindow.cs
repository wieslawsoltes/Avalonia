using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;

namespace ControlCatalog.CodeOnly
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            Title = "ControlCatalog CodeOnly";
            Width = 800;
            Height = 600;

            Content = CreateContent();
        }

        private Control CreateContent()
        {
            var tabs = new TabControl
            {
                Items =
                {
                    new TabItem { Header = "CheckBox", Content = new CheckBoxPage() },
                    new TabItem { Header = "Buttons", Content = new ButtonsPage() },
                    new TabItem { Header = "Slider", Content = new SliderPage() },
                    new TabItem { Header = "Border", Content = new BorderPage() },
                    new TabItem { Header = "ProgressBar", Content = new ProgressBarPage() },
                    new TabItem { Header = "RadioButton", Content = new RadioButtonPage() },
                    new TabItem { Header = "ToggleSwitch", Content = new ToggleSwitchPage() },
                    new TabItem { Header = "Canvas", Content = new CanvasPage() },
                    new TabItem { Header = "Expander", Content = new ExpanderPage() },
                    new TabItem { Header = "Viewbox", Content = new ViewboxPage() }
                }
            };

            return tabs;
        }
    }
}
