using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class InlineGlyphRunTests
{
    [Fact]
    public void Primary_Blob_Uses_No_Secondary_Storage_And_Warm_Lookup_Allocates_Nothing()
    {
        using var app = Start();
        using var run = new GlyphRunImpl(Typeface.Default.GlyphTypeface, 17, Glyphs(), default);
        var field = typeof(GlyphRunImpl).GetField("_secondaryBlobs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(field.GetValue(run));
        var first = run.GetTextBlob(default, default);
        for (var i = 0; i < 100; ++i) run.GetTextBlob(default, default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; ++i) run.GetTextBlob(default, default);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Null(field.GetValue(run));
        Assert.Same(first, run.GetTextBlob(default, default));
        run.GetTextBlob(new TextOptions { TextRenderingMode = TextRenderingMode.Alias }, default);
        Assert.NotNull(field.GetValue(run));
        Assert.Same(first, run.GetTextBlob(default, default));
    }

    [Fact]
    public void All_Options_Survive_Secondary_Eviction_And_Wrapper_Disposal()
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = Glyphs();
        using var cold = new GlyphRunImpl(font, 17, glyphs, default);
        using var run = new GlyphRunImpl(font, 17, glyphs, default);
        using var alias = new GlyphRunImpl(font, 17, glyphs, default);
        Assert.Same(run.SharedDataIdentity, alias.SharedDataIdentity);
        var expected = new List<(TextOptions Options, SKTextBlob Blob)>();
        foreach (var rendering in new[] { TextRenderingMode.Alias, TextRenderingMode.Antialias, TextRenderingMode.SubpixelAntialias })
            foreach (var hinting in new[] { TextHintingMode.None, TextHintingMode.Light, TextHintingMode.Strong })
                foreach (var alignment in new[] { BaselinePixelAlignment.Unaligned, BaselinePixelAlignment.Unspecified })
                {
                    var options = new TextOptions { TextRenderingMode = rendering, TextHintingMode = hinting, BaselinePixelAlignment = alignment };
                    expected.Add((options, run.GetTextBlob(options, default)));
                }
        for (var pass = 0; pass < 3; ++pass)
            foreach (var pair in expected)
                Assert.Same(pair.Blob, alias.GetTextBlob(pair.Options, default));
        alias.Dispose();
        foreach (var pair in expected)
        {
            Assert.NotEqual(IntPtr.Zero, pair.Blob.Handle);
            Assert.Same(pair.Blob, run.GetTextBlob(pair.Options, default));
        }
        run.Dispose();
        Assert.Throws<ObjectDisposedException>(() => run.GetTextBlob(default, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sliced_Shaped_Storage_Matches_General_List_And_Owns_An_Independent_Copy(bool rtl)
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        using var source = new ShapedBuffer("abcd".AsMemory(), 4, font, 17, (sbyte)(rtl ? 1 : 0));
        var glyphs = Glyphs();
        for (var i = 0; i < 4; ++i) source[i] = glyphs[rtl ? 3 - i : i];
        var parts = source.Split(2);
        using var first = parts.First!;
        using var slice = parts.Second!;
        var snapshot = slice.ToArray();
        Assert.True(SharedGlyphRunData.TryGetGlyphSpan(slice, out var span));
        Assert.Equal(snapshot, span.ToArray());
        var fallback = new CallbackGlyphs(snapshot);
        var face = (SkiaTypeface)font.PlatformTypeface;
        var reference = new SharedGlyphRunData(face, 17, fallback);
        var actual = new SharedGlyphRunData(face, 17, slice);
        try
        {
            Assert.Equal(Enumerable.Range(0, snapshot.Length).Concat(Enumerable.Range(0, snapshot.Length)), fallback.Reads);
            Assert.Equal(reference.RelativeBounds, actual.RelativeBounds);
            Assert.Equal(Raster(reference.GetTextBlob(default)), Raster(actual.GetTextBlob(default)));
            slice[0] = new GlyphInfo(9, 0, 100, new Vector(30, 40));
            source.Dispose(); first.Dispose(); slice.Dispose();
            Assert.Equal(Raster(reference.GetTextBlob(default)), Raster(actual.GetTextBlob(default)));
        }
        finally { actual.Release(); reference.Release(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(129)]
    public void Array_Span_Has_Exact_Length_And_Custom_Lists_Are_Not_Unwrapped(int count)
    {
        var glyphs = new GlyphInfo[count];
        Assert.True(SharedGlyphRunData.TryGetGlyphSpan(glyphs, out var span));
        Assert.Equal(count, span.Length);
        Assert.False(SharedGlyphRunData.TryGetGlyphSpan(new CallbackGlyphs(glyphs), out _));
    }

    private static GlyphInfo[] Glyphs() => new[]
    {
        new GlyphInfo(42, 0, 11), new GlyphInfo(43, 1, 13, new Vector(0.5, -1)),
        new GlyphInfo(44, 2, 10), new GlyphInfo(45, 3, 9, new Vector(-0.25, 0.5))
    };
    private static byte[] Raster(SKTextBlob blob)
    {
        using var surface = SKSurface.Create(new SKImageInfo(160, 64));
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        surface.Canvas.DrawText(blob, 10, 40, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
        renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
    private sealed class CallbackGlyphs(GlyphInfo[] glyphs) : IReadOnlyList<GlyphInfo>
    {
        public readonly List<int> Reads = new();
        public GlyphInfo this[int index] { get { Reads.Add(index); return glyphs[index]; } }
        public int Count => glyphs.Length;
        public IEnumerator<GlyphInfo> GetEnumerator() => ((IEnumerable<GlyphInfo>)glyphs).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
