using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.LinuxFramebuffer;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.ProGpu;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.Vulkan;
using ControlCatalog.Pages;

namespace ControlCatalog.Desktop
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var pageArgumentIndex = Array.IndexOf(args, "--page");
            if (pageArgumentIndex >= 0 && pageArgumentIndex + 1 < args.Length)
            {
                App.InitialPage = args[pageArgumentIndex + 1];
            }

            if (args.Contains("--wait-for-attach"))
            {
                Console.WriteLine("Attach debugger and use 'Set next statement'");
                while (true)
                {
                    Thread.Sleep(100);
                    if (Debugger.IsAttached)
                        break;
                }
            }

            var useSkiaShim = args.Contains("--skiashim");
#if !AVALONIA_SKIA_SHIM
            if (useSkiaShim)
            {
                Console.Error.WriteLine(
                    "The --skiashim option requires -p:UseSkiaSharpShim=true when building ControlCatalog.Desktop.");
                return 2;
            }
#endif

            var builder = useSkiaShim
                ? BuildSkiaShimApp()
                : args.Contains("--skia")
                    ? BuildSkiaApp()
                    : BuildAvaloniaApp();

            double GetScaling()
            {
                var idx = Array.IndexOf(args, "--scaling");
                if (idx >= 0 && args.Length > idx + 1 &&
                    double.TryParse(args[idx + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var scaling))
                    return scaling;
                return 1;
            }
            SurfaceOrientation GetOrientation()
            {
                var idx = Array.IndexOf(args, "--orientation");
                if (idx >= 0 && args.Length > idx + 1 &&
                    Enum.TryParse<SurfaceOrientation>(args[idx + 1], true, out var orientation))
                    return orientation;
                return SurfaceOrientation.Rotation0;
            }
            string? GetCard()
            {
                var idx = Array.IndexOf(args, "--card");
                if (idx >= 0 && args.Length > idx + 1)
                    return args[idx + 1];
                return null;
            }
            if (args.Contains("--fbdev"))
            {
                 SilenceConsole();
                 return builder.StartLinuxFbDev(args, new FbDevOutputOptions()
                 {
                     Scaling = GetScaling()
                 });
            }
            else if (args.Contains("--vnc"))
            {
                return builder.StartWithHeadlessVncPlatform(null, 5901, args, ShutdownMode.OnMainWindowClose);
            }
            else if (args.Contains("--full-headless"))
            {
                return builder
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = true
                    })
                    .AfterSetup(_ =>
                    {
                        DispatcherTimer.RunOnce(async () =>
                        {
                            var window = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!)
                                .MainWindow!;
                            var vm = window.DataContext;
                            if (vm != null)
                            {
                                var pagesProp = vm.GetType().GetProperty("Pages");
                                var indexProp = vm.GetType().GetProperty("SelectedPageIndex");
                                if (pagesProp != null && indexProp != null)
                                {
                                    var pages = (System.Collections.IList)pagesProp.GetValue(vm)!;
                                    for (int i = 0; i < pages.Count; i++)
                                    {
                                        var page = pages[i];
                                        var headerProp = page?.GetType().GetProperty("Header");
                                        var header = headerProp?.GetValue(page)?.ToString();
                                        Console.WriteLine($"Selecting page {i}: {header}");
                                        indexProp.SetValue(vm, i);
                                        await Task.Delay(100);
                                    }
                                }
                            }
                            await Task.Delay(500);
                            Console.WriteLine("Clicked through all pages, triggering GC");
                            for (var c = 0; c < 3; c++)
                            {
                                GC.Collect(2, GCCollectionMode.Forced);
                                await Task.Delay(50);
                            }

                            void FormatMem(string metric, long bytes)
                            {
                                Console.WriteLine(metric + ": " + bytes / 1024 / 1024 + "MB");
                            }

                            FormatMem("GC allocated bytes", GC.GetTotalMemory(true));
                            FormatMem("WorkingSet64", Process.GetCurrentProcess().WorkingSet64);
                        }, TimeSpan.FromSeconds(1));
                    })
                    .StartWithClassicDesktopLifetime(args);
            }
            else if (args.Contains("--drm"))
            {
                SilenceConsole();
                return builder.StartLinuxDrm(args, card: GetCard(), options: new DrmOutputOptions()
                {
                    Scaling = GetScaling(),
                    Orientation = GetOrientation(),
                });
            }
            else if (args.Contains("--dxgi"))
            {
                builder.With(new Win32PlatformOptions()
                {
                    CompositionMode = [Win32CompositionMode.LowLatencyDxgiSwapChain]
                });
                return builder.StartWithClassicDesktopLifetime(args);
            }
            else
                return builder.StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// This method is needed for IDE previewer infrastructure
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => ConfigureAppBuilder(AppBuilder.Configure<App>()
                .UseSilkNet()
                .UseProGpu());

        private static AppBuilder BuildSkiaApp()
            => ConfigureAppBuilder(AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseRenderingSubsystem(
                    Avalonia.Skia.SkiaPlatform.Initialize,
                    "Skia"));

        private static AppBuilder BuildSkiaShimApp()
            => ConfigureAppBuilder(AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .UseRenderingSubsystem(
                        Avalonia.Skia.SkiaPlatform.Initialize,
                        "SkiaSharp shim"),
                forceSoftwareRendering: true);

        private static AppBuilder ConfigureAppBuilder(AppBuilder builder, bool forceSoftwareRendering = false)
            => builder
                .UseHarfBuzz()
                .With(new X11PlatformOptions
                {
                    RenderingMode = forceSoftwareRendering
                        ? [X11RenderingMode.Software]
                        : [X11RenderingMode.Glx, X11RenderingMode.Software],
                    EnableMultiTouch = true,
                    UseDBusMenu = true,
                    EnableIme = true,
                })
                .With(new AvaloniaNativePlatformOptions
                {
                    RenderingMode = forceSoftwareRendering
                        ? [AvaloniaNativeRenderingMode.Software]
                        : [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl,
                            AvaloniaNativeRenderingMode.Software]
                })
                .With(new Win32PlatformOptions
                {
                    RenderingMode = forceSoftwareRendering
                        ? [Win32RenderingMode.Software]
                        : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                })

                .With(new VulkanOptions
                {
                    VulkanInstanceCreationOptions = new ()
                    {
                        UseDebug = true
                    }
                })
                .With(new CompositionOptions()
                {
                    UseRegionDirtyRectClipping = false
                })
                .WithInterFont()
                .WithDeveloperTools()
                .AfterSetup(builder =>
                {
                    EmbedSample.Implementation = OperatingSystem.IsWindows() ? new EmbedSampleWin()
                        : OperatingSystem.IsMacOS() ? new EmbedSampleMac()
                        : OperatingSystem.IsLinux() ? new EmbedSampleGtk()
                        : null;
                })
                .LogToTrace();

        private static void SilenceConsole()
        {
            new Thread(() =>
            {
                Console.CursorVisible = false;
                while (true)
                    Console.ReadKey(true);
            })
            { IsBackground = true }.Start();
        }
    }
}
