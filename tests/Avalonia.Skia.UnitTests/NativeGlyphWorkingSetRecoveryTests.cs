using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class NativeGlyphWorkingSetRecoveryTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    [InlineData(128)]
    public void A_Cyclic_Working_Set_Recovers_After_Cold_Backoff(int count)
    {
        using var app = UnitTestApplication.Start(TestServices.MockPlatformRenderInterface.With(
            renderInterface: new PlatformRenderInterface(), fontManagerImpl: new FontManagerImpl()));
        var font = Typeface.Default.GlyphTypeface;
        var face = (SkiaTypeface)font.PlatformTypeface;
        SharedGlyphRunCache.RemoveTypeface(face);
        face.RecordGlyphCacheBenefit();
        for (var i = 0; i < SkiaTypeface.GlyphProbeInterval; ++i) face.RecordGlyphCacheMiss();
        const double size = 17;

        var hints = (ulong[])typeof(SharedGlyphRunCache).GetField("s_recent",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var excluded = new HashSet<uint>();
        foreach (var hint in hints) excluded.Add((uint)(hint >> 32));
        var slots = new HashSet<int>();
        var runs = new List<GlyphInfo[]>();
        for (var i = 0; runs.Count < count; ++i)
        {
            var glyphs = new[] { new GlyphInfo(42, 0, 12 + i / 100.0), new GlyphInfo(43, 1, 12) };
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(face));
            hash.Add(size);
            foreach (var glyph in glyphs) hash.Add(glyph);
            var fingerprint = (uint)hash.ToHashCode();
            if (!excluded.Contains(fingerprint) && slots.Add((int)(fingerprint & (SharedGlyphRunCache.AdmissionSlots - 1))))
                runs.Add(glyphs);
        }

        var identities = new object?[count];
        var reused = 0;
        var requests = SkiaTypeface.GlyphProbeInterval * SkiaTypeface.GlyphRecoverySampleInterval +
            SkiaTypeface.GlyphRecoveryProbeCount + count * 3;
        for (var i = 0; i < requests; ++i)
        {
            var index = i % count;
            using var run = new GlyphRunImpl(font, size, runs[index], default);
            var identity = run.SharedDataIdentity;
            if (ReferenceEquals(identities[index], identity)) ++reused;
            identities[index] = identity;
        }
        // Observed identities are captured during the cyclic workload: an adjacent pair of
        // calls after the loop must not accidentally warm the very cache being tested.
        Assert.True(reused >= count * 2, $"Only {reused} actual reuses for a {count}-run working set.");
        Assert.True(face.ShouldProbeGlyphCache());
        foreach (var glyphs in runs)
        {
            using var first = new GlyphRunImpl(font, size, glyphs, default);
            using var second = new GlyphRunImpl(font, size, glyphs, default);
            Assert.Same(first.SharedDataIdentity, second.SharedDataIdentity);
            Assert.Same(first.GetTextBlob(default, default), second.GetTextBlob(default, default));
        }
    }
}
