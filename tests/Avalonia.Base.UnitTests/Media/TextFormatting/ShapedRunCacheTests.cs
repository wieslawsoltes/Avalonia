using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class ShapedRunCacheTests
{
    [Theory]
    [InlineData("Hello office", 0)]
    [InlineData("abc\tdef", 0)]
    [InlineData("a\u0301b\r\n", 0)]
    [InlineData("مرحبا", 1)]
    [InlineData("אבג 123", 1)]
    public void Cached_Glyphs_Exactly_Match_The_Uncached_Path(string text, int bidi)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16, (sbyte)bidi);
        using var reference = shaper.ShapeText(text.ToCharArray().AsMemory(), options);
        using var first = shaper.ShapeText(text.AsMemory(), options);
        Assert.Equal(0, shaper.ShapedRunCache.Count);
        using var admitted = shaper.ShapeText(text.AsMemory(), options);
        using var cached = shaper.ShapeText(text.AsMemory(), options);
        Assert.Equal(1, shaper.ShapedRunCache.Count);
        Assert.Equal(Snapshot(reference), Snapshot(first));
        Assert.Equal(Snapshot(reference), Snapshot(admitted));
        Assert.Equal(Snapshot(reference), Snapshot(cached));
        Assert.NotSame(admitted, cached);
    }

    [Fact]
    public void Returned_Buffers_Can_Be_Mutated_And_Disposed_Independently()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16);
        var first = shaper.ShapeText("cached".AsMemory(), options);
        var expected = Snapshot(first);
        first[0] = new GlyphInfo(0, 999, 999);
        first.Dispose();
        using var second = shaper.ShapeText("cached".AsMemory(), options);
        using var third = shaper.ShapeText("cached".AsMemory(), options);
        second[0] = new GlyphInfo(0, 123, 123);
        Assert.Equal(expected, Snapshot(third));
    }

    [Fact]
    public void All_Eligible_Options_And_Typeface_Identity_Are_In_The_Key()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var otherFont = CreateFont();
        using var otherFontLifetime = Disposable.Create(otherFont.Dispose);
        var options = new[]
        {
            new TextShaperOptions(font, 16),
            new TextShaperOptions(font, 20),
            new TextShaperOptions(font, 16, bidiLevel: 1),
            new TextShaperOptions(font, 16, culture: CultureInfo.GetCultureInfo("tr-TR")),
            new TextShaperOptions(font, 16, incrementalTabWidth: 50),
            new TextShaperOptions(font, 16, letterSpacing: 2),
            new TextShaperOptions(otherFont, 16),
        };
        foreach (var option in options)
        {
            using var reference = shaper.ShapeText("fi\ti".ToCharArray().AsMemory(), option);
            using var first = shaper.ShapeText("fi\ti".AsMemory(), option);
            using var admitted = shaper.ShapeText("fi\ti".AsMemory(), option);
            using var cached = shaper.ShapeText("fi\ti".AsMemory(), option);
            Assert.Equal(Snapshot(reference), Snapshot(cached));
        }
        Assert.Equal(options.Length, shaper.ShapedRunCache.Count);
    }

    [Fact]
    public void Current_Culture_Is_Resolved_Before_Lookup()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                using var first = shaper.ShapeText("fi".AsMemory(), new TextShaperOptions(font));
                using var admitted = shaper.ShapeText("fi".AsMemory(), new TextShaperOptions(font));
            }
            Assert.Equal(2, shaper.ShapedRunCache.Count);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Slices_Mutable_Memory_And_Features_Do_Not_Enter_The_Cache()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font);
        for (var i = 0; i < 3; ++i)
        {
            using var sliced = shaper.ShapeText("before office after".AsMemory(7, 6), options);
            using var mutable = shaper.ShapeText("office".ToCharArray().AsMemory(), options);
            using var featured = shaper.ShapeText("office".AsMemory(), new TextShaperOptions(font,
                fontFeatures: new[] { FontFeature.Parse("liga=0") }));
            using var longText = shaper.ShapeText(new string('a', ShapedRunCache.MaxTextLength + 1).AsMemory(), options);
        }
        Assert.Equal(0, shaper.ShapedRunCache.Count);
    }

    [Fact]
    public void Cache_Is_Bounded_And_Uses_Least_Recently_Used_Eviction()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font);
        void Admit(string text)
        {
            shaper.ShapeText(text.AsMemory(), options).Dispose();
            shaper.ShapeText(text.AsMemory(), options).Dispose();
        }
        for (var i = 0; i < ShapedRunCache.MaxEntries; ++i)
            Admit(i.ToString(CultureInfo.InvariantCulture));
        shaper.ShapeText("0".AsMemory(), options).Dispose();
        Admit("new");
        var face = (HarfBuzzTypeface)font.TextShaperTypeface;
        Assert.True(ShapedRunCache.TryCreateKey("0".AsMemory(), options, CultureInfo.CurrentCulture, face.CacheId, out var recent));
        Assert.True(ShapedRunCache.TryCreateKey("1".AsMemory(), options, CultureInfo.CurrentCulture, face.CacheId, out var old));
        Assert.True(shaper.ShapedRunCache.TryGet(recent, "0".AsMemory(), options, out var buffer));
        buffer!.Dispose();
        Assert.False(shaper.ShapedRunCache.TryGet(old, "1".AsMemory(), options, out _));
        for (var i = 0; i < 500; ++i)
            Admit(new string('x', 100) + i);
        Assert.InRange(shaper.ShapedRunCache.Count, 1, ShapedRunCache.MaxEntries);
        Assert.InRange(shaper.ShapedRunCache.RetainedBytes, 1, ShapedRunCache.MaxRetainedBytes);
    }

    [Fact]
    public void Cold_Admissions_Allocate_No_Snapshots_And_Do_Not_Retain_Strings()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font);
        using var source = shaper.ShapeText("probe".ToCharArray().AsMemory(), options);
        var cache = new ShapedRunCache();
        var keys = new List<ShapedRunCache.Key>();
        var hashes = new HashSet<int>();
        // Avoid probabilistic admission collisions so the allocation assertion is deterministic.
        for (var i = 0; keys.Count < 4096; ++i)
        {
            var key = new ShapedRunCache.Key("unique " + i, 1, 12, 0, "en-US", 1033, 0, 0);
            if (hashes.Add(key.GetHashCode()))
                keys.Add(key);
        }
        var warm = new ShapedRunCache();
        warm.Add(keys[0], source);
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var key in keys)
            cache.Add(key, source);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(0, cache.Count);
        Assert.Equal(ShapedRunCache.AdmissionRetainedBytes, cache.RetainedBytes);
    }

    [Fact]
    public void One_Off_Scan_Does_Not_Evict_A_Hot_Run()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font);
        using var first = shaper.ShapeText("hot".AsMemory(), options);
        using var admitted = shaper.ShapeText("hot".AsMemory(), options);
        var face = (HarfBuzzTypeface)font.TextShaperTypeface;
        Assert.True(ShapedRunCache.TryCreateKey("hot".AsMemory(), options, CultureInfo.CurrentCulture, face.CacheId, out var hot));
        var hashes = new HashSet<int> { hot.GetHashCode() };
        for (var i = 0; i < 4096; ++i)
        {
            var text = "one off " + i;
            Assert.True(ShapedRunCache.TryCreateKey(text.AsMemory(), options, CultureInfo.CurrentCulture, face.CacheId, out var key));
            if (hashes.Add(key.GetHashCode()))
                shaper.ShapeText(text.AsMemory(), options).Dispose();
        }
        Assert.Equal(1, shaper.ShapedRunCache.Count);
        Assert.True(shaper.ShapedRunCache.TryGet(hot, "hot".AsMemory(), options, out var cached));
        using (cached)
            Assert.Equal(Snapshot(admitted), Snapshot(cached!));
    }

    [Fact]
    public void Immutable_Snapshots_Support_Concurrent_Reads()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = Start(shaper);
        var font = CreateFont();
        using var fontLifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font);
        using var first = shaper.ShapeText("parallel".AsMemory(), options);
        using var admitted = shaper.ShapeText("parallel".AsMemory(), options);
        var expected = Snapshot(first);
        Parallel.For(0, 64, _ =>
        {
            using var result = shaper.ShapeText("parallel".AsMemory(), options);
            Assert.Equal(expected, Snapshot(result));
        });
        Assert.Equal(1, shaper.ShapedRunCache.Count);
    }

    private static GlyphInfo[] Snapshot(ShapedBuffer buffer) =>
        Enumerable.Range(0, buffer.Length).Select(i => buffer[i]).ToArray();

    private static IDisposable Start(HarfBuzzTextShaper shaper) =>
        UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));

    private static GlyphTypeface CreateFont()
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new CustomPlatformTypeface(stream));
    }
}
