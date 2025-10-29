using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.XamlIl.Runtime;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Keeps track of runtime hot reload metadata and provides helpers for building and populating instances.
/// </summary>
public sealed class RuntimeHotReloadManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeHotReloadMetadata> _metadata;
    private readonly Dictionary<string, RuntimeHotReloadDelegates> _delegates;
    private readonly Dictionary<string, List<WeakReference>> _trackedInstances;
    private readonly Func<RuntimeHotReloadMetadata, RuntimeHotReloadDelegates> _delegateFactory;

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public RuntimeHotReloadManager()
        : this(metadata => RuntimeHotReloadDelegateProvider.CreateDelegates(metadata))
    {
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public RuntimeHotReloadManager(Func<RuntimeHotReloadMetadata, RuntimeHotReloadDelegates> delegateFactory)
    {
        _delegateFactory = delegateFactory ?? throw new ArgumentNullException(nameof(delegateFactory));
        _metadata = new Dictionary<string, RuntimeHotReloadMetadata>(StringComparer.Ordinal);
        _delegates = new Dictionary<string, RuntimeHotReloadDelegates>(StringComparer.Ordinal);
        _trackedInstances = new Dictionary<string, List<WeakReference>>(StringComparer.Ordinal);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public void Register(string xamlClassName, RuntimeHotReloadMetadata metadata)
    {
        if (xamlClassName is null)
            throw new ArgumentNullException(nameof(xamlClassName));
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        lock (_gate)
        {
            _metadata[xamlClassName] = metadata;
            _delegates.Remove(xamlClassName);
        }

        RefreshTrackedInstances(xamlClassName);
        UpdatePopulateOverride(xamlClassName, metadata);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public void RegisterRange(IEnumerable<KeyValuePair<string, RuntimeHotReloadMetadata>> manifest)
    {
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));

        foreach (var entry in manifest)
            Register(entry.Key, entry.Value);
    }

    /// <summary>
    /// Clears cached delegates for all registered entries. Metadata registrations are preserved.
    /// </summary>
    public void ClearDelegates()
    {
        lock (_gate)
        {
            _delegates.Clear();
        }
    }

    /// <summary>
    /// Clears cached delegates for the specified XAML classes, if present.
    /// </summary>
    public void InvalidateDelegates(IEnumerable<string> xamlClassNames)
    {
        if (xamlClassNames is null)
            throw new ArgumentNullException(nameof(xamlClassNames));

        lock (_gate)
        {
            foreach (var name in xamlClassNames)
                _delegates.Remove(name);
        }
    }

    public bool TryGetDelegates(string xamlClassName, [NotNullWhen(true)] out RuntimeHotReloadDelegates? delegates)
    {
        if (xamlClassName is null)
            throw new ArgumentNullException(nameof(xamlClassName));

        lock (_gate)
        {
            if (!_metadata.TryGetValue(xamlClassName, out var metadata))
            {
                delegates = null;
                return false;
            }

            delegates = GetOrCreateDelegates_NoLock(xamlClassName, metadata);
            return true;
        }
    }

    public RuntimeHotReloadDelegates GetDelegates(string xamlClassName)
    {
        if (TryGetDelegates(xamlClassName, out var delegates))
            return delegates;

        throw new KeyNotFoundException($"No runtime hot reload metadata registered for '{xamlClassName}'.");
    }

    public object Build(string xamlClassName, IServiceProvider? serviceProvider = null)
    {
        var delegates = GetDelegates(xamlClassName);
        var provider = EnsureServiceProvider(serviceProvider);
        var instance = delegates.Build(provider);
        TrackInstance(instance);
        return instance;
    }

    public void Populate(string xamlClassName, object instance, IServiceProvider? serviceProvider = null)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        var delegates = GetDelegates(xamlClassName);
        var provider = EnsureServiceProvider(serviceProvider);
        delegates.Populate(provider, instance);
        TrackInstance(instance);
        InvokeHotReloadHook(instance);
    }

    private RuntimeHotReloadDelegates GetOrCreateDelegates_NoLock(string xamlClassName, RuntimeHotReloadMetadata metadata)
    {
        if (!_delegates.TryGetValue(xamlClassName, out var delegates))
        {
            delegates = _delegateFactory(metadata);
            _delegates[xamlClassName] = delegates;
        }

        return delegates;
    }

    public void TrackInstance(object instance)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        var type = instance.GetType();
        var key = type.FullName ?? type.Name;

        lock (_gate)
        {
            if (!_trackedInstances.TryGetValue(key, out var list))
            {
                list = new List<WeakReference>();
                _trackedInstances[key] = list;
            }

            list.Add(new WeakReference(instance));
        }

        if (instance is IHotReloadableView hotReloadable)
            HotReloadViewRegistry.Register(hotReloadable);

        if (instance is ResourceDictionary dictionary)
            HotReloadResourceDictionaryRegistry.Register(dictionary);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private void RefreshTrackedInstances(string xamlClassName)
    {
        RuntimeHotReloadDelegates delegates;
        WeakReference[] tracked;

        lock (_gate)
        {
            if (!_metadata.TryGetValue(xamlClassName, out var metadata))
                return;

            delegates = GetOrCreateDelegates_NoLock(xamlClassName, metadata);
            var targetType = ResolveRuntimeType(metadata.PopulateTargetTypeName);
            HotReloadStaticHookInvoker.Invoke(targetType);
            if (targetType is not null)
                HotReloadResourceDictionaryRegistry.NotifyTypeReloaded(targetType);

            if (!_trackedInstances.TryGetValue(xamlClassName, out var list) || list.Count == 0)
                return;

            tracked = list.ToArray();

            // prune dead references inside the lock
            for (var i = list.Count - 1; i >= 0; --i)
            {
                if (!list[i].IsAlive)
                    list.RemoveAt(i);
            }
        }

        var provider = EnsureServiceProvider(null);
        foreach (var wr in tracked)
        {
            if (wr.Target is { } instance)
            {
                if (instance is IHotReloadableView view)
                    HotReloadViewRegistry.NotifyReloading(view);

                try
                {
                    delegates.Populate(provider, instance);
                    InvokeHotReloadHook(instance);
                    if (instance is IHotReloadableView reloaded)
                        HotReloadViewRegistry.NotifyReloaded(reloaded);
                    if (instance is ResourceDictionary dictionary)
                        HotReloadResourceDictionaryRegistry.NotifyReloaded(dictionary);
                }
                catch (Exception ex)
                {
                    HotReloadDiagnostics.ReportError(
                        "Failed to populate tracked instance for '{0}' during hot reload refresh.",
                        ex,
                        xamlClassName);
                }
            }
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private void UpdatePopulateOverride(string xamlClassName, RuntimeHotReloadMetadata metadata)
    {
        var targetType = ResolveRuntimeType(metadata.PopulateTargetTypeName);
        if (targetType is null)
            return;

        var field = targetType.GetField("!XamlIlPopulateOverride",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        if (field is null)
            return;

        Action<object> updater = instance => Populate(xamlClassName, instance);

        try
        {
            field.SetValue(null, updater);
        }
        catch
        {
            // Ignore failures; fallback behaviour remains available.
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    internal static Type? ResolveRuntimeType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var type = Type.GetType(typeName, throwOnError: false);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static void InvokeHotReloadHook(object instance)
    {
        if (instance is IXamlHotReloadable hook)
        {
            try
            {
                hook.OnHotReload();
            }
            catch
            {
                // Ignore user hook exceptions.
            }
        }
    }

    private static IServiceProvider EnsureServiceProvider(IServiceProvider? serviceProvider)
        => serviceProvider ?? XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null);

    internal IReadOnlyList<RuntimeHotReloadRegistration> CreateSnapshot()
    {
        lock (_gate)
        {
            if (_metadata.Count == 0)
                return Array.Empty<RuntimeHotReloadRegistration>();

            var registrations = new List<RuntimeHotReloadRegistration>(_metadata.Count);
            foreach (var pair in _metadata)
            {
                var trackedCount = 0;
                var liveCount = 0;
                if (_trackedInstances.TryGetValue(pair.Key, out var instances))
                {
                    trackedCount = instances.Count;
                    if (trackedCount > 0)
                    {
                        foreach (var reference in instances)
                        {
                            if (reference.IsAlive)
                                liveCount++;
                        }
                    }
                }

                registrations.Add(
                    new RuntimeHotReloadRegistration(
                        pair.Key,
                        pair.Value,
                        trackedCount,
                        liveCount));
            }

            return registrations.ToArray();
        }
    }

    internal IReadOnlyList<object> GetTrackedInstancesSnapshot(string typeName)
    {
        lock (_gate)
        {
            if (!_trackedInstances.TryGetValue(typeName, out var list) || list.Count == 0)
                return Array.Empty<object>();

            var result = new List<object>(list.Count);
            for (var i = list.Count - 1; i >= 0; --i)
            {
                var instance = list[i].Target;
                if (instance is null)
                    list.RemoveAt(i);
                else
                    result.Add(instance);
            }

            return result;
        }
    }
}
