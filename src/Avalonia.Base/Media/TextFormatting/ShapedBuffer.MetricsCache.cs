using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Utilities;

namespace Avalonia.Media.TextFormatting;

public sealed partial class ShapedBuffer
{
    // Metadata lives on an optional stateful glyph reference, not every ShapedBuffer.
    // Ordinary cold, sliced and feature-bearing runs retain their original object size.
    private readonly record struct MetricsState(DefaultTextLineMetricsCache Cache, int Generation);

    internal DefaultTextLineMetricsCache? SharedMetrics
    {
        get
        {
            if (!_disposed && _glyphRef is IRefWithState<MetricsState> reference)
            {
                var state = reference.State;
                if (_glyphRef.Item.Generation == state.Generation) return state.Cache;
            }
            return null;
        }
    }

    internal void AttachMetricsCache(DefaultTextLineMetricsCache cache)
    {
        if (_disposed || _glyphRef is null) return;
        var previous = _glyphRef;
        _glyphRef = RefCountable.CloneWithState(previous, new MetricsState(cache, previous.Item.Generation));
        previous.Dispose();
    }

    /// <summary>
    /// Build an independent cache-hit buffer and its optional metadata in the original
    /// reference allocation. The snapshot is copied; no mutable array is shared with it.
    /// </summary>
    internal ShapedBuffer(ReadOnlyMemory<char> text, ReadOnlySpan<GlyphInfo> snapshot,
        GlyphTypeface glyphTypeface, double fontRenderingEmSize, sbyte bidiLevel,
        DefaultTextLineMetricsCache metrics)
    {
        Text = text;
        GlyphTypeface = glyphTypeface;
        FontRenderingEmSize = fontRenderingEmSize;
        BidiLevel = bidiLevel;
        _glyphRef = RefCountable.Create(new PooledArray<GlyphInfo>(snapshot.Length), new MetricsState(metrics, 0));
        _glyphInfos = new ArraySlice<GlyphInfo>(_glyphRef.Item.Array, 0, snapshot.Length);
        _glyphIndicesRef = RefCountable.Create(new PooledArray<ushort>(snapshot.Length));
        _glyphIndices = new ArraySlice<ushort>(_glyphIndicesRef.Item.Array, 0, snapshot.Length);
        for (var i = 0; i < snapshot.Length; ++i) InitializeGlyph(i, snapshot[i]);
    }

    /// <summary>
    /// Populate a newly allocated, unpublished buffer. There are no aliases or derived
    /// caches to invalidate yet. All writes after publication must use the public indexer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void InitializeGlyph(int index, GlyphInfo glyph)
    {
        Debug.Assert(!_disposed && _glyphRef is not null && _glyphRef.RefCount == 1 &&
            _glyphRef.Item.Generation == 0 && _clusterPrefix is null);
        _glyphInfos[index] = glyph;
        _glyphIndices[index] = glyph.GlyphIndex;
    }
}
