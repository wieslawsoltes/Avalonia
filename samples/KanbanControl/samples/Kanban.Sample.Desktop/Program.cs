using Avalonia;
using Kanban.Sample;
namespace Kanban.Sample.Desktop;
internal static class Program
{
    [STAThread] private static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
