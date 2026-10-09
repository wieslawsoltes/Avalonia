using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class SharedGlyphRunTests
{
    [Fact]
    public void Repeated_Runs_Share_Geometry_And_Blobs_But_Not_Baseline()
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = Glyphs();
        using var first = new GlyphRunImpl(font, 14, glyphs, default);
        using var admitted = new GlyphRunImpl(font, 14, glyphs, default);
        using var hit = new GlyphRunImpl(font, 14, glyphs, new Point(10, 20));
        Assert.Same(admitted.SharedDataIdentity, hit.SharedDataIdentity);
        Assert.Same(admitted.GetTextBlob(default, default), hit.GetTextBlob(default, default));
        Assert.Equal(admitted.Bounds.Translate(new Vector(10, 20)), hit.Bounds);
        Assert.Equal(new Point(10, 20), hit.BaselineOrigin);
    }

    [Theory]
    [InlineData(TextRenderingMode.Alias)]
    [InlineData(TextRenderingMode.Antialias)]
    [InlineData(TextRenderingMode.SubpixelAntialias)]
    public void Shared_And_Uncached_Runs_Have_Exactly_Equal_Raster_Output(TextRenderingMode mode)
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = Glyphs();
        using var reference = new GlyphRunImpl(font, 17, new UncachedGlyphs(glyphs), default);
        using var first = new GlyphRunImpl(font, 17, glyphs, default);
        using var admitted = new GlyphRunImpl(font, 17, glyphs, default);
        using var cached = new GlyphRunImpl(font, 17, glyphs, default);
        var options = default(TextOptions) with { TextRenderingMode = mode };
        Assert.Equal(reference.Bounds, cached.Bounds);
        Assert.Equal(Raster(reference, options), Raster(cached, options));
    }

    [Fact]
    public void Disposing_Another_Run_Or_Evicting_Does_Not_Invalidate_A_Live_Blob()
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = Glyphs();
        using var first = new GlyphRunImpl(font, 18, glyphs, default);
        var admitted = new GlyphRunImpl(font, 18, glyphs, default);
        using var live = new GlyphRunImpl(font, 18, glyphs, default);
        var expected = Raster(live, default);
        admitted.Dispose();
        for (var i = 0; i < 300; ++i)
        {
            using var cold = new GlyphRunImpl(font, 20 + i, glyphs, default);
            using var warm = new GlyphRunImpl(font, 20 + i, glyphs, default);
            // Exercise eviction with demonstrably useful entries rather than relying on
            // admission-only work to reset the adaptive miss streak.
            using var hit = new GlyphRunImpl(font, 20 + i, glyphs, default);
            Assert.Same(warm.SharedDataIdentity, hit.SharedDataIdentity);
        }
        Assert.Equal(SharedGlyphRunCache.Capacity, SharedGlyphRunCache.Count);
        using var recreated = new GlyphRunImpl(font, 18, glyphs, default);
        Assert.NotSame(live.SharedDataIdentity, recreated.SharedDataIdentity);
        Assert.Equal(expected, Raster(live, default));
    }

    [Fact]
    public void Mutated_Glyph_Data_And_Text_Options_Do_Not_Alias()
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = Glyphs();
        using var first = new GlyphRunImpl(font, 19, glyphs, default);
        using var admitted = new GlyphRunImpl(font, 19, glyphs, default);
        var expected = Raster(admitted, default);
        glyphs[0] = new GlyphInfo(9, 0, 30, new Vector(4, 2));
        using var changed = new GlyphRunImpl(font, 19, glyphs, default);
        Assert.NotSame(admitted.SharedDataIdentity, changed.SharedDataIdentity);
        Assert.Equal(expected, Raster(admitted, default));
        var alias = default(TextOptions) with { TextRenderingMode = TextRenderingMode.Alias };
        var antialias = default(TextOptions) with { TextRenderingMode = TextRenderingMode.Antialias };
        Assert.NotSame(admitted.GetTextBlob(alias, default), admitted.GetTextBlob(antialias, default));
    }

    private static GlyphInfo[] Glyphs() => new[]
    {
        new GlyphInfo(42, 0, 11), new GlyphInfo(43, 1, 13, new Vector(0.5, -1)), new GlyphInfo(44, 2, 10)
    };
    private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
        renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
    private static byte[] Raster(GlyphRunImpl run, TextOptions options)
    {
        using var surface = SKSurface.Create(new SKImageInfo(160, 64));
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        surface.Canvas.DrawText(run.GetTextBlob(options, default), 10, 40, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private sealed class UncachedGlyphs(GlyphInfo[] glyphs) : IReadOnlyList<GlyphInfo>
    {
        public GlyphInfo this[int index] => glyphs[index];
        public int Count => glyphs.Length;
        public IEnumerator<GlyphInfo> GetEnumerator() => ((IEnumerable<GlyphInfo>)glyphs).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
