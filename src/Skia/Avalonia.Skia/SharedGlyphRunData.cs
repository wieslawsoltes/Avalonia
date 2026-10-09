using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SkiaSharp;

namespace Avalonia.Skia;

/// <summary>
/// Immutable run geometry and retained blobs. Each live GlyphRunImpl and each cache entry owns
/// one reference, so eviction or disposal of another run cannot invalidate a borrowed blob.
/// </summary>
internal sealed class SharedGlyphRunData
{
    private readonly SkiaTypeface _typeface;
    private readonly double _size;
    private readonly ushort[] _indices;
    private readonly SKPoint[] _positions;
    private readonly object _blobGate = new();
    private SKTextBlob?[]? _blobs;
    private int _references = 1;

    internal SharedGlyphRunData(SkiaTypeface typeface, double size, IReadOnlyList<GlyphInfo> glyphs)
    {
        _typeface = typeface;
        _size = size;
        var count = glyphs.Count;
        _indices = new ushort[count];
        _positions = new SKPoint[count];
        if (glyphs is ShapedBuffer buffer)
            buffer.GlyphIndices.CopyTo(_indices);
        else
            for (var i = 0; i < count; ++i)
                _indices[i] = glyphs[i].GlyphIndex;

        var options = default(TextOptions) with
        {
            TextRenderingMode = TextRenderingMode.SubpixelAntialias,
            TextHintingMode = TextHintingMode.Strong,
            BaselinePixelAlignment = BaselinePixelAlignment.Unaligned
        };
        using var font = CreateFont(options);
        var bounds = ArrayPool<SKRect>.Shared.Rent(count);
        try
        {
            font.GetGlyphWidths(_indices, null, bounds.AsSpan(0, count));
            var currentX = 0.0;
            var runBounds = new Rect();
            for (var i = 0; i < count; ++i)
            {
                var glyph = glyphs[i];
                var b = bounds[i];
                _positions[i] = new SKPoint((float)(currentX + glyph.GlyphOffset.X), (float)glyph.GlyphOffset.Y);
                // Keep the existing native bounds algorithm; this change only reuses its result.
                runBounds = runBounds.Union(new Rect(currentX + b.Left, b.Top, b.Width, b.Height));
                currentX += glyph.GlyphAdvance;
            }
            RelativeBounds = runBounds;
        }
        finally { ArrayPool<SKRect>.Shared.Return(bounds); }
    }

    internal Rect RelativeBounds { get; }
    internal void AddReference() => Interlocked.Increment(ref _references);
    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0)
            return;
        lock (_blobGate)
        {
            if (_blobs is not null)
                foreach (var blob in _blobs)
                    blob?.Dispose();
            _blobs = null;
        }
    }

    internal SKTextBlob GetTextBlob(TextOptions options)
    {
        // There are only 3 edging x 3 hinting x 2 baseline-snap effective font states.
        // Do not evict a blob while the data is leased: another wrapper may be drawing it.
        var edging = options.TextRenderingMode switch
        {
            TextRenderingMode.Alias => 0,
            TextRenderingMode.Antialias => 1,
            _ => 2
        };
        var hinting = options.TextHintingMode switch
        {
            TextHintingMode.None => 0,
            TextHintingMode.Light => 1,
            _ => 2
        };
        var snap = options.BaselinePixelAlignment == BaselinePixelAlignment.Unaligned ? 0 : 1;
        var index = edging * 6 + hinting * 2 + snap;
        lock (_blobGate)
        {
            var blobs = _blobs ??= new SKTextBlob?[18];
            return blobs[index] ??= CreateTextBlob(options);
        }
    }

    private SKTextBlob CreateTextBlob(TextOptions options)
    {
#if AVALONIA_PERF_COUNTERS
        Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeTextBlobsCreated);
#endif
        using var font = CreateFont(options);
        var builder = SKTextBlobBuilderCache.Shared.Get();
        try
        {
            var run = builder.AllocatePositionedRun(font, _indices.Length);
            run.SetPositions(_positions);
            run.SetGlyphs(_indices);
            return builder.Build()!;
        }
        finally { SKTextBlobBuilderCache.Shared.Return(builder); }
    }

    private SKFont CreateFont(TextOptions options)
    {
        var edging = options.TextRenderingMode switch
        {
            TextRenderingMode.Alias => SKFontEdging.Alias,
            TextRenderingMode.Antialias => SKFontEdging.Antialias,
            _ => SKFontEdging.SubpixelAntialias
        };
        var font = _typeface.CreateSKFont((float)_size);
        font.ForceAutoHinting = options.TextHintingMode == TextHintingMode.Light;
        font.Hinting = options.TextHintingMode switch
        {
            TextHintingMode.None => SKFontHinting.None,
            TextHintingMode.Light => SKFontHinting.Slight,
            _ => SKFontHinting.Full
        };
        font.Subpixel = edging != SKFontEdging.Alias;
        font.Edging = edging;
        font.BaselineSnap = options.BaselinePixelAlignment != BaselinePixelAlignment.Unaligned;
        return font;
    }
}

internal static class SharedGlyphRunCache
{
    internal const int Capacity = 128;
    internal const int MaxGlyphs = 128;
    private const int ByteAllowance = 2 * 1024 * 1024;
    private static readonly object s_gate = new();
    private static readonly Dictionary<Key, LinkedListNode<Entry>> s_entries = new();
    private static readonly LinkedList<Entry> s_lru = new();
    private static readonly ulong[] s_recent = new ulong[1024];
    private static int s_bytes;

    internal static int Count { get { lock (s_gate) return s_entries.Count; } }

    internal static SharedGlyphRunData Acquire(SkiaTypeface face, double size, IReadOnlyList<GlyphInfo> glyphs)
    {
        #if AVALONIA_PERF_COUNTERS
        Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeGlyphRequests);
#endif
        if (face.IsDisposed)
            throw new ObjectDisposedException(nameof(GlyphTypeface));
        var eligible = glyphs.Count is > 0 and <= MaxGlyphs &&
            (glyphs is ShapedBuffer || glyphs is GlyphInfo[]) && !double.IsNaN(size) && !double.IsInfinity(size);
        var key = default(Key);
        var admit = false;
        if (eligible)
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(face));
            hash.Add(size);
            for (var i = 0; i < glyphs.Count; ++i)
                hash.Add(glyphs[i]);
            key = new Key(face, size, glyphs.Count, hash.ToHashCode());
            lock (s_gate)
            {
                if (s_entries.TryGetValue(key, out var found) && Matches(found.Value.Glyphs, glyphs))
                {
                    s_lru.Remove(found);
                    s_lru.AddFirst(found);
                    found.Value.Data.AddReference();
#if AVALONIA_PERF_COUNTERS
                    Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeGlyphHits);
#endif
                    return found.Value.Data;
                }
                var fingerprint = (ulong)(uint)key.Hash + 1;
                ref var slot = ref s_recent[(int)(fingerprint & 1023)];
                admit = slot == fingerprint;
                slot = fingerprint;
            }
        }

        var data = new SharedGlyphRunData(face, size, glyphs);
        if (!admit)
            return data;
        lock (s_gate)
        {
            if (face.IsDisposed)
                return data;
            if (s_entries.TryGetValue(key, out var existing))
            {
                if (Matches(existing.Value.Glyphs, glyphs))
                {
                    existing.Value.Data.AddReference();
                    data.Release();
                    return existing.Value.Data;
                }
                Remove(existing);
            }
            // Reserve for geometry, key/snapshot overhead and all effective blob states.
            // This is a conservative allowance, not a measurement of Skia's internal heap.
            var bytes = 4096 + glyphs.Count * 256;
            while (s_entries.Count >= Capacity || s_bytes + bytes > ByteAllowance)
                Remove(s_lru.Last!);
            var snapshot = new GlyphInfo[glyphs.Count];
            for (var i = 0; i < snapshot.Length; ++i) snapshot[i] = glyphs[i];
            data.AddReference();
            var node = s_lru.AddFirst(new Entry(key, snapshot, data, bytes));
            s_entries.Add(key, node);
            s_bytes += bytes;
        }
        return data;
    }

    internal static void RemoveTypeface(SkiaTypeface face)
    {
        lock (s_gate)
        {
            var node = s_lru.First;
            while (node is not null)
            {
                var next = node.Next;
                if (ReferenceEquals(node.Value.Key.Face, face)) Remove(node);
                node = next;
            }
        }
    }

    private static bool Matches(GlyphInfo[] stored, IReadOnlyList<GlyphInfo> current)
    {
        for (var i = 0; i < stored.Length; ++i)
            if (!stored[i].Equals(current[i])) return false;
        return true;
    }

    private static void Remove(LinkedListNode<Entry> node)
    {
        s_entries.Remove(node.Value.Key);
        s_lru.Remove(node);
        s_bytes -= node.Value.Bytes;
        node.Value.Data.Release();
    }

    private readonly record struct Key(SkiaTypeface Face, double Size, int Count, int Hash);
    private sealed record Entry(Key Key, GlyphInfo[] Glyphs, SharedGlyphRunData Data, int Bytes);
}
