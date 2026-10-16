using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class GlyphRunFontPoolTests
{
    [Fact]
    public void Same_State_Reuses_An_Exclusive_Font_And_Copied_Lease_Cannot_Return_It_Twice()
    {
        using var app = Start();
        var pool = Face.GlyphFonts;
        var first = pool.Rent(17, default);
        var staleCopy = first;
        var font = first.Font;
        first.Dispose();
        using var second = pool.Rent(17, default);
        Assert.Same(font, second.Font);
        staleCopy.Dispose();
        using var third = pool.Rent(17, default);
        Assert.NotSame(second.Font, third.Font);
    }

    [Fact]
    public void All_Option_States_And_Sizes_Produce_The_Original_Font_Configuration()
    {
        using var app = Start();
        var face = Face;
        foreach (var size in new[] { 13f, 29f })
        foreach (var rendering in new[] { TextRenderingMode.Alias, TextRenderingMode.Antialias, TextRenderingMode.SubpixelAntialias })
        foreach (var hinting in new[] { TextHintingMode.None, TextHintingMode.Light, TextHintingMode.Strong })
        foreach (var unaligned in new[] { false, true })
        {
            var options = new TextOptions
            {
                TextRenderingMode = rendering,
                TextHintingMode = hinting,
                BaselinePixelAlignment = unaligned ? BaselinePixelAlignment.Unaligned : default
            };
            using var rental = face.GlyphFonts.Rent(size, options);
            var font = rental.Font;
            Assert.Equal(size, font.Size);
            Assert.True(font.LinearMetrics);
            Assert.Equal(rendering != TextRenderingMode.Alias, font.Subpixel);
            Assert.Equal(rendering switch
            {
                TextRenderingMode.Alias => SKFontEdging.Alias,
                TextRenderingMode.Antialias => SKFontEdging.Antialias,
                _ => SKFontEdging.SubpixelAntialias
            }, font.Edging);
            Assert.Equal(hinting == TextHintingMode.Light, font.ForceAutoHinting);
            Assert.Equal(hinting switch
            {
                TextHintingMode.None => SKFontHinting.None,
                TextHintingMode.Light => SKFontHinting.Slight,
                _ => SKFontHinting.Full
            }, font.Hinting);
            Assert.Equal(!unaligned, font.BaselineSnap);
        }
        Assert.InRange(face.GlyphFonts.RetainedCount, 1, GlyphRunFontPool.Capacity);
    }

    [Fact]
    public void Concurrent_Users_Never_Borrow_The_Same_Mutable_Font()
    {
        using var app = Start();
        var pool = Face.GlyphFonts;
        var active = new HashSet<SKFont>();
        var gate = new object();
        Parallel.For(0, 128, _ =>
        {
            using var lease = pool.Rent(19, default);
            lock (gate) Assert.True(active.Add(lease.Font));
            Thread.SpinWait(1000);
            Assert.Equal(19f, lease.Font.Size);
            lock (gate) Assert.True(active.Remove(lease.Font));
        });
        Assert.Empty(active);
        Assert.InRange(pool.RetainedCount, 1, GlyphRunFontPool.Capacity);
    }

    [Fact]
    public void Capacity_Overflow_Is_Not_Retained_And_Disposal_Defers_Active_Fonts()
    {
        using var app = Start();
        var face = Face;
        var pool = face.GlyphFonts;
        var leases = new GlyphRunFontPool.Lease[GlyphRunFontPool.Capacity + 2];
        try
        {
            for (var i = 0; i < leases.Length; ++i) leases[i] = pool.Rent(11 + i, default);
            Assert.Equal(GlyphRunFontPool.Capacity, pool.RetainedCount);
            var idle = leases[0].Font;
            leases[0].Dispose();
            var active = leases[1].Font;
            face.Dispose();
            Assert.Equal(IntPtr.Zero, idle.Handle);
            Assert.NotEqual(IntPtr.Zero, active.Handle);
            Assert.True(float.IsFinite(active.Metrics.Ascent));
            Assert.Throws<ObjectDisposedException>(() => pool.Rent(12, default));
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
        Assert.Equal(0, pool.RetainedCount);
        foreach (var lease in leases) Assert.Equal(IntPtr.Zero, lease.Font.Handle);
    }

    [Fact]
    public void Warm_Leases_Allocate_No_Managed_Fonts_Or_Lease_Objects()
    {
        using var app = Start();
        var pool = Face.GlyphFonts;
        for (var i = 0; i < 64; ++i) pool.Rent(17, default).Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 4096; ++i) pool.Rent(17, default).Dispose();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static SkiaTypeface Face => (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
    private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
        renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
}
