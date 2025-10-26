using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Avalonia;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Helper facade for accessing <see cref="RuntimeHotReloadManager"/> through <see cref="AvaloniaLocator"/>.
/// </summary>
public static class RuntimeHotReloadService
{
    /// <summary>
    /// Gets the registered hot reload manager, creating and registering a singleton if necessary.
    /// </summary>
    private static readonly object s_gate = new();
    private static readonly List<Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>>> s_manifestProviders = new();
    private static readonly HashSet<string> s_registeredManifestPaths = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_manifestPathsInitialized;

    public static RuntimeHotReloadManager GetOrCreate()
    {
        var manager = AvaloniaLocator.Current.GetService<RuntimeHotReloadManager>();
        if (manager is null)
        {
            manager = new RuntimeHotReloadManager();
            AvaloniaLocator.CurrentMutable.Bind<RuntimeHotReloadManager>().ToConstant(manager);
        }

        EnsureManifestPathsRegistered();
        return manager;
    }

    /// <summary>
    /// Registers metadata for a single XAML class with the current manager if present.
    /// </summary>
    public static void Register(string xamlClassName, RuntimeHotReloadMetadata metadata)
        => GetOrCreate().Register(xamlClassName, metadata);

    /// <summary>
    /// Registers multiple manifest entries with the current manager if present.
    /// </summary>
    public static void RegisterRange(IEnumerable<KeyValuePair<string, RuntimeHotReloadMetadata>> manifest)
        => GetOrCreate().RegisterRange(manifest);

    /// <summary>
    /// Loads metadata entries from the specified manifest file and registers them.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void RegisterManifest(string path)
    {
        var manifest = RuntimeHotReloadManifest.Load(path);
        RegisterRange(manifest);
    }

    /// <summary>
    /// Loads metadata entries from the provided manifest stream and registers them.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void RegisterManifest(Stream manifestStream)
    {
        var manifest = RuntimeHotReloadManifest.Load(manifestStream);
        RegisterRange(manifest);
    }

    /// <summary>
    /// Registers a manifest file that will be reloaded on each hot reload cycle.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void RegisterManifestPath(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        path = Path.GetFullPath(path);
        lock (s_gate)
        {
            if (!s_registeredManifestPaths.Add(path))
                return;
        }

        RegisterManifestProvider(() => RuntimeHotReloadManifest.Load(path));
        ReloadRegisteredManifests();
    }

    /// <summary>
    /// Clears cached delegates for all registered XAML classes.
    /// </summary>
    public static void ClearDelegates() => GetOrCreate().ClearDelegates();

    /// <summary>
    /// Clears cached delegates for the specified XAML classes.
    /// </summary>
    public static void InvalidateDelegates(IEnumerable<string> xamlClassNames)
        => GetOrCreate().InvalidateDelegates(xamlClassNames);

    /// <summary>
    /// Registers a live instance that should receive hot reload updates when metadata changes.
    /// </summary>
    public static void Track(object instance) => GetOrCreate().TrackInstance(instance);

    /// <summary>
    /// Registers a callback that supplies manifest entries whenever hot reload is triggered.
    /// </summary>
    public static void RegisterManifestProvider(Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>> provider)
    {
        if (provider is null)
            throw new ArgumentNullException(nameof(provider));

        lock (s_gate)
            s_manifestProviders.Add(provider);
    }

    /// <summary>
    /// Reloads manifests from registered providers.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void ReloadRegisteredManifests()
    {
        Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>>[] providers;
        lock (s_gate)
            providers = s_manifestProviders.ToArray();

        if (providers.Length == 0)
            return;

        foreach (var provider in providers)
        {
            var manifest = provider();
            if (manifest is null)
                continue;
            RegisterRange(manifest);
        }
    }

    /// <summary>
    /// Removes all registered manifest providers. Intended for test scenarios.
    /// </summary>
    public static void ClearManifestProviders()
    {
        lock (s_gate)
        {
            s_manifestProviders.Clear();
            s_registeredManifestPaths.Clear();
            s_manifestPathsInitialized = false;
        }
    }

    private static void EnsureManifestPathsRegistered()
    {
        lock (s_gate)
        {
            if (s_manifestPathsInitialized)
                return;
            s_manifestPathsInitialized = true;
        }

        try
        {
            var env = Environment.GetEnvironmentVariable("AVALONIA_HOTRELOAD_MANIFEST_PATHS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                foreach (var raw in env.Split(Path.PathSeparator))
                {
                    var path = raw?.Trim();
                    if (string.IsNullOrEmpty(path))
                        continue;
                    if (File.Exists(path))
                        RegisterManifestPath(path);
                }
            }

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
            {
                foreach (var file in Directory.EnumerateFiles(baseDir, "*.axaml.hotreload.json"))
                    RegisterManifestPath(file);
            }

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly?.Location is { } location && !string.IsNullOrEmpty(location))
            {
                var candidate = Path.ChangeExtension(location, ".axaml.hotreload.json");
                if (File.Exists(candidate))
                    RegisterManifestPath(candidate);
            }
        }
        catch
        {
            // Ignore manifest auto-registration errors.
        }

        ReloadRegisteredManifests();
    }
}
