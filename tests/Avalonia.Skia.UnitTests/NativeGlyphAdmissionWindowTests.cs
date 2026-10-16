using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class NativeGlyphAdmissionWindowTests
{
    [Fact]
    public void Hint_Age_And_Fingerprint_Are_Checked_Including_Counter_Wrap()
    {
        const uint fingerprint = 42;
        const uint stamp = 1000;
        var hint = ((ulong)fingerprint << 32) | stamp;
        Assert.True(SharedGlyphRunCache.IsRecentAdmission(hint, fingerprint, stamp + 1));
        Assert.True(SharedGlyphRunCache.IsRecentAdmission(hint, fingerprint,
            stamp + SharedGlyphRunCache.AdmissionWindow));
        Assert.False(SharedGlyphRunCache.IsRecentAdmission(hint, fingerprint,
            stamp + SharedGlyphRunCache.AdmissionWindow + 1));
        Assert.False(SharedGlyphRunCache.IsRecentAdmission(hint, fingerprint + 1, stamp + 1));
        Assert.False(SharedGlyphRunCache.IsRecentAdmission(0, 0, 1));
        var wrappedHint = ((ulong)fingerprint << 32) | (uint.MaxValue - 2);
        Assert.True(SharedGlyphRunCache.IsRecentAdmission(wrappedHint, fingerprint, 1));
        Assert.False(SharedGlyphRunCache.IsRecentAdmission(wrappedHint, fingerprint,
            SharedGlyphRunCache.AdmissionWindow));
    }

    [Fact]
    public void Admission_Without_A_Hit_Does_Not_Reset_Per_Font_Backoff()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        face.RecordGlyphCacheBenefit();
        for (var i = 0; i < SkiaTypeface.GlyphProbeInterval; ++i)
        {
            Assert.True(face.ShouldProbeGlyphCache());
            face.RecordGlyphCacheMiss();
        }
        face.RecordGlyphCacheAdmission();
        Assert.True(face.ShouldProbeGlyphCache());
        face.RecordGlyphCacheMiss();
        Assert.False(face.ShouldProbeGlyphCache());
        face.RecordGlyphCacheBenefit();
        Assert.True(face.ShouldProbeGlyphCache());
    }

    [Fact]
    public void Bypassed_Requests_Age_Hints_Without_A_Global_Clock()
    {
        using var app = Start();
        var face = (SkiaTypeface)Typeface.Default.GlyphTypeface.PlatformTypeface;
        face.RecordGlyphCacheBenefit();
        for (var i = 0; i < SkiaTypeface.GlyphProbeInterval; ++i) face.RecordGlyphCacheMiss();
        var before = face.NextGlyphCacheAdmissionStamp();
        for (var i = 0; i < 20; ++i) Assert.False(face.ShouldProbeGlyphCache());
        var after = face.NextGlyphCacheAdmissionStamp();
        Assert.Equal(unchecked(before + 21), after);
        face.RecordGlyphCacheBenefit();
        Assert.True(face.ShouldProbeGlyphCache());
        // Successful probes do not advance the clock on the hot path.
        Assert.Equal(unchecked(after + 1), face.NextGlyphCacheAdmissionStamp());
    }

    [Fact]
    public void Oversized_Scans_Do_Not_Admit_Old_Hints_And_A_New_Hot_Run_Recovers()
    {
        using var app = Start();
        var font = Typeface.Default.GlyphTypeface;
        var face = (SkiaTypeface)font.PlatformTypeface;
        SharedGlyphRunCache.RemoveTypeface(face);
        face.RecordGlyphCacheBenefit();
        const double size = 17;
        var runs = CreateDistinctSlots(face, size, SharedGlyphRunCache.AdmissionWindow * 4);
        var before = SharedGlyphRunCache.Count;
        for (var pass = 0; pass < 4; ++pass)
            foreach (var glyphs in runs)
                using (var run = new GlyphRunImpl(font, size, glyphs, default))
                    Assert.True(run.Bounds.Width > 0);
        Assert.Equal(before, SharedGlyphRunCache.Count);

        var hot = new[] { new GlyphInfo(42, 0, 13.75), new GlyphInfo(44, 1, 11.25) };
        for (var i = 0; i < SkiaTypeface.GlyphProbeInterval * 3; ++i)
            new GlyphRunImpl(font, size, hot, default).Dispose();
        Assert.True(face.ShouldProbeGlyphCache());
        using var first = new GlyphRunImpl(font, size, hot, default);
        using var second = new GlyphRunImpl(font, size, hot, default);
        using var reference = new GlyphRunImpl(font, size, new UncachedGlyphs(hot), default);
        Assert.Same(first.SharedDataIdentity, second.SharedDataIdentity);
        Assert.Same(first.GetTextBlob(default, default), second.GetTextBlob(default, default));
        Assert.Equal(reference.Bounds, second.Bounds);
        Assert.Equal(Raster(reference), Raster(second));
    }

    private static GlyphInfo[][] CreateDistinctSlots(SkiaTypeface face, double size, int count)
    {
        // The cache is process-wide, so avoid fingerprints left by another test as well as
        // collisions within this corpus. Correctness never depends on hashes being unique;
        // only this deterministic test's zero-admission assertion needs the distinction.
        var field = typeof(SharedGlyphRunCache).GetField("s_recent", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (ulong[])field.GetValue(null)!;
        var excluded = new HashSet<uint>();
        foreach (var hint in previous) excluded.Add((uint)(hint >> 32));
        var slots = new HashSet<int>();
        var result = new List<GlyphInfo[]>();
        for (var i = 0; result.Count < count; ++i)
        {
            var glyphs = new[]
            {
                new GlyphInfo(42, 0, 12 + i / 100.0),
                new GlyphInfo(43, 1, 12), new GlyphInfo(44, 2, 12)
            };
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(face));
            hash.Add(size);
            foreach (var glyph in glyphs) hash.Add(glyph);
            var fingerprint = (uint)hash.ToHashCode();
            if (!excluded.Contains(fingerprint) && slots.Add((int)(fingerprint & (SharedGlyphRunCache.AdmissionSlots - 1))))
                result.Add(glyphs);
        }
        return result.ToArray();
    }

    private static byte[] Raster(GlyphRunImpl run)
    {
        using var surface = SKSurface.Create(new SKImageInfo(160, 64));
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        surface.Canvas.DrawText(run.GetTextBlob(default, default), 10, 40, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
        renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));

    private sealed class UncachedGlyphs(GlyphInfo[] glyphs) : IReadOnlyList<GlyphInfo>
    {
        public GlyphInfo this[int index] => glyphs[index];
        public int Count => glyphs.Length;
        public IEnumerator<GlyphInfo> GetEnumerator() => ((IEnumerable<GlyphInfo>)glyphs).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
