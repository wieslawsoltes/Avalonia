using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Platform;
using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;
using GlyphInfo = HarfBuzzSharp.GlyphInfo;

namespace Avalonia.Harfbuzz
{
    public class HarfBuzzTextShaper : ITextShaperImpl
    {
        [ThreadStatic]
        private static Buffer? s_buffer;

        private static readonly ConcurrentDictionary<int, Language> s_cachedLanguage = new();
        private readonly ShapedRunCache _shapedRunCache = new();

        internal ShapedRunCache ShapedRunCache => _shapedRunCache;

        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            var textSpan = text.Span;

            if (text.Length == 0)
                return new ShapedBuffer(text, 0, options.GlyphTypeface, options.FontRenderingEmSize, options.BidiLevel);

            var glyphTypeface = options.GlyphTypeface;

            if (glyphTypeface.TextShaperTypeface is not HarfBuzzTypeface harfBuzzTypeface)
            {
                throw new NotSupportedException("The provided GlyphTypeface is not supported by this text shaper.");
            }

            if (harfBuzzTypeface.IsDisposed)
                throw new ObjectDisposedException(nameof(GlyphTypeface));

            var usedCulture = options.Culture ?? CultureInfo.CurrentCulture;
            // Resolve backing memory once, retaining surrounding characters for native shaping.
            // Only a complete immutable string can use the cache. Slices, mutable memory,
            // long runs and explicit features do not advance the adaptive policy at all.
            var containingMemory = GetContainingMemory(text, out var start, out var length, out var completeString);
            var eligible = completeString is { Length: > 0 and <= ShapedRunCache.MaxTextLength } &&
                ShapedRunCache.CanCacheOptions(options);
            var probe = eligible && _shapedRunCache.ShouldProbe();
#if AVALONIA_PERF_COUNTERS
            Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeRequests);
            if (eligible && !probe)
                Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeProbeSkips);
#endif
            if (probe)
                return ShapeTextWithCache(text, containingMemory, start, length, completeString!,
                    in options, harfBuzzTypeface, usedCulture);

            return ShapeTextCore(text, containingMemory, start, length, in options, harfBuzzTypeface, usedCulture);
        }

        private ShapedBuffer ShapeTextWithCache(ReadOnlyMemory<char> text, ReadOnlyMemory<char> containingMemory,
            int start, int length, string completeString, in TextShaperOptions options,
            HarfBuzzTypeface harfBuzzTypeface, CultureInfo usedCulture)
        {
            // The relatively large cache key and cache-only locals are not part of the
            // uncacheable native path's stack frame. Neither path changes admission policy.
            var cacheKey = ShapedRunCache.CreateKey(completeString, options, usedCulture, harfBuzzTypeface.CacheId);
            if (_shapedRunCache.TryGet(cacheKey, text, options, out var cached))
                return cached;
            var shaped = ShapeTextCore(text, containingMemory, start, length, in options, harfBuzzTypeface, usedCulture);
            _shapedRunCache.Add(cacheKey, shaped);
            return shaped;
        }

        private static ShapedBuffer ShapeTextCore(ReadOnlyMemory<char> text, ReadOnlyMemory<char> containingMemory,
            int start, int length, in TextShaperOptions options, HarfBuzzTypeface harfBuzzTypeface, CultureInfo usedCulture)
        {
            var glyphTypeface = options.GlyphTypeface;
            var fontRenderingEmSize = options.FontRenderingEmSize;
            var bidiLevel = options.BidiLevel;
            var buffer = s_buffer ??= new Buffer();
            buffer.Reset();

            // HarfBuzz needs the surrounding characters to correctly shape the text.
            var containingText = containingMemory.Span;
            buffer.AddUtf16(containingText, start, length);
            MergeBreakPair(buffer);
            buffer.GuessSegmentProperties();
            buffer.Direction = (bidiLevel & 1) == 0 ? Direction.LeftToRight : Direction.RightToLeft;
            buffer.Language = s_cachedLanguage.GetOrAdd(
                usedCulture.LCID,
                static (_, culture) => new Language(culture),
                usedCulture);

            var font = harfBuzzTypeface.HBFont;
            font.Shape(buffer, GetFeatures(options));

            // HarfBuzz produces glyphs in visual order for RTL by default: the first glyph
            // in the buffer is the leftmost visual glyph (highest cluster value). LTR output
            // already has clusters in ascending (logical = visual) order. We preserve that
            // order in the ShapedBuffer so downstream rendering/hit-testing can operate on
            // visual-order glyphs without an extra bidi reversal step.
            font.GetScale(out var scaleX, out _);
            var textScale = fontRenderingEmSize / scaleX;
            var bufferLength = buffer.Length;
            var shapedBuffer = new ShapedBuffer(text, bufferLength, glyphTypeface, fontRenderingEmSize, bidiLevel);
            var glyphInfos = buffer.GetGlyphInfoSpan();
            var glyphPositions = buffer.GetGlyphPositionSpan();

            for (var i = 0; i < bufferLength; i++)
            {
                var sourceInfo = glyphInfos[i];
                var glyphIndex = (ushort)sourceInfo.Codepoint;
                var originalCluster = (int)sourceInfo.Cluster;
                var glyphCluster = originalCluster - start;
                // Read one position once. Keep exactly the original arithmetic order while
                // avoiding two helper calls and repeated span/index access for every glyph.
                var position = glyphPositions[i];
                var glyphAdvance = position.XAdvance * textScale + options.LetterSpacing;
                var glyphOffset = new Vector(position.XOffset * textScale, -position.YOffset * textScale);

                if (originalCluster < containingText.Length && containingText[originalCluster] == '\t')
                {
                    glyphIndex = glyphTypeface.CharacterToGlyphMap[' '];
                    if (options.IncrementalTabWidth > 0)
                        glyphAdvance = options.IncrementalTabWidth;
                    else
                    {
                        glyphTypeface.TryGetHorizontalGlyphAdvance(glyphIndex, out var advance);
                        glyphAdvance = 4 * advance * textScale;
                    }
                }
                // This buffer has not escaped; no alias or derived metric can exist yet.
                shapedBuffer.InitializeGlyph(i, new Media.TextFormatting.GlyphInfo(glyphIndex, glyphCluster, glyphAdvance, glyphOffset));
            }

            return shapedBuffer;
        }

        public ITextShaperTypeface CreateTypeface(GlyphTypeface glyphTypeface) => new HarfBuzzTypeface(glyphTypeface);

        private static void MergeBreakPair(Buffer buffer)
        {
            var length = buffer.Length;
            if (length == 0) return;
            var glyphInfos = buffer.GetGlyphInfoSpan();
            var second = glyphInfos[length - 1];
            if (!new Codepoint(second.Codepoint).IsBreakChar) return;
            if (length > 1 && glyphInfos[length - 2].Codepoint == '\r' && second.Codepoint == '\n')
            {
                var first = glyphInfos[length - 2];
                first.Codepoint = '\u200C';
                second.Codepoint = '\u200C';
                second.Cluster = first.Cluster;
                unsafe
                {
                    fixed (GlyphInfo* p = &glyphInfos[length - 2]) { *p = first; }
                    fixed (GlyphInfo* p = &glyphInfos[length - 1]) { *p = second; }
                }
            }
            else
            {
                second.Codepoint = '\u200C';
                unsafe { fixed (GlyphInfo* p = &glyphInfos[length - 1]) { *p = second; } }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlyMemory<char> GetContainingMemory(ReadOnlyMemory<char> memory,
            out int start, out int length, out string? completeString)
        {
            completeString = null;
            if (MemoryMarshal.TryGetString(memory, out var containingString, out start, out length))
            {
                if (start == 0 && length == containingString.Length) completeString = containingString;
                return containingString.AsMemory();
            }
            if (MemoryMarshal.TryGetArray(memory, out var segment))
            {
                start = segment.Offset;
                length = segment.Count;
                return segment.Array.AsMemory();
            }
            if (MemoryMarshal.TryGetMemoryManager(memory, out MemoryManager<char>? memoryManager, out start, out length))
                return memoryManager.Memory;
            throw new InvalidOperationException("Memory not backed by string, array or manager");
        }

        private static Feature[] GetFeatures(TextShaperOptions options)
        {
            if (options.FontFeatures is null || options.FontFeatures.Count == 0) return Array.Empty<Feature>();
            var features = new Feature[options.FontFeatures.Count];
            for (var i = 0; i < options.FontFeatures.Count; i++)
            {
                var fontFeature = options.FontFeatures[i];
                features[i] = new Feature(Tag.Parse(fontFeature.Tag), (uint)fontFeature.Value,
                    (uint)fontFeature.Start, (uint)fontFeature.End);
            }
            return features;
        }
    }
}
