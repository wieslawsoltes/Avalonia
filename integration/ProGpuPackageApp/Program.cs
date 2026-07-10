using Avalonia;

namespace ProGpuPackageApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSilkNet()
            .UseProGpu()
            .UseHarfBuzz()
            .WithInterFont();
}
