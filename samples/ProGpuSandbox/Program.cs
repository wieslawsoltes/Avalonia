using Avalonia;

namespace ProGpuSandbox
{
    public class Program
    {
        static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseRenderingSubsystem(() => Avalonia.ProGpu.SkiaPlatform.Initialize(), "Skia")
                .WithDeveloperTools()
                .LogToTrace();
    }
}
