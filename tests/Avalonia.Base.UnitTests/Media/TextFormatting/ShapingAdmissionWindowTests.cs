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

public class ShapingAdmissionWindowTests
{
    [Fact]
    public void Repeated_OverCapacity_Scans_Expire_Hints_Without_Allocating_Snapshots()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16);
        using var source = shaper.ShapeText("probe".ToCharArray().AsMemory(), options);
        var cache = new ShapedRunCache();
        var keys = CreateDistinctSlots(ShapedRunCache.AdmissionWindow * 2, 1);
        foreach (var key in keys) cache.Add(key, source);
        Assert.Equal(0, cache.Count);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var pass = 0; pass < 8; ++pass)
            foreach (var key in keys) cache.Add(key, source);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, allocated);
        Assert.Equal(ShapedRunCache.AdmissionRetainedBytes, cache.RetainedBytes);
    }

    [Fact]
    public void Bypassed_Requests_Also_Age_An_Admission_Hint()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16);
        using var source = shaper.ShapeText("probe".ToCharArray().AsMemory(), options);
        var cache = new ShapedRunCache();
        var key = new ShapedRunCache.Key("probe", 1, 16, 0, "", 0, 0, 0);
        var missing = key with { Text = "never admitted" };
        cache.Add(key, source);
        for (var i = 0; i < ShapedRunCache.AdmissionWindow * 2; ++i)
            if (cache.ShouldProbe())
                Assert.False(cache.TryGet(missing, missing.Text.AsMemory(), options, out _));
        cache.Add(key, source);
        Assert.Equal(0, cache.Count);
        // A second recent touch still admits; expiration must not permanently disable reuse.
        cache.Add(key, source);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void An_Admission_Without_A_Hit_Does_Not_Reset_The_Miss_Streak()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16);
        using var source = shaper.ShapeText("probe".ToCharArray().AsMemory(), options);
        var cache = new ShapedRunCache();
        var key = new ShapedRunCache.Key("probe", 1, 16, 0, "", 0, 0, 0);
        var missing = key with { Text = "never admitted" };
        for (var i = 0; i < ShapedRunCache.ProbeInterval; ++i)
            Assert.False(cache.TryGet(missing, missing.Text.AsMemory(), options, out _));
        cache.Add(key, source);
        cache.Add(key, source);
        Assert.True(cache.ShouldProbe());
        Assert.False(cache.TryGet(missing, missing.Text.AsMemory(), options, out _));
        Assert.False(cache.ShouldProbe());
        Assert.True(cache.TryGet(key, key.Text.AsMemory(), options, out var hit));
        hit!.Dispose();
        Assert.True(cache.ShouldProbe());
    }

    [Fact]
    public void A_New_Hot_Run_Recovers_After_Repeated_OverCapacity_Scans()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16, culture: CultureInfo.InvariantCulture);
        var face = (HarfBuzzTypeface)font.TextShaperTypeface;
        var keys = CreateDistinctSlots(ShapedRunCache.AdmissionWindow * 2, face.CacheId);
        for (var pass = 0; pass < 4; ++pass)
            foreach (var key in keys)
                shaper.ShapeText(key.Text.AsMemory(), options).Dispose();
        Assert.Equal(0, shaper.ShapedRunCache.Count);
        const string text = "newly hot after cold scan";
        for (var i = 0; i < ShapedRunCache.ProbeInterval * 3; ++i)
            shaper.ShapeText(text.AsMemory(), options).Dispose();
        Assert.True(shaper.ShapedRunCache.ShouldProbe());
        Assert.Equal(1, shaper.ShapedRunCache.Count);
        using var hit = shaper.ShapeText(text.AsMemory(), options);
        using var reference = shaper.ShapeText(text.ToCharArray().AsMemory(), options);
        Assert.Equal(reference.Length, hit.Length);
        for (var i = 0; i < hit.Length; ++i) Assert.Equal(reference[i], hit[i]);
    }

    private static ShapedRunCache.Key[] CreateDistinctSlots(int count, long face)
    {
        var keys = new List<ShapedRunCache.Key>();
        var slots = new HashSet<int>();
        for (var i = 0; keys.Count < count; ++i)
        {
            var key = new ShapedRunCache.Key("unique " + i.ToString(CultureInfo.InvariantCulture),
                face, 16, 0, CultureInfo.InvariantCulture.Name, CultureInfo.InvariantCulture.LCID, 0, 0);
            if (slots.Add(key.GetHashCode() & (ShapedRunCache.AdmissionSlots - 1))) keys.Add(key);
        }
        return keys.ToArray();
    }

    private static IDisposable Start(HarfBuzzTextShaper shaper) =>
        UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));

    private static GlyphTypeface CreateFont()
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new CustomPlatformTypeface(stream));
    }
}
