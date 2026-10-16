using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class RawGlyphBlobTests
{
    private static readonly GlyphInfo[] s_glyphs =
    {
        new(42, 0, 11), new(43, 1, 13, new Vector(0.5, -1)), new(44, 2, 10)
    };
    private static readonly ushort[] s_indices = { 42, 43, 44 };
    private static readonly SKPoint[] s_positions = { new(0, 0), new(11.5f, -1), new(24, 0) };

    [Fact]
    public void Raw_Blob_Matches_Previous_Wrapper_Path_For_All_Font_States()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        var data = new SharedGlyphRunData(face, 17, s_glyphs);
        try
        {
            foreach (var rendering in new[] { TextRenderingMode.Alias, TextRenderingMode.Antialias, TextRenderingMode.SubpixelAntialias })
                foreach (var hinting in new[] { TextHintingMode.None, TextHintingMode.Light, TextHintingMode.Strong })
                    for (var snap = 0; snap < 2; ++snap)
                    {
                        var options = new TextOptions
                        {
                            TextRenderingMode = rendering,
                            TextHintingMode = hinting,
                            BaselinePixelAlignment = snap == 0 ? BaselinePixelAlignment.Unaligned : default
                        };
                        using var reference = BuildWithWrapper(face, options);
                        var actual = data.GetTextBlob(options);
                        Assert.Equal(reference.Bounds, actual.Bounds);
                        Assert.Equal(Raster(reference), Raster(actual));
                        Assert.Same(actual, data.GetTextBlob(options));
                    }
        }
        finally { data.Release(); }
    }

    [Fact]
    public void First_Blob_Creation_Allocates_Less_Than_The_Previous_Wrapper_Path()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        const int count = 128;
        // Geometry and test fixtures are outside both measured regions. Both paths build
        // a fresh native blob and font; only the previous path allocates a run-view object.
        var runs = new SharedGlyphRunData[count];
        try
        {
            for (var i = 0; i < count; ++i)
            {
                runs[i] = new SharedGlyphRunData(face, 17, s_glyphs);
                var warm = new SharedGlyphRunData(face, 17, s_glyphs);
                try { warm.GetTextBlob(default); }
                finally { warm.Release(); }
                using var reference = BuildWithWrapper(face, default);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; ++i)
                runs[i].GetTextBlob(default);
            var rawBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; ++i)
            {
                using var reference = BuildWithWrapper(face, default);
            }
            var wrapperBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(wrapperBytes - rawBytes >= count * 16L,
                $"Expected the run-view object to be removed; raw={rawBytes}, wrapper={wrapperBytes}.");
        }
        finally
        {
            foreach (var data in runs) data?.Release();
        }
    }

    private static SKTextBlob BuildWithWrapper(SkiaTypeface face, TextOptions options)
    {
        using var font = face.CreateSKFont(17);
        font.ForceAutoHinting = options.TextHintingMode == TextHintingMode.Light;
        font.Hinting = options.TextHintingMode switch
        {
            TextHintingMode.None => SKFontHinting.None,
            TextHintingMode.Light => SKFontHinting.Slight,
            _ => SKFontHinting.Full
        };
        font.Edging = options.TextRenderingMode switch
        {
            TextRenderingMode.Alias => SKFontEdging.Alias,
            TextRenderingMode.Antialias => SKFontEdging.Antialias,
            _ => SKFontEdging.SubpixelAntialias
        };
        font.Subpixel = font.Edging != SKFontEdging.Alias;
        font.BaselineSnap = options.BaselinePixelAlignment != BaselinePixelAlignment.Unaligned;
        var builder = SKTextBlobBuilderCache.Shared.Get();
        try
        {
            var run = builder.AllocatePositionedRun(font, s_indices.Length);
            run.SetPositions(s_positions);
            run.SetGlyphs(s_indices);
            return builder.Build()!;
        }
        finally { SKTextBlobBuilderCache.Shared.Return(builder); }
    }

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
}
