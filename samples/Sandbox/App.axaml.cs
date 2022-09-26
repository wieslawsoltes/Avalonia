using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MiniMvvm;

namespace Sandbox
{
    public class MainViewModel : ViewModelBase
    {
        private WindowState _windowState;

        public MainViewModel()
        {
            WindowState = WindowState.Maximized;
        }
        
        public WindowState WindowState
        {
            get { return _windowState; }
            set { this.RaiseAndSetIfChanged(ref _windowState, value); }
        }
    }

    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                var result = new MainWindow() { DataContext = new MainViewModel() };
                
                desktopLifetime.MainWindow = result;
                
                result.Show();

                result.Width = 500;
                result.Height = 300;
            }
        }
    }
}
