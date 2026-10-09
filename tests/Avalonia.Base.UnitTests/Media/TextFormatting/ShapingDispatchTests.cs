using System;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class ShapingDispatchTests
{
    [Theory]
    [InlineData("before office after", 7, 6, 0)]
    [InlineData("[a\tb\r\n]", 1, 5, 0)]
    [InlineData("xمرحباy", 2, 3, 1)]
    public void Alternating_Cached_And_Context_Runs_Preserves_Glyphs_And_Ownership(
        string source, int start, int length, int bidi)
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        var font = new GlyphTypeface(new CustomPlatformTypeface(stream));
        using var lifetime = Disposable.Create(font.Dispose);
        var options = new TextShaperOptions(font, 16, bidiLevel: (sbyte)bidi, incrementalTabWidth: 32, letterSpacing: 0.5);
        var context = source.AsMemory(start, length);
        var label = context.ToString();
        using var expectedContext = shaper.ShapeText(source.ToCharArray().AsMemory(start, length), options);
        using var expectedLabel = shaper.ShapeText(label.ToCharArray().AsMemory(), options);
        for (var pass = 0; pass < 20; ++pass)
        {
            using var cached = shaper.ShapeText(label.AsMemory(), options);
            using var uncached = shaper.ShapeText(context, options);
            Assert.Equal(expectedLabel.Length, cached.Length);
            Assert.Equal(expectedContext.Length, uncached.Length);
            Assert.Equal(context, uncached.Text);
            for (var i = 0; i < cached.Length; ++i) Assert.Equal(expectedLabel[i], cached[i]);
            for (var i = 0; i < uncached.Length; ++i) Assert.Equal(expectedContext[i], uncached[i]);
            // A caller may mutate its independent result; neither route can share this storage.
            if (cached.Length > 0) cached[0] = default;
            if (uncached.Length > 0) uncached[0] = default;
        }
    }
}
