using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace Kanban.Sample;
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        var view = new MainView();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window { Title = "Kanban · Atlas workspace", Width = 1500, Height = 930, MinWidth = 620, MinHeight = 500, Content = view };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = view;
        base.OnFrameworkInitializationCompleted();
    }
}
