using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Threading;

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
    private static readonly Dictionary<string, FileSystemWatcher> s_directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> s_sourceToType = new(StringComparer.OrdinalIgnoreCase);

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
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
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void Register(string xamlClassName, RuntimeHotReloadMetadata metadata)
    {
        var manager = GetOrCreate();
        manager.Register(xamlClassName, metadata);
        RegisterWatcherForEntry(xamlClassName, metadata);
    }

    /// <summary>
    /// Registers multiple manifest entries with the current manager if present.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void RegisterRange(IEnumerable<KeyValuePair<string, RuntimeHotReloadMetadata>> manifest)
    {
        var entries = manifest as IList<KeyValuePair<string, RuntimeHotReloadMetadata>> ?? manifest.ToList();
        var manager = GetOrCreate();
        manager.RegisterRange(entries);
        RegisterWatchersForManifest(entries);
    }

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
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void ClearDelegates() => GetOrCreate().ClearDelegates();

    /// <summary>
    /// Clears cached delegates for the specified XAML classes.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void InvalidateDelegates(IEnumerable<string> xamlClassNames)
        => GetOrCreate().InvalidateDelegates(xamlClassNames);

    /// <summary>
    /// Registers a live instance that should receive hot reload updates when metadata changes.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
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
            foreach (var watcher in s_directoryWatchers.Values)
                watcher.Dispose();
            s_directoryWatchers.Clear();
            s_sourceToType.Clear();
#if !NETSTANDARD2_0
            s_lastReload.Clear();
#endif
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
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

    private static void RegisterWatchersForManifest(IEnumerable<KeyValuePair<string, RuntimeHotReloadMetadata>> manifest)
    {
        foreach (var entry in manifest)
            RegisterWatcherForEntry(entry.Key, entry.Value);
    }

    private static void RegisterWatcherForEntry(string typeName, RuntimeHotReloadMetadata metadata)
    {
#if NETSTANDARD2_0
        _ = typeName;
        _ = metadata;
#else
        if (string.IsNullOrEmpty(metadata.SourcePath))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(metadata.SourcePath);
        }
        catch
        {
            return;
        }

        lock (s_gate)
        {
            s_sourceToType[fullPath] = typeName;
        }

        EnsureDirectoryWatcher(fullPath);
#endif
    }

#if !NETSTANDARD2_0
    private static readonly Dictionary<string, DateTime> s_lastReload = new(StringComparer.OrdinalIgnoreCase);

    private static void EnsureDirectoryWatcher(string fullSourcePath)
    {
        var directory = Path.GetDirectoryName(fullSourcePath);
        if (string.IsNullOrEmpty(directory))
            return;

        lock (s_gate)
        {
            if (s_directoryWatchers.ContainsKey(directory))
                return;

            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnWatcherChanged;
            watcher.Created += OnWatcherChanged;
            watcher.Renamed += OnWatcherRenamed;
            s_directoryWatchers[directory] = watcher;
        }
    }

    private static void OnWatcherChanged(object? sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
            return;

        HandleSourceChange(e.FullPath);
    }

    private static void OnWatcherRenamed(object? sender, RenamedEventArgs e)
    {
        var oldFull = Path.GetFullPath(e.OldFullPath);
        var newFull = Path.GetFullPath(e.FullPath);
        string? typeName = null;

        lock (s_gate)
        {
            if (s_sourceToType.TryGetValue(oldFull, out var value))
            {
                s_sourceToType.Remove(oldFull);
                s_sourceToType[newFull] = value;
                typeName = value;
            }
            else if (s_sourceToType.TryGetValue(newFull, out value))
            {
                typeName = value;
            }
        }

        if (typeName != null)
            HandleSourceChange(newFull);
    }

    private static void HandleSourceChange(string path)
    {
        string? typeName;
        path = Path.GetFullPath(path);
        lock (s_gate)
        {
            if (!s_sourceToType.TryGetValue(path, out typeName))
                return;

            if (s_lastReload.TryGetValue(path, out var last) && (DateTime.UtcNow - last).TotalMilliseconds < 150)
                return;

            s_lastReload[path] = DateTime.UtcNow;
        }

        void Apply() => ApplyHotReloadFromFile(path, typeName!);

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private static void ApplyHotReloadFromFile(string sourcePath, string typeName)
    {
        string xaml;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                xaml = File.ReadAllText(sourcePath);
                break;
            }
            catch when (attempt < 4)
            {
                System.Threading.Thread.Sleep(50);
            }
            catch
            {
                return;
            }
        }

        var targetType = RuntimeHotReloadManager.ResolveRuntimeType(typeName);
        if (targetType is null)
            return;

        var baseUri = new Uri(sourcePath, UriKind.Absolute);
        var configuration = new RuntimeXamlLoaderConfiguration { LocalAssembly = targetType.Assembly };

        try
        {
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(baseUri, xaml), configuration);
        }
        catch
        {
            return;
        }

        var manager = GetOrCreate();
        var instances = manager.GetTrackedInstancesSnapshot(typeName);
        foreach (var instance in instances)
        {
            try
            {
                AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(baseUri, instance, xaml), configuration);
            }
            catch
            {
                // Ignore per-instance failures to avoid breaking the session.
            }
        }
    }
#endif
}
