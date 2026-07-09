using System;
using System.Collections.Generic;
using Avalonia.Platform;
using ProGPU.Backend;
using ProGPU.Text;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Avalonia.ProGpu
{
    internal readonly struct BitmapGlyphMetrics
    {
        public BitmapGlyphMetrics(
            ushort pixelsPerEm,
            ushort pixelsPerInch,
            short originOffsetX,
            short originOffsetY,
            int pixelWidth,
            int pixelHeight)
        {
            PixelsPerEm = pixelsPerEm;
            PixelsPerInch = pixelsPerInch;
            OriginOffsetX = originOffsetX;
            OriginOffsetY = originOffsetY;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }

        public ushort PixelsPerEm { get; }
        public ushort PixelsPerInch { get; }
        public short OriginOffsetX { get; }
        public short OriginOffsetY { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }

        public Rect GetBounds(Point baselineOrigin, double emSize)
        {
            var scale = emSize / PixelsPerEm;
            return new Rect(
                baselineOrigin.X - OriginOffsetX * scale,
                baselineOrigin.Y - (PixelHeight - OriginOffsetY) * scale,
                PixelWidth * scale,
                PixelHeight * scale);
        }
    }

    internal readonly struct CachedBitmapGlyph
    {
        public CachedBitmapGlyph(GpuTexture texture, BitmapGlyphMetrics metrics, Rect sourceRect)
        {
            Texture = texture;
            Metrics = metrics;
            SourceRect = sourceRect;
        }

        public GpuTexture Texture { get; }
        public BitmapGlyphMetrics Metrics { get; }
        public Rect SourceRect { get; }
    }

    internal static class BitmapGlyphCache
    {
        private readonly record struct GlyphKey(TtfFont Font, ushort GlyphIndex, ushort PixelsPerEm);

        private sealed class DecodedGlyph
        {
            public DecodedGlyph(BitmapGlyphMetrics metrics, Rgba32[] pixels)
            {
                Metrics = metrics;
                Pixels = pixels;
            }

            public BitmapGlyphMetrics Metrics { get; }
            public Rgba32[] Pixels { get; }
        }

        private sealed class AtlasPage
        {
            private const int Padding = 1;
            private int _nextX;
            private int _nextY;
            private int _rowHeight;

            public AtlasPage(GpuTexture texture)
            {
                Texture = texture;
            }

            public GpuTexture Texture { get; }

            public bool TryAllocate(int width, int height, out int x, out int y)
            {
                var paddedWidth = width + Padding * 2;
                var paddedHeight = height + Padding * 2;
                if (paddedWidth > Texture.Width || paddedHeight > Texture.Height)
                {
                    x = 0;
                    y = 0;
                    return false;
                }

                if (_nextX + paddedWidth > Texture.Width)
                {
                    _nextX = 0;
                    _nextY += _rowHeight;
                    _rowHeight = 0;
                }

                if (_nextY + paddedHeight > Texture.Height)
                {
                    x = 0;
                    y = 0;
                    return false;
                }

                x = _nextX + Padding;
                y = _nextY + Padding;
                _nextX += paddedWidth;
                _rowHeight = Math.Max(_rowHeight, paddedHeight);
                return true;
            }
        }

        private readonly struct AtlasEntry
        {
            public AtlasEntry(AtlasPage page, Rect sourceRect)
            {
                Page = page;
                SourceRect = sourceRect;
            }

            public AtlasPage Page { get; }
            public Rect SourceRect { get; }
        }

        private sealed class ContextAtlas
        {
            public Dictionary<GlyphKey, AtlasEntry> Entries { get; } = new();
            public List<AtlasPage> Pages { get; } = new();
        }

        private static readonly object s_sync = new();
        private static readonly Dictionary<GlyphKey, DecodedGlyph> s_decodedGlyphs = new();
        private static readonly HashSet<GlyphKey> s_failedGlyphs = new();
        private static readonly Dictionary<WgpuContext, ContextAtlas> s_atlases = new();

        static BitmapGlyphCache()
        {
            WgpuContext.Disposing += OnContextDisposing;
        }

        public static bool TryGetMetrics(
            TtfFont font,
            ushort glyphIndex,
            double emSize,
            out BitmapGlyphMetrics metrics)
        {
            if (TryGetDecodedGlyph(font, glyphIndex, emSize, out _, out var decoded))
            {
                metrics = decoded.Metrics;
                return true;
            }

            metrics = default;
            return false;
        }

        public static bool TryGetTexture(
            TtfFont font,
            ushort glyphIndex,
            double emSize,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CachedBitmapGlyph? glyph)
        {
            glyph = null;
            if (!TryGetDecodedGlyph(font, glyphIndex, emSize, out var key, out var decoded))
            {
                return false;
            }

            var context = WgpuContext.Current;
            if (context == null || context.IsDisposed)
            {
                return false;
            }

            lock (context.RenderLock)
            {
                if (context.IsDisposed)
                {
                    return false;
                }

                lock (s_sync)
                {
                    if (!s_atlases.TryGetValue(context, out var atlas))
                    {
                        atlas = new ContextAtlas();
                        s_atlases.Add(context, atlas);
                    }

                    if (atlas.Entries.TryGetValue(key, out var cachedEntry) &&
                        !cachedEntry.Page.Texture.IsDisposed)
                    {
                        glyph = new CachedBitmapGlyph(
                            cachedEntry.Page.Texture,
                            decoded.Metrics,
                            cachedEntry.SourceRect);
                        return true;
                    }

                    AtlasPage? page = null;
                    var x = 0;
                    var y = 0;
                    foreach (var candidate in atlas.Pages)
                    {
                        if (candidate.TryAllocate(
                                decoded.Metrics.PixelWidth,
                                decoded.Metrics.PixelHeight,
                                out x,
                                out y))
                        {
                            page = candidate;
                            break;
                        }
                    }

                    if (page == null)
                    {
                        try
                        {
                            page = new AtlasPage(new GpuTexture(
                                context,
                                1024,
                                1024,
                                TextureFormat.Rgba8Unorm,
                                TextureUsage.TextureBinding | TextureUsage.CopyDst,
                                "ProGPU bitmap glyph atlas",
                                alphaMode: GpuTextureAlphaMode.Straight));
                        }
                        catch
                        {
                            return false;
                        }

                        atlas.Pages.Add(page);
                        if (!page.TryAllocate(
                                decoded.Metrics.PixelWidth,
                                decoded.Metrics.PixelHeight,
                                out x,
                                out y))
                        {
                            page.Texture.Dispose();
                            atlas.Pages.Remove(page);
                            return false;
                        }
                    }

                    var paddedWidth = decoded.Metrics.PixelWidth + 2;
                    var paddedHeight = decoded.Metrics.PixelHeight + 2;
                    var paddedPixels = new Rgba32[paddedWidth * paddedHeight];
                    for (var row = 0; row < decoded.Metrics.PixelHeight; row++)
                    {
                        decoded.Pixels.AsSpan(row * decoded.Metrics.PixelWidth, decoded.Metrics.PixelWidth)
                            .CopyTo(paddedPixels.AsSpan((row + 1) * paddedWidth + 1, decoded.Metrics.PixelWidth));
                    }

                    try
                    {
                        page.Texture.WritePixelsSubRect(
                            new ReadOnlySpan<Rgba32>(paddedPixels),
                            (uint)(x - 1),
                            (uint)(y - 1),
                            (uint)paddedWidth,
                            (uint)paddedHeight);
                    }
                    catch
                    {
                        return false;
                    }

                    var sourceRect = new Rect(
                        x,
                        y,
                        decoded.Metrics.PixelWidth,
                        decoded.Metrics.PixelHeight);
                    atlas.Entries[key] = new AtlasEntry(page, sourceRect);
                    glyph = new CachedBitmapGlyph(page.Texture, decoded.Metrics, sourceRect);
                    return true;
                }
            }
        }

        private static bool TryGetDecodedGlyph(
            TtfFont font,
            ushort glyphIndex,
            double emSize,
            out GlyphKey key,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DecodedGlyph? decoded)
        {
            decoded = null;
            if (!font.TryGetBitmapGlyph(glyphIndex, (float)emSize, out var bitmap))
            {
                key = default;
                return false;
            }

            key = new GlyphKey(font, glyphIndex, bitmap.PixelsPerEm);
            lock (s_sync)
            {
                if (s_decodedGlyphs.TryGetValue(key, out decoded))
                {
                    return true;
                }

                if (s_failedGlyphs.Contains(key))
                {
                    return false;
                }

                try
                {
                    using var image = Image.Load<Rgba32>(bitmap.Data.Span);
                    var pixels = new Rgba32[image.Width * image.Height];
                    image.CopyPixelDataTo(pixels);
                    decoded = new DecodedGlyph(
                        new BitmapGlyphMetrics(
                            bitmap.PixelsPerEm,
                            bitmap.PixelsPerInch,
                            bitmap.OriginOffsetX,
                            bitmap.OriginOffsetY,
                            image.Width,
                            image.Height),
                        pixels);
                    s_decodedGlyphs.Add(key, decoded);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidImageContentException or NotSupportedException or ArgumentException)
                {
                    s_failedGlyphs.Add(key);
                    return false;
                }
            }
        }

        private static void OnContextDisposing(WgpuContext context)
        {
            List<GpuTexture>? textures = null;
            lock (s_sync)
            {
                if (s_atlases.Remove(context, out var atlas))
                {
                    textures = new List<GpuTexture>(atlas.Pages.Count);
                    foreach (var page in atlas.Pages)
                    {
                        textures.Add(page.Texture);
                    }
                }
            }

            if (textures == null)
            {
                return;
            }

            foreach (var texture in textures)
            {
                texture.Dispose();
            }
        }
    }
}
