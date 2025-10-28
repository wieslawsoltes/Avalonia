using Avalonia.Controls;
using Avalonia.Markup.Xaml;
#if !NETSTANDARD2_0
using Avalonia.Markup.Xaml.HotReload;
#endif

namespace ControlCatalog.Pages
{
    public class TextBoxPage : UserControl
    {
        public TextBoxPage()
        {
            this.InitializeComponent();
#if !NETSTANDARD2_0
            RuntimeHotReloadService.Track(this);
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
