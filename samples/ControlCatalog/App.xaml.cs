using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
#if !NETSTANDARD2_0
using System.IO;
using Avalonia.Markup.Xaml.HotReload;
#endif
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Avalonia.Themes.Fluent;
using ControlCatalog.Models;
using ControlCatalog.ViewModels;

namespace ControlCatalog
{
    public class App : Application
    {
        private readonly Styles _themeStylesContainer = new();
        private FluentTheme? _fluentTheme;
        private SimpleTheme? _simpleTheme;
        private IStyle? _colorPickerFluent, _colorPickerSimple;
        
        public App()
        {
            DataContext = new ApplicationViewModel();
        }

        public override void Initialize()
        {
            Styles.Add(_themeStylesContainer);

            AvaloniaXamlLoader.Load(this);

#if !NETSTANDARD2_0
            var hotReloadService = RuntimeHotReloadService.GetOrCreate();
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "ControlCatalog.axaml.hotreload.json");
            if (File.Exists(manifestPath))
            {
                var tfa = typeof(RuntimeHotReloadManifest).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false);
                if (tfa.Length > 0 && tfa[0] is System.Runtime.Versioning.TargetFrameworkAttribute attr)
                {
                    Console.WriteLine($"[HotReload] RuntimeHotReloadManifest TFM: {attr.FrameworkName}");
                }
                try
                {
                    var manifest = RuntimeHotReloadManifest.Load(manifestPath);
                    Console.WriteLine($"[HotReload] Manifest entries: {manifest.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HotReload] Failed to load manifest directly: {ex}");
                }
                RuntimeHotReloadService.RegisterManifestPath(manifestPath);
                RuntimeHotReloadService.ReloadRegisteredManifests();
                var snapshot = RuntimeHotReloadService.GetStatusSnapshot();
                Console.WriteLine($"[HotReload] Registered manifests: {string.Join(", ", snapshot.ManifestPaths)}");
                Console.WriteLine($"[HotReload] Watcher count: {snapshot.WatcherPaths.Count}");
                foreach (var registration in snapshot.Registrations)
                {
                    Console.WriteLine($"[HotReload] Registration: {registration.XamlClassName} tracked={registration.TrackedInstanceCount} live={registration.LiveInstanceCount}");
                }
            }
            else
            {
                Console.WriteLine($"[HotReload] Manifest not found at {manifestPath}");
            }
#endif

#if DEBUG
            Environment.SetEnvironmentVariable("AVALONIA_DISABLE_TEXT_POOL_VERIFICATION", "1");
#endif

            _fluentTheme = (FluentTheme)Resources["FluentTheme"]!;
            _simpleTheme = (SimpleTheme)Resources["SimpleTheme"]!;
            _colorPickerFluent = (IStyle)Resources["ColorPickerFluent"]!;
            _colorPickerSimple = (IStyle)Resources["ColorPickerSimple"]!;
            
            SetCatalogThemes(CatalogTheme.Fluent);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            }
            else if(ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            {
                singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainWindowViewModel() };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
            {
                singleViewLifetime.MainView = new MainView { DataContext = new MainWindowViewModel() };
            }

            if (this.TryGetFeature<IActivatableLifetime>() is {} activatableApplicationLifetime)
            {
                activatableApplicationLifetime.Activated += (sender, args) =>
                    Console.WriteLine($"App activated: {args.Kind}");
                activatableApplicationLifetime.Deactivated += (sender, args) =>
                    Console.WriteLine($"App deactivated: {args.Kind}");
            }

            base.OnFrameworkInitializationCompleted();
        }

        private CatalogTheme _prevTheme;
        public static CatalogTheme CurrentTheme => ((App)Current!)._prevTheme; 
        public static void SetCatalogThemes(CatalogTheme theme)
        {
            var app = (App)Current!;
            var prevTheme = app._prevTheme;
            app._prevTheme = theme;
            var shouldReopenWindow = prevTheme != theme;
            
            if (app._themeStylesContainer.Count == 0)
            {
                app._themeStylesContainer.Add(new Style());
                app._themeStylesContainer.Add(new Style());
                app._themeStylesContainer.Add(new Style());
            }

            if (theme == CatalogTheme.Fluent)
            {
                app._themeStylesContainer[0] = app._fluentTheme!;
                app._themeStylesContainer[1] = app._colorPickerFluent!;
            }
            else if (theme == CatalogTheme.Simple)
            {
                app._themeStylesContainer[0] = app._simpleTheme!;
                app._themeStylesContainer[1] = app._colorPickerSimple!;
            }

            if (shouldReopenWindow)
            {
                if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                {
                    var oldWindow = desktopLifetime.MainWindow;
                    var newWindow = new MainWindow();
                    desktopLifetime.MainWindow = newWindow;
                    newWindow.Show();
                    oldWindow?.Close();
                }
                else if (app.ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
                {
                    singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainWindowViewModel() };
                }
                else if (app.ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
                {
                    singleViewLifetime.MainView = new MainView();
                }
            }
        }
    }
}
