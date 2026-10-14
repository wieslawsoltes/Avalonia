using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Inking.Themes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace InkingStudio;
public sealed class App : Application
{
    public static StudioView? Studio { get; private set; }
    public static Func<StudioView, IStudioStorage>? StorageFactory { get; set; }
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme()); Styles.Add(new InkTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        Studio = new StudioView();
        Studio.Storage = StorageFactory?.Invoke(Studio) ?? new DesktopStudioStorage(Studio);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window { Title = "Ink Studio · Avalonia Inking", Width = 1400, Height = 920, MinWidth = 680, MinHeight = 500, Content = Studio };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = Studio;
        base.OnFrameworkInitializationCompleted();
    }
}
