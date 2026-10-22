using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class SharedGlyphBlobLifetimeTests
{
    [Fact]
    public void Other_Wrappers_Changing_Options_Cannot_Dispose_Borrowed_Blobs()
    {
        using var app = UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
            renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
        var font = Typeface.Default.GlyphTypeface;
        var glyphs = new[] { new GlyphInfo(42, 0, 12), new GlyphInfo(43, 1, 12) };
        using var cold = new GlyphRunImpl(font, 23, glyphs, default);
        using var first = new GlyphRunImpl(font, 23, glyphs, default);
        using var second = new GlyphRunImpl(font, 23, glyphs, default);
        Assert.Same(first.SharedDataIdentity, second.SharedDataIdentity);
        var borrowed = first.GetTextBlob(default, default);
        var bounds = borrowed.Bounds;
        var options = new List<TextOptions>();
        foreach (var rendering in new[] { TextRenderingMode.Alias, TextRenderingMode.Antialias, TextRenderingMode.SubpixelAntialias })
            foreach (var hinting in new[] { TextHintingMode.None, TextHintingMode.Light, TextHintingMode.Strong })
                for (var snap = 0; snap < 2; ++snap)
                    options.Add(new TextOptions
                    {
                        TextRenderingMode = rendering,
                        TextHintingMode = hinting,
                        BaselinePixelAlignment = snap == 0 ? BaselinePixelAlignment.Unaligned : default
                    });
        foreach (var option in options)
        {
            second.GetTextBlob(option, default);
            Assert.NotEqual(IntPtr.Zero, borrowed.Handle);
            Assert.Equal(bounds, borrowed.Bounds);
        }
        // Each thread owns its wrapper; only immutable geometry and blob storage are shared.
        Parallel.For(0, 18, i =>
        {
            using var run = new GlyphRunImpl(font, 23, glyphs, default);
            var blob = run.GetTextBlob(options[i], default);
            Assert.NotEqual(IntPtr.Zero, blob.Handle);
            Assert.NotEqual(IntPtr.Zero, borrowed.Handle);
        });
        second.Dispose();
        Assert.NotEqual(IntPtr.Zero, borrowed.Handle);
        Assert.Equal(bounds, borrowed.Bounds);
    }
}
