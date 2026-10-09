using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Styling;

namespace Avalonia.Controls;

/// <summary>
/// Caches resolution locations, never deferred resource values. Epoch invalidation happens at
/// dictionary mutation, before host callbacks (including reentrant lookups) can run.
/// </summary>
internal sealed class ResourceLookupCache
{
    internal const int Capacity = 128;
    private static long s_epoch;
    [ThreadStatic] internal static int DeferredDepth;
    private readonly Dictionary<Key, Entry> _locations = new();

    internal static long Epoch => Volatile.Read(ref s_epoch);
    internal static void Invalidate()
    {
        Interlocked.Increment(ref s_epoch);
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.ResourceInvalidations);
#endif
    }
    internal static bool IsEligible(object key) => key is Type || key is string { Length: <= 256 };
    internal int Count => _locations.Count;

    internal bool TryGet(object key, ThemeVariant? theme, out ResourceDictionary? location)
    {
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.ResourceCacheProbes);
#endif
        var lookup = new Key(key, theme);
        var found = _locations.TryGetValue(lookup, out var entry) && entry.Epoch == Epoch;
        location = null;
        if (found && !entry.Missing && entry.Location?.TryGetTarget(out location) != true)
        {
            _locations.Remove(lookup);
            found = false;
        }
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(found ? Diagnostics.PerformanceCounter.ResourceCacheHits : Diagnostics.PerformanceCounter.ResourceCacheMisses);
#endif
        return found;
    }

    internal void Add(object key, ThemeVariant? theme, ResourceDictionary? location, long epoch)
    {
        if (epoch != Epoch) return;
        var lookup = new Key(key, theme);
        var existing = _locations.TryGetValue(lookup, out var previous);
        if (!existing && _locations.Count >= Capacity) _locations.Clear();
        var weak = previous.Location;
        if (location is not null)
        {
            if (weak is null) weak = new WeakReference<ResourceDictionary>(location);
            else weak.SetTarget(location);
        }
        else weak?.SetTarget(null!);
        // Stamp each entry separately. Reuse weak handles after invalidation instead of
        // allocating a new handle on every update/lookup pair in a mutable resource graph.
        _locations[lookup] = new Entry(epoch, weak, location is null);
    }

    private readonly record struct Entry(long Epoch, WeakReference<ResourceDictionary>? Location, bool Missing);

    private readonly struct Key(object value, ThemeVariant? theme) : IEquatable<Key>
    {
        private readonly object _value = value;
        private readonly ThemeVariant? _theme = theme;
        public bool Equals(Key other) => Equals(_value, other._value) && ReferenceEquals(_theme, other._theme);
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_value.GetHashCode(),
            _theme is null ? 0 : RuntimeHelpers.GetHashCode(_theme));
    }
}
