using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Media.TextFormatting;

namespace Avalonia.Harfbuzz;

/// <summary>
/// Bounded immutable glyph snapshots. A run's mutable returned buffer is never a cached value.
/// </summary>
internal sealed class ShapedRunCache
{
    internal const int MaxEntries = 256;
    internal const int MaxRetainedBytes = 256 * 1024;
    internal const int MaxTextLength = 128;
    internal const int MaxGlyphCount = 256;
    internal const int AdmissionSlots = 1024;
    internal const int AdmissionRetainedBytes = AdmissionSlots * sizeof(ulong);
    // Do not sample at a power-of-two stride: over-capacity scans often have those lengths.
    internal const int ProbeInterval = 67;
    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly ulong[] _recent = new ulong[AdmissionSlots];
    private int _retainedBytes;
    private int _missesWithoutBenefit;
    private int _probeCountdown;

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int RetainedBytes { get { lock (_gate) return _retainedBytes + AdmissionRetainedBytes; } }
    internal bool ShouldProbe() => Volatile.Read(ref _probeCountdown) <= 0 || Interlocked.Decrement(ref _probeCountdown) <= 0;

    internal readonly record struct Key(string Text, long Typeface, double Size, sbyte BidiLevel,
        string Culture, int CultureLcid, double TabWidth, double LetterSpacing);
    private sealed record Entry(Key Key, GlyphInfo[] Glyphs, DefaultTextLineMetricsCache Metrics, int RetainedBytes);

    internal static bool TryCreateKey(ReadOnlyMemory<char> text, TextShaperOptions options,
        CultureInfo culture, long typeface, out Key key)
    {
        if (text.Length is > 0 and <= MaxTextLength &&
            (options.FontFeatures is null || options.FontFeatures.Count == 0) &&
            MemoryMarshal.TryGetString(text, out var value, out var start, out var length) &&
            start == 0 && length == value.Length &&
            !double.IsNaN(options.FontRenderingEmSize) && !double.IsNaN(options.LetterSpacing) &&
            !double.IsNaN(options.IncrementalTabWidth))
        {
            key = new(value, typeface, options.FontRenderingEmSize, options.BidiLevel,
                culture.Name, culture.LCID, options.IncrementalTabWidth, options.LetterSpacing);
            return true;
        }
        key = default;
        return false;
    }

    internal bool TryGet(Key key, ReadOnlyMemory<char> text, TextShaperOptions options,
        [NotNullWhen(true)] out ShapedBuffer? result)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                if (_missesWithoutBenefit < ProbeInterval) ++_missesWithoutBenefit;
                if (_missesWithoutBenefit >= ProbeInterval) Volatile.Write(ref _probeCountdown, ProbeInterval);
                result = null;
                return false;
            }
            _missesWithoutBenefit = 0;
            Volatile.Write(ref _probeCountdown, 0);
            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value;
        }
#if AVALONIA_PERF_COUNTERS
        Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeHits);
#endif
        result = new ShapedBuffer(text, entry.Glyphs.Length, options.GlyphTypeface,
            options.FontRenderingEmSize, options.BidiLevel);
        for (var i = 0; i < entry.Glyphs.Length; ++i) result[i] = entry.Glyphs[i];
        result.AttachMetricsCache(entry.Metrics);
        return true;
    }

    internal void Add(Key key, ShapedBuffer buffer)
    {
        if (buffer.Length > MaxGlyphCount) return;
        lock (_gate)
        {
            var fingerprint = (ulong)(uint)key.GetHashCode() + 1;
            ref var recent = ref _recent[(int)(fingerprint & (AdmissionSlots - 1))];
            if (recent != fingerprint) { recent = fingerprint; return; }
            if (_entries.TryGetValue(key, out var existing))
            {
                buffer.AttachMetricsCache(existing.Value.Metrics);
                return;
            }
            var bytes = 512 + (key.Text.Length + key.Culture.Length) * sizeof(char) + buffer.Length * 40;
            if (bytes > MaxRetainedBytes - AdmissionRetainedBytes) return;
            var glyphs = new GlyphInfo[buffer.Length];
            for (var i = 0; i < glyphs.Length; ++i) glyphs[i] = buffer[i];
            while (_entries.Count >= MaxEntries || _retainedBytes + bytes > MaxRetainedBytes - AdmissionRetainedBytes)
            {
                var last = _lru.Last!;
                _entries.Remove(last.Value.Key);
                _retainedBytes -= last.Value.RetainedBytes;
                _lru.RemoveLast();
            }
            var metrics = new DefaultTextLineMetricsCache();
            var node = _lru.AddFirst(new Entry(key, glyphs, metrics, bytes));
            _entries.Add(key, node);
            _retainedBytes += bytes;
            _missesWithoutBenefit = 0;
            Volatile.Write(ref _probeCountdown, 0);
            buffer.AttachMetricsCache(metrics);
#if AVALONIA_PERF_COUNTERS
            Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeAdmissions);
#endif
        }
    }
}
