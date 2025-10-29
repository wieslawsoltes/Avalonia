using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;

namespace Avalonia.Markup.Xaml.HotReload;

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Runtime hot reload is a development-only feature that requires dynamic access to generated types.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Runtime hot reload is a development-only feature that requires dynamic code generation.")]
public static class RuntimeHotReloadService
{
    /// <summary>
    /// Gets the registered hot reload manager, creating and registering a singleton if necessary.
    /// </summary>
    private static readonly object s_gate = new();
    private static readonly List<Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>>> s_manifestProviders = new();
    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>>> s_manifestProvidersByKey = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_registeredManifestPaths = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_manifestPathsInitialized;
    private static readonly Dictionary<string, FileSystemWatcher> s_directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> s_sourceToType = new(StringComparer.OrdinalIgnoreCase);

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
    private static readonly object s_reloadQueueGate = new();
    private static Task s_reloadQueue = Task.CompletedTask;
    private static readonly object s_throttleGate = new();
    private static readonly Dictionary<string, ThrottleState> s_throttleStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan s_throttleBurstWindow = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan s_throttleResetWindow = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan s_throttleMaxDelay = TimeSpan.FromMilliseconds(1500);

    internal static Task WaitForPendingReloadsAsync()
    {
        lock (s_reloadQueueGate)
            return s_reloadQueue;
    }
#endif

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
        try
        {
            var manifest = RuntimeHotReloadManifest.Load(path);
            RegisterRange(manifest);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to load hot reload manifest '{0}'.",
                ex,
                path);
        }
    }

    /// <summary>
    /// Loads metadata entries from the provided manifest stream and registers them.
    /// </summary>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static void RegisterManifest(Stream manifestStream)
    {
        try
        {
            var manifest = RuntimeHotReloadManifest.Load(manifestStream);
            RegisterRange(manifest);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to load hot reload manifest from stream.",
                ex);
        }
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

        RegisterManifestProvider(() => RuntimeHotReloadManifest.Load(path), path);
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

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
    /// <summary>
    /// Explicitly registers a hot-reloadable view so that its state and reload lifecycle are tracked.
    /// </summary>
    public static void RegisterHotReloadView(IHotReloadableView view)
    {
        if (view is null)
            throw new ArgumentNullException(nameof(view));

        GetOrCreate().TrackInstance(view);
    }

    /// <summary>
    /// Removes a hot-reloadable view from the registry.
    /// </summary>
    public static void UnregisterHotReloadView(IHotReloadableView view)
    {
        if (view is null)
            throw new ArgumentNullException(nameof(view));

        HotReloadViewRegistry.Unregister(view);
    }
#endif

    /// <summary>
    /// Registers a callback that supplies manifest entries whenever hot reload is triggered.
    /// </summary>
    public static void RegisterManifestProvider(Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>> provider)
        => RegisterManifestProvider(provider, cacheKey: null);

    public static void RegisterManifestProvider(
        Func<IReadOnlyDictionary<string, RuntimeHotReloadMetadata>> provider,
        string? cacheKey)
    {
        if (provider is null)
            throw new ArgumentNullException(nameof(provider));

        lock (s_gate)
        {
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (s_manifestProvidersByKey.TryGetValue(cacheKey, out var existing))
                {
                    if (ReferenceEquals(existing, provider))
                        return;

                    var index = s_manifestProviders.IndexOf(existing);
                    if (index >= 0)
                        s_manifestProviders[index] = provider;
                    else
                        s_manifestProviders.Add(provider);

                    s_manifestProvidersByKey[cacheKey] = provider;
                    return;
                }

                s_manifestProvidersByKey[cacheKey] = provider;
            }
            else
            {
                if (s_manifestProviders.Contains(provider))
                    return;
            }

            s_manifestProviders.Add(provider);
        }
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
            try
            {
                var manifest = provider();
                if (manifest is null)
                {
                    HotReloadDiagnostics.ReportWarning(
                        "Hot reload manifest provider returned no data.");
                    continue;
                }

                try
                {
                    RegisterRange(manifest);
                }
                catch (Exception ex)
                {
                    HotReloadDiagnostics.ReportError(
                        "Failed to register hot reload manifest entries provided by a manifest provider.",
                        ex);
                }
            }
            catch (Exception ex)
            {
                HotReloadDiagnostics.ReportError(
                    "Hot reload manifest provider threw an exception.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Produces a snapshot of the currently registered hot reload manifests, watchers, and tracked instances.
    /// </summary>
    public static RuntimeHotReloadStatus GetStatusSnapshot()
    {
        string[] manifestPaths;
        string[] watcherPaths;

        lock (s_gate)
        {
            manifestPaths = s_registeredManifestPaths.Count == 0
                ? Array.Empty<string>()
                : s_registeredManifestPaths.ToArray();

            watcherPaths = s_sourceToType.Count == 0
                ? Array.Empty<string>()
                : s_sourceToType.Keys.ToArray();
        }

        var manager = AvaloniaLocator.Current.GetService<RuntimeHotReloadManager>();
        var registrations = manager?.CreateSnapshot() ?? Array.Empty<RuntimeHotReloadRegistration>();
        var activeViews = HotReloadViewRegistry.GetActiveViews()
            .Select(v => v.GetType().FullName ?? v.GetType().Name)
            .ToArray();
        var replacements = HotReloadViewRegistry.GetReplacementStats();

        return new RuntimeHotReloadStatus(manifestPaths, watcherPaths, registrations, activeViews, replacements);
    }

    /// <summary>
    /// Removes all registered manifest providers. Intended for test scenarios.
    /// </summary>
    public static void ClearManifestProviders()
    {
        lock (s_gate)
        {
            s_manifestProviders.Clear();
            s_manifestProvidersByKey.Clear();
            s_registeredManifestPaths.Clear();
            s_manifestPathsInitialized = false;
            foreach (var watcher in s_directoryWatchers.Values)
                watcher.Dispose();
            s_directoryWatchers.Clear();
            s_sourceToType.Clear();
#if !NETSTANDARD2_0
            s_lastReload.Clear();
#endif
#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
            lock (s_reloadQueueGate)
                s_reloadQueue = Task.CompletedTask;
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
                    {
                        RegisterManifestPath(path);
                    }
                    else
                    {
                        HotReloadDiagnostics.ReportWarning(
                            "Hot reload manifest path '{0}' was not found.",
                            path);
                    }
                }
            }

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
            {
                foreach (var file in Directory.EnumerateFiles(baseDir, "*.axaml.hotreload.json"))
                    RegisterManifestPath(file);
            }
            else if (!string.IsNullOrEmpty(baseDir))
            {
                HotReloadDiagnostics.ReportWarning(
                    "Hot reload base directory '{0}' was not found.",
                    baseDir);
            }

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly?.Location is { } location && !string.IsNullOrEmpty(location))
            {
                var candidate = Path.ChangeExtension(location, ".axaml.hotreload.json");
                if (File.Exists(candidate))
                    RegisterManifestPath(candidate);
            }
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Automatic hot reload manifest discovery failed.",
                ex);
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
#if NETSTANDARD2_0 && !NET6_0_OR_GREATER
        _ = typeName;
        _ = metadata;
#else
        if (string.IsNullOrEmpty(metadata.SourcePath))
        {
            if (!string.IsNullOrEmpty(metadata.RelativeSourcePath))
            {
                HotReloadDiagnostics.ReportWarning(
                    "Hot reload metadata for '{0}' had no absolute source path; available relative path: '{1}'.",
                    typeName,
                    metadata.RelativeSourcePath);
            }
            else
            {
                HotReloadDiagnostics.ReportWarning(
                    "Hot reload metadata for '{0}' did not include a source path; skipping file watcher.",
                    typeName);
            }
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(metadata.SourcePath);
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to normalize hot reload source path for '{0}' ({1}).",
                ex,
                typeName,
                metadata.SourcePath ?? "<null>");
            return;
        }

        lock (s_gate)
        {
            s_sourceToType[fullPath] = typeName;
        }

        EnsureDirectoryWatcher(fullPath);
#endif
    }

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
    private static readonly Dictionary<string, DateTime> s_lastReload = new(StringComparer.OrdinalIgnoreCase);
    private static readonly MethodInfo? s_runtimeXamlLoaderMethod = Type.GetType("Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml.Loader")?
        .GetMethod("Load", BindingFlags.Public | BindingFlags.Static, binder: null, new[] { typeof(RuntimeXamlLoaderDocument), typeof(RuntimeXamlLoaderConfiguration) }, modifiers: null);

    private static void EnsureDirectoryWatcher(string fullSourcePath)
    {
        var directory = Path.GetDirectoryName(fullSourcePath);
        if (string.IsNullOrEmpty(directory))
            return;
        if (!Directory.Exists(directory))
        {
            HotReloadDiagnostics.ReportWarning(
                "Hot reload source directory '{0}' was not found; disabling file watcher.",
                directory);
            return;
        }

        lock (s_gate)
        {
            if (s_directoryWatchers.ContainsKey(directory))
                return;

            try
            {
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
            catch (Exception ex)
            {
                HotReloadDiagnostics.ReportError(
                    "Failed to create file watcher for '{0}'.",
                    ex,
                    directory);
            }
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static void OnWatcherChanged(object? sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
            return;

        HandleSourceChange(e.FullPath);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
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

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static void HandleSourceChange(string path)
    {
        string? typeName;
        path = Path.GetFullPath(path);
        lock (s_gate)
        {
            if (!s_sourceToType.TryGetValue(path, out typeName))
            {
                HotReloadDiagnostics.ReportInfo(
                    "Received hot reload notification for untracked source '{0}'.",
                    path);
                return;
            }

            if (s_lastReload.TryGetValue(path, out var last) && (DateTime.UtcNow - last).TotalMilliseconds < 150)
                return;

            s_lastReload[path] = DateTime.UtcNow;
        }

#if NETSTANDARD2_0 && !NET6_0_OR_GREATER
        Dispatcher.UIThread.Post(
            () => ApplyHotReloadOnUiThread(path, typeName!),
            DispatcherPriority.Send);
#else
        QueueHotReload(path, typeName!);
#endif
    }

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
    private static Task QueueHotReload(string sourcePath, string typeName)
        => EnqueueHotReloadWork(() => ApplyHotReloadFromFileAsync(sourcePath, typeName), sourcePath);

    private static Task EnqueueHotReloadWork(Func<Task> work, string? throttleKey = null)
    {
        lock (s_reloadQueueGate)
        {
            var continuation = s_reloadQueue
                .ContinueWith(
                    _ => throttleKey is null
                        ? work()
                        : ApplyThrottledAsync(work, throttleKey),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();

            s_reloadQueue = continuation;
            return continuation;
        }
    }

    private static async Task ApplyHotReloadFromFileAsync(string sourcePath, string typeName)
    {
        var scope = typeName;
        HotReloadDiagnostics.ReportReloadStart(scope, "ApplyFromFile", new object?[] { Path.GetFileName(sourcePath) });
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => ApplyHotReloadOnUiThread(sourcePath, typeName),
                DispatcherPriority.Send);
            stopwatch.Stop();
            HotReloadDiagnostics.ReportReloadSuccess(scope, stopwatch.Elapsed, "ApplyFromFile", new object?[] { Path.GetFileName(sourcePath) });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            HotReloadDiagnostics.ReportError(
                "Failed to apply hot reload for '{0}'.",
                ex,
                typeName);
            HotReloadDiagnostics.ReportReloadFailure(scope, stopwatch.Elapsed, "ApplyFromFile", ex, new object?[] { Path.GetFileName(sourcePath) });
        }
    }

    private static async Task ApplyHotReloadFromXamlAsync(string typeName, string xaml)
    {
        var scope = typeName;
        HotReloadDiagnostics.ReportReloadStart(scope, "ApplyFromXaml", Array.Empty<object?>());
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => ApplyHotReloadFromXamlOnUiThread(typeName, xaml),
                DispatcherPriority.Send);
            stopwatch.Stop();
            HotReloadDiagnostics.ReportReloadSuccess(scope, stopwatch.Elapsed, "ApplyFromXaml", Array.Empty<object?>());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            HotReloadDiagnostics.ReportError(
                "Failed to apply hot reload for '{0}'.",
                ex,
                typeName);
            HotReloadDiagnostics.ReportReloadFailure(scope, stopwatch.Elapsed, "ApplyFromXaml", ex, Array.Empty<object?>());
        }
    }

    private static async Task ApplyThrottledAsync(Func<Task> work, string throttleKey)
    {
        TimeSpan delay;
        ThrottleState state;
        lock (s_throttleGate)
        {
            if (!s_throttleStates.TryGetValue(throttleKey, out state!))
            {
                state = new ThrottleState();
                s_throttleStates[throttleKey] = state;
            }

            var now = DateTime.UtcNow;
            if (state.LastDispatchUtc != default && now - state.LastDispatchUtc >= s_throttleResetWindow)
                state.Delay = TimeSpan.Zero;

            if (state.LastRequestUtc != default && now - state.LastRequestUtc < s_throttleBurstWindow)
            {
                if (state.Delay == TimeSpan.Zero)
                    state.Delay = s_throttleBurstWindow;
                else
                {
                    var increased = state.Delay.TotalMilliseconds * 2;
                    if (increased > s_throttleMaxDelay.TotalMilliseconds)
                        increased = s_throttleMaxDelay.TotalMilliseconds;
                    state.Delay = TimeSpan.FromMilliseconds(increased);
                }
            }
            else if (state.Delay > TimeSpan.Zero && now - state.LastRequestUtc >= s_throttleResetWindow)
            {
                state.Delay = TimeSpan.Zero;
            }

            state.LastRequestUtc = now;
            delay = state.Delay;
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay).ConfigureAwait(false);

        await work().ConfigureAwait(false);

        lock (s_throttleGate)
        {
            if (s_throttleStates.TryGetValue(throttleKey, out state!))
            {
                state.LastDispatchUtc = DateTime.UtcNow;
            }
        }
    }

    private sealed class ThrottleState
    {
        public TimeSpan Delay;
        public DateTime LastRequestUtc;
        public DateTime LastDispatchUtc;
    }
#endif

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static void ApplyHotReloadFromFile(string sourcePath, string typeName)
    {
        string? xaml = null;
        Exception? readException = null;

        for (var attempt = 0; attempt < 5; attempt++)
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
            catch (Exception ex)
            {
                readException = ex;
                break;
            }
        }

        if (xaml is null)
        {
            if (readException is not null)
            {
                HotReloadDiagnostics.ReportError(
                    "Failed to read XAML source '{0}' for hot reload.",
                    readException,
                    sourcePath);
            }
            return;
        }

        ApplyHotReloadCore(xaml, sourcePath, typeName);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static Task ApplyHotReloadAsync(string typeName, string xaml)
    {
        if (typeName is null)
            throw new ArgumentNullException(nameof(typeName));
        if (xaml is null)
            throw new ArgumentNullException(nameof(xaml));

#if !NETSTANDARD2_0 || NET6_0_OR_GREATER
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyHotReloadFromXamlOnUiThread(typeName, xaml);
            return Task.CompletedTask;
        }

        return EnqueueHotReloadWork(() => ApplyHotReloadFromXamlAsync(typeName, xaml));
#else
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    ApplyHotReloadFromXamlOnUiThread(typeName, xaml);
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            },
            DispatcherPriority.Send);
        return tcs.Task;
#endif
    }

    private static void ApplyHotReloadOnUiThread(string sourcePath, string typeName)
    {
        try
        {
            var scope = typeName;
        HotReloadDiagnostics.ReportReloadStart(scope, "ApplyFromFileUI", new object?[] { Path.GetFileName(sourcePath) });
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                ApplyHotReloadFromFile(sourcePath, typeName);
                stopwatch.Stop();
                HotReloadDiagnostics.ReportReloadSuccess(scope, stopwatch.Elapsed, "ApplyFromFileUI", new object?[] { Path.GetFileName(sourcePath) });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                HotReloadDiagnostics.ReportReloadFailure(scope, stopwatch.Elapsed, "ApplyFromFileUI", ex, new object?[] { Path.GetFileName(sourcePath) });
                throw;
            }
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to apply hot reload for '{0}'.",
                ex,
                typeName);
        }
    }

    private static void ApplyHotReloadFromXamlOnUiThread(string typeName, string xaml)
    {
        try
        {
            var scope = typeName;
            HotReloadDiagnostics.ReportReloadStart(scope, "ApplyFromXamlUI", Array.Empty<object?>());
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                ApplyHotReloadCore(xaml, null, typeName);
                stopwatch.Stop();
                HotReloadDiagnostics.ReportReloadSuccess(scope, stopwatch.Elapsed, "ApplyFromXamlUI", Array.Empty<object?>());
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                HotReloadDiagnostics.ReportReloadFailure(scope, stopwatch.Elapsed, "ApplyFromXamlUI", ex, Array.Empty<object?>());
                throw;
            }
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to apply hot reload for '{0}'.",
                ex,
                typeName);
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static void ApplyHotReloadCore(string xaml, string? sourcePath, string typeName)
    {
        var targetType = RuntimeHotReloadManager.ResolveRuntimeType(typeName);
        if (targetType is null)
        {
            HotReloadDiagnostics.ReportWarning(
                "Unable to resolve runtime type '{0}' for hot reload.",
                typeName);
            return;
        }

        HotReloadStaticHookInvoker.Invoke(targetType);
        if (targetType is not null)
            HotReloadResourceDictionaryRegistry.NotifyTypeReloaded(targetType);

        var baseUri = sourcePath is not null
            ? new Uri(sourcePath, UriKind.Absolute)
            : new Uri($"memory://hotreload/{typeName}", UriKind.Absolute);
        var configuration = new RuntimeXamlLoaderConfiguration { LocalAssembly = targetType!.Assembly };

        try
        {
            if (s_runtimeXamlLoaderMethod == null)
            {
                HotReloadDiagnostics.ReportWarning("Runtime XAML loader is not available; skipping hot reload for '{0}'.", typeName);
                return;
            }

            s_runtimeXamlLoaderMethod.Invoke(null, new object[] { new RuntimeXamlLoaderDocument(baseUri, xaml), configuration });
        }
        catch (Exception ex)
        {
            HotReloadDiagnostics.ReportError(
                "Failed to apply hot reload to '{0}'.",
                ex,
                typeName);
            return;
        }

        var manager = GetOrCreate();
        var instances = manager.GetTrackedInstancesSnapshot(typeName);
        foreach (var instance in instances)
        {
            if (instance is IHotReloadableView view)
                HotReloadViewRegistry.NotifyReloading(view);

            var snapshot = CaptureInstanceSnapshot(instance);
            try
            {
                s_runtimeXamlLoaderMethod.Invoke(null, new object[] { new RuntimeXamlLoaderDocument(baseUri, instance, xaml), configuration });
                RestoreInstanceSnapshot(instance, snapshot);

                if (instance is IHotReloadableView reloaded)
                    HotReloadViewRegistry.NotifyReloaded(reloaded);

                if (instance is ResourceDictionary dictionary)
                    HotReloadResourceDictionaryRegistry.NotifyReloaded(dictionary);
            }
            catch (Exception ex)
            {
                HotReloadDiagnostics.ReportError(
                    "Failed to apply hot reload to an instance of '{0}'.",
                    ex,
                    typeName);
            }
        }
    }

    internal static void ApplyHotReloadForTests(string typeName, string xaml)
        => ApplyHotReloadAsync(typeName, xaml).GetAwaiter().GetResult();
#endif

#if NETSTANDARD2_0 && !NET6_0_OR_GREATER
    internal static void ApplyHotReloadForTests(string typeName, string xaml)
        => ApplyHotReloadAsync(typeName, xaml).GetAwaiter().GetResult();
#endif

    /// <summary>
    /// Captures relevant state for a tracked instance so it can be restored after the populate pass.
    /// </summary>
    private static InstanceSnapshot? CaptureInstanceSnapshot(object instance)
    {
        if (instance is null)
            return null;

        var type = instance.GetType();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        if (properties.Length == 0)
            return null;

        List<PropertySnapshot>? propertySnapshots = null;
        List<CollectionSnapshot>? collectionSnapshots = null;
        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            object? value;
            try
            {
                value = property.CanRead ? property.GetValue(instance) : null;
            }
            catch
            {
                // Ignore properties that throw during capture.
                continue;
            }

            if (property.CanRead && property.CanWrite)
            {
                propertySnapshots ??= new List<PropertySnapshot>();
                propertySnapshots.Add(new PropertySnapshot(property, value));
                continue;
            }

            if (value is null)
                continue;

            if (value is IList list)
            {
                var items = new object?[list.Count];
                for (var i = 0; i < list.Count; i++)
                    items[i] = list[i];
                collectionSnapshots ??= new List<CollectionSnapshot>();
                collectionSnapshots.Add(CollectionSnapshot.ForList(property, items));
            }
            else if (value is IDictionary dictionary)
            {
                var entries = new KeyValuePair<object?, object?>[dictionary.Count];
                var index = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    entries[index++] = new KeyValuePair<object?, object?>(entry.Key, entry.Value);
                }

                collectionSnapshots ??= new List<CollectionSnapshot>();
                collectionSnapshots.Add(CollectionSnapshot.ForDictionary(property, entries));
            }
        }

        var hasCustomState = false;
        object? customState = null;
        if (instance is IXamlHotReloadStateProvider stateProvider)
        {
            try
            {
                customState = stateProvider.CaptureHotReloadState();
                hasCustomState = true;
            }
            catch
            {
                // Ignore custom state exceptions so we still restore the basics.
            }
        }

        if (propertySnapshots is null && collectionSnapshots is null && !hasCustomState)
            return null;

        return new InstanceSnapshot(propertySnapshots, collectionSnapshots, hasCustomState, customState);
    }

    /// <summary>
    /// Restores state captured by <see cref="CaptureInstanceSnapshot"/>.
    /// </summary>
    private static void RestoreInstanceSnapshot(object instance, InstanceSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        if (snapshot.Properties is { } propertySnapshots)
        {
            foreach (var entry in propertySnapshots)
            {
                try
                {
                    entry.Property.SetValue(instance, entry.Value);
                }
                catch
                {
                    // Ignore properties that cannot be restored.
                }
            }
        }

        if (snapshot.Collections is { } collections)
        {
            foreach (var entry in collections)
            {
                try
                {
                    switch (entry.Kind)
                    {
                        case CollectionSnapshotKind.List:
                        {
                            if (entry.Property.GetValue(instance) is IList list && !list.IsReadOnly && !list.IsFixedSize)
                            {
                                list.Clear();
                                var items = entry.ListItems;
                                if (items is not null)
                                {
                                    for (var i = 0; i < items.Length; i++)
                                        list.Add(items[i]);
                                }
                            }
                            break;
                        }
                        case CollectionSnapshotKind.Dictionary:
                        {
                            if (entry.Property.GetValue(instance) is IDictionary dictionary && !dictionary.IsReadOnly && !dictionary.IsFixedSize)
                            {
                                dictionary.Clear();
                                var pairs = entry.DictionaryEntries;
                                if (pairs is not null)
                                {
                                    for (var i = 0; i < pairs.Length; i++)
                                    {
                                        var pair = pairs[i];
                                        if (pair.Key is null)
                                            continue;
                                        dictionary[pair.Key] = pair.Value;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore collections that cannot be restored.
                }
            }
        }

        if (snapshot.HasCustomState && instance is IXamlHotReloadStateProvider stateProvider)
        {
            try
            {
                stateProvider.RestoreHotReloadState(snapshot.CustomState);
            }
            catch
            {
                // Ignore custom restore errors to avoid breaking reload.
            }
        }
    }

    private sealed record PropertySnapshot(PropertyInfo Property, object? Value);

    private sealed record CollectionSnapshot(
        PropertyInfo Property,
        CollectionSnapshotKind Kind,
        object?[]? ListItems,
        KeyValuePair<object?, object?>[]? DictionaryEntries)
    {
        public static CollectionSnapshot ForList(PropertyInfo property, object?[] items)
            => new(property, CollectionSnapshotKind.List, items, null);

        public static CollectionSnapshot ForDictionary(PropertyInfo property, KeyValuePair<object?, object?>[] entries)
            => new(property, CollectionSnapshotKind.Dictionary, null, entries);
    }

    private sealed record InstanceSnapshot(
        List<PropertySnapshot>? Properties,
        List<CollectionSnapshot>? Collections,
        bool HasCustomState,
        object? CustomState);

    private enum CollectionSnapshotKind
    {
        List,
        Dictionary
    }
}
