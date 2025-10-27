using System;
using System.Collections.Generic;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Provides a snapshot of the current runtime hot reload state.
/// </summary>
public sealed class RuntimeHotReloadStatus
{
    public RuntimeHotReloadStatus(
        IReadOnlyList<string> manifestPaths,
        IReadOnlyList<string> watcherPaths,
        IReadOnlyList<RuntimeHotReloadRegistration> registrations)
    {
        ManifestPaths = manifestPaths ?? Array.Empty<string>();
        WatcherPaths = watcherPaths ?? Array.Empty<string>();
        Registrations = registrations ?? Array.Empty<RuntimeHotReloadRegistration>();
    }

    public IReadOnlyList<string> ManifestPaths { get; }

    public IReadOnlyList<string> WatcherPaths { get; }

    public IReadOnlyList<RuntimeHotReloadRegistration> Registrations { get; }
}

/// <summary>
/// Describes an individual runtime hot reload registration.
/// </summary>
public sealed class RuntimeHotReloadRegistration
{
    public RuntimeHotReloadRegistration(
        string xamlClassName,
        RuntimeHotReloadMetadata metadata,
        int trackedInstanceCount,
        int liveInstanceCount)
    {
        XamlClassName = xamlClassName ?? throw new ArgumentNullException(nameof(xamlClassName));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        TrackedInstanceCount = trackedInstanceCount;
        LiveInstanceCount = liveInstanceCount;
    }

    public string XamlClassName { get; }

    public RuntimeHotReloadMetadata Metadata { get; }

    public int TrackedInstanceCount { get; }

    public int LiveInstanceCount { get; }
}
