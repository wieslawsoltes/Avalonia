using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using SkiaSharp;

namespace Avalonia.Skia
{
    internal class GlyphRunImpl : IGlyphRunImpl
    {
        private SharedGlyphRunData? _data;
        // Wrapper-local borrowed references preserve the existing lock-free warm lookup.
        // The shared owner, not this cache, disposes blobs after every lease is released.
        private readonly TwoLevelCache<TextOptions, SKTextBlob> _textBlobCache = new();

        public GlyphRunImpl(GlyphTypeface glyphTypeface, double fontRenderingEmSize,
            IReadOnlyList<GlyphInfo> glyphInfos, Point baselineOrigin)
        {
            if (glyphTypeface is null) throw new ArgumentNullException(nameof(glyphTypeface));
            if (glyphInfos is null) throw new ArgumentNullException(nameof(glyphInfos));
            _data = SharedGlyphRunCache.Acquire((SkiaTypeface)glyphTypeface.PlatformTypeface, fontRenderingEmSize, glyphInfos);
            FontRenderingEmSize = fontRenderingEmSize;
            BaselineOrigin = baselineOrigin;
            Bounds = _data.RelativeBounds.Translate(new Vector(baselineOrigin.X, baselineOrigin.Y));
        }

        public double FontRenderingEmSize { get; }
        public Point BaselineOrigin { get; }
        public Rect Bounds { get; }
        internal object? SharedDataIdentity => _data;

        public SKTextBlob GetTextBlob(TextOptions textOptions, RenderOptions renderOptions)
        {
            var data = _data ?? throw new ObjectDisposedException(nameof(GlyphRunImpl));
            if (textOptions.TextRenderingMode == TextRenderingMode.Unspecified)
            {
                textOptions = textOptions with
                {
                    TextRenderingMode = renderOptions.EdgeMode == EdgeMode.Aliased ? TextRenderingMode.Alias : TextRenderingMode.SubpixelAntialias
                };
            }
            return _textBlobCache.GetOrAdd(textOptions, data, static (options, owner) => owner.GetTextBlob(options));
        }

        public void Dispose()
        {
            var data = Interlocked.Exchange(ref _data, null);
            if (data is null) return;
            _textBlobCache.ClearAndDispose();
            data.Release();
        }

        public IReadOnlyList<float> GetIntersections(float lowerLimit, float upperLimit) =>
            GetTextBlob(default, default).GetIntercepts(lowerLimit, upperLimit);
    }
}
