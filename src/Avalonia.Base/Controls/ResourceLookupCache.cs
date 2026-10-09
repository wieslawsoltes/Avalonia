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
    private readonly Dictionary<Key, WeakReference<ResourceDictionary>?> _locations = new();
    private long _epoch = -1;

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
        if (_epoch != Epoch)
        {
            _locations.Clear();
            _epoch = Epoch;
        }
        var lookup = new Key(key, theme);
        var found = _locations.TryGetValue(lookup, out var weak);
        location = null;
        if (found && weak is not null && !weak.TryGetTarget(out location))
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
        if (_epoch != epoch) { _locations.Clear(); _epoch = epoch; }
        if (_locations.Count >= Capacity) _locations.Clear();
        // A lazily invalidated cache must not keep a removed dictionary/resource graph alive.
        _locations[new Key(key, theme)] = location is null ? null : new WeakReference<ResourceDictionary>(location);
    }

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
