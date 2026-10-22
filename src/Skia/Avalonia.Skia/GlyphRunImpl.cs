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
        private TextOptions _primaryOptions;
        private SKTextBlob? _primaryBlob;
        private (TextOptions Options, SKTextBlob? Blob)[]? _secondaryBlobs;

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
            if (_primaryBlob is { } primary && _primaryOptions == textOptions) return primary;
            return GetTextBlobSlow(data, textOptions);
        }

        private SKTextBlob GetTextBlobSlow(SharedGlyphRunData data, TextOptions options)
        {
            if (_primaryBlob is null)
            {
                var blob = data.GetTextBlob(options);
                _primaryOptions = options;
                return _primaryBlob = blob;
            }
            var secondary = _secondaryBlobs;
            if (secondary is not null)
                for (var i = 0; i < secondary.Length; ++i)
                    if (secondary[i].Blob is { } existing && secondary[i].Options == options)
                        return existing;

            var result = data.GetTextBlob(options);
            secondary ??= _secondaryBlobs = new (TextOptions, SKTextBlob?)[3];
            // Preserve the previous cache's permanent first entry and three most recently
            // inserted alternatives. These are borrowed blobs: eviction never disposes them.
            secondary[2] = secondary[1];
            secondary[1] = secondary[0];
            secondary[0] = (options, result);
            return result;
        }

        public void Dispose()
        {
            var data = Interlocked.Exchange(ref _data, null);
            if (data is null) return;
            _primaryBlob = null;
            _secondaryBlobs = null;
            data.Release();
        }

        public IReadOnlyList<float> GetIntersections(float lowerLimit, float upperLimit) =>
            GetTextBlob(default, default).GetIntercepts(lowerLimit, upperLimit);
    }
}
