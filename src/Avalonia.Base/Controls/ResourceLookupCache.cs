using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Styling;

namespace Avalonia.Controls;

/// <summary>
/// Caches resolution locations, never deferred resource values. Location-invalidating changes
/// advance the epoch before host callbacks (including reentrant lookups) can run.
/// </summary>
internal sealed class ResourceLookupCache
{
    internal const int Capacity = 128;
    private static long s_epoch;
    [ThreadStatic] internal static int DeferredDepth;
    private readonly Dictionary<Key, Entry> _locations = new();
    // Mirror one string entry inline. Repeated invalidation/revalidation of that key must
    // not hash and rewrite the secondary dictionary on every mutation. Its weak handle
    // is shared with the dictionary, and a dirty epoch/missing state is flushed on change.
    private string? _primaryKey;
    private ThemeVariant? _primaryTheme;
    private Entry _primaryEntry;
    private bool _primaryDirty;

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
        var primary = IsPrimary(key, theme);
        Entry entry;
        bool found;
        if (primary)
        {
            entry = _primaryEntry;
            found = entry.Epoch == Epoch;
        }
        else
            found = _locations.TryGetValue(new Key(key, theme), out entry) && entry.Epoch == Epoch;
        location = null;
        if (found && !entry.Missing && entry.Location?.TryGetTarget(out location) != true)
        {
            _locations.Remove(new Key(key, theme));
            if (primary)
            {
                _primaryKey = null;
                _primaryEntry = default;
                _primaryTheme = null;
                _primaryDirty = false;
            }
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
        location?.MarkResourceLookupDependency();
        if (IsPrimary(key, theme))
        {
            _primaryEntry = CreateEntry(_primaryEntry, location, epoch);
            _primaryDirty = true;
            return;
        }
        if (_primaryDirty)
        {
            _locations[new Key(_primaryKey!, _primaryTheme)] = _primaryEntry;
            _primaryDirty = false;
        }
        var lookup = new Key(key, theme);
        var existing = _locations.TryGetValue(lookup, out var previous);
        if (!existing && _locations.Count >= Capacity)
        {
            _locations.Clear();
            _primaryKey = null;
            _primaryTheme = null;
            _primaryEntry = default;
        }
        var entry = CreateEntry(previous, location, epoch);
        _locations[lookup] = entry;
        // The inline equality check is restricted to immutable strings: no user Type
        // subclass equality/hash callbacks are skipped by this extra fast path.
        if (key is string text)
        {
            _primaryKey = text;
            _primaryTheme = theme;
            _primaryEntry = entry;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPrimary(object key, ThemeVariant? theme) =>
        _primaryKey is not null && key is string text && ReferenceEquals(theme, _primaryTheme) &&
        string.Equals(text, _primaryKey, StringComparison.Ordinal);

    private static Entry CreateEntry(Entry previous, ResourceDictionary? location, long epoch)
    {
        var weak = previous.Location;
        if (location is not null)
        {
            if (weak is null) weak = new WeakReference<ResourceDictionary>(location);
            else weak.SetTarget(location);
        }
        else weak?.SetTarget(null!);
        return new Entry(epoch, weak, location is null);
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
