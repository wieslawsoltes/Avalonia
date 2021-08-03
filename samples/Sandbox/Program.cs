using Avalonia;

namespace Sandbox
{
    public class Program
    {
        static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new Win32PlatformOptions()
                {
                    UseDeferredRendering = true,
                    AllowEglInitialization = false,
                    UseWindowsUIComposition = false,
                    UseWgl = false
                })
                .LogToTrace();
    }
}
