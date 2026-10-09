using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Styling;

namespace Avalonia.Controls;

internal enum ResourceLookupChange { None, Inserted, Removed }

/// <summary>Caches weak resolution locations, never deferred resource values.</summary>
internal sealed class ResourceLookupCache
{
    internal const int Capacity = 128;
    private static long s_epoch;
    private static readonly Type s_runtimeType = typeof(object).GetType();
    private static readonly object s_changeGate = new();
    private static readonly WeakReference<ResourceDictionary> s_changedOwner = new(null!);
    private static string? s_changedKey;
    private static ResourceLookupChange s_change;
    private static long s_changeEpoch = long.MinValue;
    [ThreadStatic] internal static int DeferredDepth;
    private readonly Dictionary<Key, Entry> _locations = new();
    private string? _primaryKey;
    private ThemeVariant? _primaryTheme;
    private Entry _primaryEntry;
    private bool _primaryDirty;

    internal static long Epoch => Volatile.Read(ref s_epoch);
    internal static void Invalidate()
    {
        Interlocked.Increment(ref s_epoch);
        Volatile.Write(ref s_changeEpoch, long.MinValue);
        CountInvalidation();
    }

    internal static void Invalidate(ResourceDictionary owner, string key, ResourceLookupChange change)
    {
        // Publish a consistent, bounded, non-owning description even when independent
        // resource graphs are changed on different threads. No user callbacks run here.
        lock (s_changeGate)
        {
            var epoch = Interlocked.Increment(ref s_epoch);
            s_changedOwner.SetTarget(owner);
            s_changedKey = key;
            s_change = change;
            Volatile.Write(ref s_changeEpoch, epoch);
        }
        CountInvalidation();
    }

    [System.Diagnostics.Conditional("AVALONIA_PERF_COUNTERS")]
    private static void CountInvalidation()
    {
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.ResourceInvalidations);
#endif
    }

    internal static bool IsStableStoredKey(object key) =>
        key is string || key is Type && key.GetType() == s_runtimeType;

    internal static bool IsEligible(object key) =>
        key is string { Length: <= 256 } || key is Type && key.GetType() == s_runtimeType;

    internal int Count => _locations.Count;

    internal bool TryGet(object key, ThemeVariant? theme, out ResourceDictionary? location)
    {
#if AVALONIA_PERF_COUNTERS
        Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.ResourceCacheProbes);
#endif
        var primary = IsPrimary(key, theme);
        Entry entry;
        var exists = primary;
        if (primary) entry = _primaryEntry;
        else exists = _locations.TryGetValue(new Key(key, theme), out entry);
        var epoch = Epoch;
        var found = exists && entry.Epoch == epoch;
        if (exists && !found && TryAdvance(key, entry, epoch, out var advanced))
        {
            entry = advanced;
            if (primary) { _primaryEntry = entry; _primaryDirty = true; }
            else _locations[new Key(key, theme)] = entry;
            found = true;
        }
        location = null;
        if (found && !entry.Missing && entry.Location?.TryGetTarget(out location) != true)
        {
            _locations.Remove(new Key(key, theme));
            if (primary) ClearPrimary();
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
            _primaryEntry = CreateEntry(key, _primaryEntry, location, epoch);
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
            ClearPrimary();
        }
        var entry = CreateEntry(key, previous, location, epoch);
        _locations[lookup] = entry;
        if (key is string text)
        {
            _primaryKey = text;
            _primaryTheme = theme;
            _primaryEntry = entry;
        }
    }

    private static bool TryAdvance(object key, Entry previous, long epoch, out Entry result)
    {
        result = default;
        if (!TryGetSingleChange(key, previous, epoch, out var change)) return false;
        if (previous.Missing && change == ResourceLookupChange.Inserted)
        {
            // The preceding full search proved absence, and the retained weak candidate
            // is still reachable: only its entry, not the graph, changed in this epoch.
            result = new Entry(epoch, previous.Location, false, true);
            return true;
        }
        if (previous.SoleCandidate && change == ResourceLookupChange.Removed)
        {
            // Only the candidate inserted into a previously empty search could have won.
            // No lower-priority fallback can have appeared without another invalidation.
            result = new Entry(epoch, previous.Location, true, false);
            return true;
        }
        return false;
    }

    private static bool TryGetSingleChange(object key, Entry previous, long epoch, out ResourceLookupChange change)
    {
        change = ResourceLookupChange.None;
        if (unchecked(epoch - previous.Epoch) != 1 || key is not string text ||
            previous.Location?.TryGetTarget(out var candidate) != true)
            return false;
        lock (s_changeGate)
        {
            if (Volatile.Read(ref s_changeEpoch) != epoch || Epoch != epoch ||
                !string.Equals(text, s_changedKey, StringComparison.Ordinal) ||
                !s_changedOwner.TryGetTarget(out var changed) || !ReferenceEquals(candidate, changed))
                return false;
            change = s_change;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPrimary(object key, ThemeVariant? theme) =>
        _primaryKey is not null && key is string text && ReferenceEquals(theme, _primaryTheme) &&
        string.Equals(text, _primaryKey, StringComparison.Ordinal);

    private void ClearPrimary()
    {
        _primaryKey = null;
        _primaryTheme = null;
        _primaryEntry = default;
        _primaryDirty = false;
    }

    private static Entry CreateEntry(object key, Entry previous, ResourceDictionary? location, long epoch)
    {
        var weak = previous.Location;
        if (location is not null)
        {
            if (weak is null) weak = new WeakReference<ResourceDictionary>(location);
            else weak.SetTarget(location);
        }
        else
        {
            // Retain a *weak* candidate only when the single intervening change removed
            // its key and this completed traversal found no fallback. Graph changes, extra
            // epochs and unrelated changes discard it, preventing detached-owner revival.
            var retainCandidate = !previous.Missing && TryGetSingleChange(key, previous, epoch, out var change) &&
                change == ResourceLookupChange.Removed;
            if (!retainCandidate) weak?.SetTarget(null!);
        }
        return new Entry(epoch, weak, location is null, false);
    }

    private readonly record struct Entry(long Epoch, WeakReference<ResourceDictionary>? Location, bool Missing, bool SoleCandidate);

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
