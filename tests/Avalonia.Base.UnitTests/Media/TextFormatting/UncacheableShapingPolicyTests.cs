using System;
using System.Buffers;
using System.Reflection;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class UncacheableShapingPolicyTests
{
    [Theory]
    [InlineData(0)] // Complete but over-length string.
    [InlineData(1)] // String slice with surrounding text.
    [InlineData(2)] // Array slice with surrounding text.
    [InlineData(3)] // MemoryManager-backed slice.
    [InlineData(4)] // Explicit OpenType features.
    public void Ineligible_Runs_Do_Not_Advance_Cold_Cache_State(int kind)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        using var manager = new Characters("[office]".ToCharArray());
        var options = kind == 4
            ? new TextShaperOptions(font, 16, fontFeatures: new[] { FontFeature.Parse("liga=0") })
            : new TextShaperOptions(font, 16);
        var input = kind switch
        {
            0 => new string('a', ShapedRunCache.MaxTextLength + 1).AsMemory(),
            1 => "[office]".AsMemory(1, 6),
            2 => "[office]".ToCharArray().AsMemory(1, 6),
            3 => manager.Memory.Slice(1, 6),
            _ => "office".AsMemory()
        };
        var cache = shaper.ShapedRunCache;
        var missing = new ShapedRunCache.Key("missing", 1, 16, 0, "", 0, 0, 0);
        for (var i = 0; i < ShapedRunCache.ProbeInterval; ++i)
            Assert.False(cache.TryGet(missing, missing.Text.AsMemory(), options, out _));
        var before = State(cache);
        using var expected = shaper.ShapeText(input, options);
        for (var pass = 0; pass < ShapedRunCache.ProbeInterval * 2; ++pass)
        {
            using var actual = shaper.ShapeText(input, options);
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < actual.Length; ++i) Assert.Equal(expected[i], actual[i]);
            Assert.Equal(input, actual.Text);
        }
        Assert.Equal(before, State(cache));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Context_Slices_Match_Arrays_And_MemoryManagers_With_The_Same_Context()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        const string text = "xمرحباy";
        using var manager = new Characters(text.ToCharArray());
        var options = new TextShaperOptions(font, 16, bidiLevel: 1);
        using var expected = shaper.ShapeText(text.AsMemory(2, 3), options);
        using var array = shaper.ShapeText(text.ToCharArray().AsMemory(2, 3), options);
        using var managed = shaper.ShapeText(manager.Memory.Slice(2, 3), options);
        Assert.Equal(expected.Length, array.Length);
        Assert.Equal(expected.Length, managed.Length);
        for (var i = 0; i < expected.Length; ++i)
        {
            Assert.Equal(expected[i], array[i]);
            Assert.Equal(expected[i], managed[i]);
        }
    }

    [Fact]
    public void Ineligible_Text_Still_Rejects_A_Disposed_Typeface()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16);
        shaper.ShapeText("[office]".AsMemory(1, 6), options).Dispose();
        font.Dispose();
        Assert.Throws<ObjectDisposedException>(() => shaper.ShapeText("[office]".AsMemory(1, 6), options));
    }

    private static int[] State(ShapedRunCache cache)
    {
        var fields = new[] { "_admissionClock", "_probeCountdown", "_sampledProbes", "_recoveryProbesRemaining", "_missesWithoutBenefit" };
        var result = new int[fields.Length];
        for (var i = 0; i < fields.Length; ++i)
            result[i] = (int)typeof(ShapedRunCache).GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
        return result;
    }

    private static GlyphTypeface CreateFont()
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new CustomPlatformTypeface(stream));
    }

    private sealed class Characters(char[] values) : MemoryManager<char>
    {
        public override Span<char> GetSpan() => values;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
