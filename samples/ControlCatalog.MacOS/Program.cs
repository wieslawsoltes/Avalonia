using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.MacOS;
using Avalonia.Threading;
using ControlCatalog.Pages;

namespace ControlCatalog.MacOS;

internal static class Program
{
    private static readonly List<object> s_evidence = new();
    private static string s_output = "artifacts/macos-theme";
    private static bool s_headless;

    [STAThread]
    private static int Main(string[] args)
    {
        s_headless = args.Contains("--headless", StringComparer.Ordinal);
        var index = Array.IndexOf(args, "--output");
        if (index >= 0)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException("--output requires a directory.");
            s_output = Path.GetFullPath(args[index + 1]);
        }
        Directory.CreateDirectory(s_output);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Environment.SetEnvironmentVariable("AVALONIA_CATALOG_THEME", "MacOS");
        var builder = AppBuilder.Configure<ControlCatalog.App>().UseSkia().UseHarfBuzz();
        builder = s_headless
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true })
            : builder.UsePlatformDetect();
        if (s_headless)
            builder = builder.WithInterFont();
        return builder.AfterSetup(_ => DispatcherTimer.RunOnce(Capture, TimeSpan.FromMilliseconds(350)))
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static async void Capture()
    {
        var app = (ControlCatalog.App)Application.Current!;
        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
        try
        {
            var window = lifetime.MainWindow ?? throw new InvalidOperationException("ControlCatalog did not create a main window.");
            window.Width = 1280;
            window.Height = 1040;
            var theme = app.MacOSTheme;
            theme.ReduceMotion = true;
            if (s_headless)
                theme.SetToken(MacOSTokens.ContentControlThemeFontFamily, new FontFamily("fonts:Inter#Inter"));
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = variant;
                window.Content = new MacOSThemePage();
                await Save(window, "overview-" + variant.Key);
            }
            window.RequestedThemeVariant = ThemeVariant.Light;
            theme.DensityStyle = DensityStyle.Compact;
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-compact");
            theme.DensityStyle = DensityStyle.Normal;
            theme.IncreaseContrast = true;
            theme.ReduceTransparency = true;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = variant;
                window.Content = new MacOSThemePage();
                await Save(window, "overview-" + variant.Key + "-contrast");
            }
            theme.IncreaseContrast = null;
            theme.ReduceTransparency = false;
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.FlowDirection = FlowDirection.RightToLeft;
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-rtl");
            window.FlowDirection = FlowDirection.LeftToRight;
            theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse("#F5CE42"));
            window.Content = new MacOSThemePage();
            await Save(window, "overview-Light-custom-accent");
            theme.ResetToken(MacOSTokens.SystemAccentColor);

            var requested = new HashSet<string>(StringComparer.Ordinal)
            {
                "Buttons", "CheckBox", "RadioButton", "ToggleSwitch", "TextBox", "ComboBox",
                "NumericUpDown", "Slider", "TreeView", "TableView", "Menu", "ColorPicker",
                "CommandBar", "Expander", "TabControl", "SplitView", "DatePicker", "TimePicker",
                "DateTimePicker", "CalendarDatePicker", "AutoCompleteBox", "ScrollViewer"
            };
            foreach (var item in MacOSCatalogPages.Create().Where(p => requested.Contains(p.Header)))
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = variant;
                    window.Content = item.CreatePage();
                    await Save(window, "catalog-" + item.Header + "-" + variant.Key);
                }
            }
            var page = new MacOSThemePage();
            window.Content = page;
            window.RequestedThemeVariant = ThemeVariant.Light;
            await Task.Delay(150);
            page.FindControl<Button>("PrimaryAction")!.Focus(NavigationMethod.Tab);
            await Save(window, "overview-Light-keyboard-focus");
            var popupButton = page.FindControl<Button>("PopupAction")!;
            popupButton.Flyout!.ShowAt(popupButton);
            await Save(window, "overview-Light-popover");
            popupButton.Flyout.Hide();

            File.WriteAllText(Path.Combine(s_output, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                commit = Environment.GetEnvironmentVariable("MACOS_THEME_COMMIT") ?? "local-working-tree",
                operatingSystem = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                windowing = s_headless ? "Avalonia.Headless" : "native platform backend",
                capture = "RenderTargetBitmap of the running ControlCatalog window client area; not an OS desktop screenshot",
                screenshots = s_evidence
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Captured {s_evidence.Count} real ControlCatalog frames.");
            lifetime.Shutdown(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(s_output, "capture-error.txt"), error.ToString());
            lifetime.Shutdown(1);
        }
    }

    private static async Task Save(Window window, string name)
    {
        await Task.Delay(180);
        window.UpdateLayout();
        const double renderScale = 2;
        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width * renderScale), (int)Math.Ceiling(window.Bounds.Height * renderScale));
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Cannot capture an unarranged window.");
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * renderScale, 96 * renderScale));
        bitmap.Render(window);
        var path = Path.Combine(s_output, name + ".png");
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        var theme = ((ControlCatalog.App)Application.Current!).MacOSTheme;
        s_evidence.Add(new
        {
            file = Path.GetFileName(path), width = size.Width, height = size.Height, renderScale, dpi = 96 * renderScale,
            font = s_headless ? "Inter (explicit headless fallback)" : "platform default",
            variant = window.ActualThemeVariant.Key.ToString(), density = theme.DensityStyle.ToString(),
            increaseContrast = theme.IncreaseContrast, reduceMotion = theme.ReduceMotion,
            reduceTransparency = theme.ReduceTransparency, flowDirection = window.FlowDirection.ToString(),
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        });
        Console.WriteLine("Captured " + name);
    }
}
