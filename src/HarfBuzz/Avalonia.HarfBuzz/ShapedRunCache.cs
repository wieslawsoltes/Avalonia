using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Media.TextFormatting;

namespace Avalonia.Harfbuzz;

/// <summary>
/// Bounded LRU of immutable glyph data for short, context-free strings. Returned buffers are
/// independent: line breaking, bidi processing and disposal must never modify a cached entry.
/// </summary>
internal sealed class ShapedRunCache
{
    internal const int MaxEntries = 256;
    internal const int MaxRetainedBytes = 256 * 1024;
    internal const int MaxTextLength = 128;
    internal const int MaxGlyphCount = 256;

    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _lru = new();
    private int _retainedBytes;

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal int RetainedBytes { get { lock (_gate) return _retainedBytes; } }

    internal readonly record struct Key(
        string Text, long Typeface, double Size, sbyte BidiLevel, string Culture,
        int CultureLcid, double TabWidth, double LetterSpacing);

    private sealed record Entry(Key Key, GlyphInfo[] Glyphs, int RetainedBytes);

    internal static bool TryCreateKey(ReadOnlyMemory<char> text, TextShaperOptions options,
        CultureInfo culture, long typeface, out Key key)
    {
        // Substrings need their original surrounding context. Mutable memory and feature lists
        // stay on HarfBuzz's existing path; a content-only key would not be sufficient for them.
        if (text.Length is > 0 and <= MaxTextLength &&
            (options.FontFeatures is null || options.FontFeatures.Count == 0) &&
            MemoryMarshal.TryGetString(text, out var value, out var start, out var length) &&
            start == 0 && length == value.Length &&
            !double.IsNaN(options.FontRenderingEmSize) && !double.IsNaN(options.LetterSpacing) &&
            !double.IsNaN(options.IncrementalTabWidth))
        {
            key = new Key(value, typeface, options.FontRenderingEmSize, options.BidiLevel,
                culture.Name, culture.LCID, options.IncrementalTabWidth, options.LetterSpacing);
            return true;
        }

        key = default;
        return false;
    }

    internal bool TryGet(Key key, ReadOnlyMemory<char> text, TextShaperOptions options,
        [NotNullWhen(true)] out ShapedBuffer? result)
    {
        GlyphInfo[] glyphs;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                result = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            glyphs = node.Value.Glyphs;
        }

        // The immutable array remains valid even if another thread evicts its entry.
        result = new ShapedBuffer(text, glyphs.Length, options.GlyphTypeface,
            options.FontRenderingEmSize, options.BidiLevel);
        for (var i = 0; i < glyphs.Length; ++i)
            result[i] = glyphs[i];
        return true;
    }

    internal void Add(Key key, ShapedBuffer buffer)
    {
        if (buffer.Length > MaxGlyphCount)
            return;

        lock (_gate)
        {
            // Another caller may have shaped this key while the cache lock was released.
            if (_entries.ContainsKey(key))
                return;

            var glyphs = new GlyphInfo[buffer.Length];
            for (var i = 0; i < glyphs.Length; ++i)
                glyphs[i] = buffer[i];

            // Conservative accounting for glyph/string payloads and per-entry managed overhead.
            // Both the byte allowance and the entry limit apply; no font object is retained.
            var bytes = 256 + (key.Text.Length + key.Culture.Length) * sizeof(char) + glyphs.Length * 40;
            if (bytes > MaxRetainedBytes)
                return;
            while (_entries.Count >= MaxEntries || _retainedBytes + bytes > MaxRetainedBytes)
            {
                var last = _lru.Last!;
                _entries.Remove(last.Value.Key);
                _retainedBytes -= last.Value.RetainedBytes;
                _lru.RemoveLast();
            }

            var node = _lru.AddFirst(new Entry(key, glyphs, bytes));
            _entries.Add(key, node);
            _retainedBytes += bytes;
        }
    }
}
