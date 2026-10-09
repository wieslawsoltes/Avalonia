using System;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class ColdGlyphCacheTests
{
    [Fact]
    public void Single_Font_State_Does_Not_Allocate_Expanded_Blob_Storage()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        var data = new SharedGlyphRunData(face, 17, new[] { new GlyphInfo(42, 0, 12) });
        var field = typeof(SharedGlyphRunData).GetField("_blobs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var first = data.GetTextBlob(new TextOptions { TextRenderingMode = TextRenderingMode.Alias });
            Assert.Null(field.GetValue(data));
            Assert.Same(first, data.GetTextBlob(new TextOptions { TextRenderingMode = TextRenderingMode.Alias }));
            var second = data.GetTextBlob(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias });
            Assert.NotNull(field.GetValue(data));
            Assert.NotSame(first, second);
            Assert.NotEqual(IntPtr.Zero, first.Handle);
            Assert.Same(first, data.GetTextBlob(new TextOptions { TextRenderingMode = TextRenderingMode.Alias }));
        }
        finally { data.Release(); }
    }

    [Fact]
    public void Per_Font_Probe_Backoff_Recovers_After_A_Benefit()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        face.RecordGlyphCacheBenefit();
        for (var i = 0; i < SkiaTypeface.GlyphProbeInterval; ++i)
        {
            Assert.True(face.ShouldProbeGlyphCache());
            face.RecordGlyphCacheMiss();
        }
        for (var i = 1; i < SkiaTypeface.GlyphProbeInterval; ++i)
            Assert.False(face.ShouldProbeGlyphCache());
        Assert.True(face.ShouldProbeGlyphCache());
        face.RecordGlyphCacheBenefit();
        Assert.True(face.ShouldProbeGlyphCache());
    }

    private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
        renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
}
