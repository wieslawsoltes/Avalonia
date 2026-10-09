using System;
using System.Linq;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class DefaultLineMetricsCacheTests
{
    [Theory]
    [InlineData("Ready to render")]
    [InlineData("office   ")]
    [InlineData("a\u0301b\ttext")]
    public void Cached_Metrics_Exactly_Match_Original_And_Recompute_Overflow(string text)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var properties = new GenericTextRunProperties(Typeface.Default, 16);
        var font = Typeface.Default.GlyphTypeface;
        var options = new TextShaperOptions(font, 16);
        shaper.ShapeText(text.AsMemory(), options).Dispose();
        using var admitted = Line(shaper.ShapeText(text.AsMemory(), options), properties, 500);
        using var reference = Line(shaper.ShapeText(text.ToCharArray().AsMemory(), options), properties, 10);
        var buffer = shaper.ShapeText(text.AsMemory(), options);
        Assert.NotNull(buffer.SharedMetrics);
        using var cached = Line(buffer, properties, 10);
        Assert.False(((ShapedTextRun)cached.TextRuns[0]).HasGlyphRun);
        Assert.True(((ShapedTextRun)reference.TextRuns[0]).HasGlyphRun);
        Assert.Equal(Metrics(reference), Metrics(cached));
        Assert.Equal(reference.Bounds, cached.Bounds);
        Assert.Equal(reference.InkBounds, cached.InkBounds);
        Assert.Equal(reference.GetCharacterHitFromDistance(12), cached.GetCharacterHitFromDistance(12));
    }

    [Fact]
    public void Buffer_And_Alias_Mutations_Invalidate_Shared_Metrics()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var properties = new GenericTextRunProperties(Typeface.Default, 16);
        var options = new TextShaperOptions(Typeface.Default.GlyphTypeface, 16);
        shaper.ShapeText("mutable".AsMemory(), options).Dispose();
        using var admitted = Line(shaper.ShapeText("mutable".AsMemory(), options), properties, 500);
        var buffer = shaper.ShapeText("mutable".AsMemory(), options);
        Assert.NotNull(buffer.SharedMetrics);
        using (var alias = buffer.WithBidiLevel(0))
            alias[0] = new GlyphInfo(buffer[0].GlyphIndex, 0, 100);
        Assert.Null(buffer.SharedMetrics);
        using var changed = Line(buffer, properties, 500);
        Assert.True(((ShapedTextRun)changed.TextRuns[0]).HasGlyphRun);
        Assert.NotEqual(admitted.Width, changed.Width);
    }

    [Theory]
    [InlineData(TextAlignment.Center, 0)]
    [InlineData(TextAlignment.Left, 40)]
    public void NonDefault_Paragraphs_Keep_Original_Metrics(TextAlignment alignment, double height)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var properties = new GenericTextRunProperties(Typeface.Default, 16);
        var options = new TextShaperOptions(Typeface.Default.GlyphTypeface, 16);
        shaper.ShapeText("paragraph".AsMemory(), options).Dispose();
        using var admitted = Line(shaper.ShapeText("paragraph".AsMemory(), options), properties, 500);
        var paragraph = new GenericTextParagraphProperties(properties, textAlignment: alignment, lineHeight: height);
        var run = new ShapedTextRun(shaper.ShapeText("paragraph".AsMemory(), options), properties);
        using var line = new TextLineImpl(new TextRun[] { run }, 0, run.Length, 500, paragraph);
        line.FinalizeLine();
        Assert.True(run.HasGlyphRun);
        if (height != 0) Assert.Equal(height, line.Height);
        if (alignment == TextAlignment.Center) Assert.True(line.Start > 0);
    }

    [Fact]
    public void Exposed_Mutable_GlyphRun_Is_Not_Bypassed()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var properties = new GenericTextRunProperties(Typeface.Default, 16);
        var options = new TextShaperOptions(Typeface.Default.GlyphTypeface, 16);
        shaper.ShapeText("exposed".AsMemory(), options).Dispose();
        using var admitted = Line(shaper.ShapeText("exposed".AsMemory(), options), properties, 500);
        var run = new ShapedTextRun(shaper.ShapeText("exposed".AsMemory(), options), properties);
        _ = run.GlyphRun;
        Assert.True(run.HasGlyphRun);
        using var line = new TextLineImpl(new TextRun[] { run }, 0, run.Length, 500, new GenericTextParagraphProperties(properties));
        line.FinalizeLine();
        Assert.Equal(admitted.Width, line.Width);
    }

    private static IDisposable Start(HarfBuzzTextShaper shaper) =>
        UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(renderInterface: new PlatformRenderInterface(),
            fontManagerImpl: new FontManagerImpl(), textShaperImpl: shaper));
    private static TextLineImpl Line(ShapedBuffer buffer, TextRunProperties properties, double width)
    {
        var run = new ShapedTextRun(buffer, properties);
        var line = new TextLineImpl(new TextRun[] { run }, 0, run.Length, width, new GenericTextParagraphProperties(properties));
        line.FinalizeLine();
        return line;
    }
    private static object[] Metrics(TextLine line) => new object[]
    {
        line.HasOverflowed, line.Baseline, line.Extent, line.Height, line.NewLineLength,
        line.OverhangAfter, line.OverhangLeading, line.OverhangTrailing, line.TrailingWhitespaceLength,
        line.Start, line.Width, line.WidthIncludingTrailingWhitespace
    };
}
