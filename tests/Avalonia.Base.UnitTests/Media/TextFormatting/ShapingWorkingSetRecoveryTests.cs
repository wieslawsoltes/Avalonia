using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class ShapingWorkingSetRecoveryTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(32)]
    [InlineData(256)]
    public void A_Cyclic_Working_Set_Recovers_After_Cold_Backoff(int count)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        var font = new GlyphTypeface(new CustomPlatformTypeface(stream));
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16, culture: CultureInfo.InvariantCulture);
        var face = (HarfBuzzTypeface)font.TextShaperTypeface;
        var cache = shaper.ShapedRunCache;
        var missing = new ShapedRunCache.Key("never admitted", face.CacheId, 16, 0, "", 127, 0, 0);
        for (var i = 0; i < ShapedRunCache.ProbeInterval; ++i)
            Assert.False(cache.TryGet(missing, missing.Text.AsMemory(), options, out _));

        var keys = new List<ShapedRunCache.Key>();
        var slots = new HashSet<int>();
        for (var i = 0; keys.Count < count; ++i)
        {
            var text = "hot " + i.ToString(CultureInfo.InvariantCulture);
            Assert.True(ShapedRunCache.TryCreateKey(text.AsMemory(), options,
                CultureInfo.InvariantCulture, face.CacheId, out var key));
            if (slots.Add(key.GetHashCode() & (ShapedRunCache.AdmissionSlots - 1))) keys.Add(key);
        }
        var requests = ShapedRunCache.ProbeInterval * ShapedRunCache.RecoverySampleInterval +
            ShapedRunCache.RecoveryProbeCount + count * 3;
        for (var i = 0; i < requests; ++i)
            shaper.ShapeText(keys[i % count].Text.AsMemory(), options).Dispose();

        Assert.Equal(count, cache.Count);
        Assert.True(cache.ShouldProbe());
        foreach (var key in keys)
        {
            Assert.True(cache.TryGet(key, key.Text.AsMemory(), options, out var hit));
            using (hit)
            using (var reference = shaper.ShapeText(key.Text.ToCharArray().AsMemory(), options))
            {
                Assert.Equal(reference.Length, hit!.Length);
                for (var i = 0; i < hit.Length; ++i) Assert.Equal(reference[i], hit[i]);
            }
        }
    }
}
