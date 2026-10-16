using System;
using System.Globalization;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class AdaptiveShapingProbeTests
{
    [Fact]
    public void Cold_Scan_Skips_Probes_Then_Recovers_To_A_Hot_Run()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        var font = new GlyphTypeface(new CustomPlatformTypeface(stream));
        try
        {
            var options = new TextShaperOptions(font, 16);
            shaper.ShapeText("hot".AsMemory(), options).Dispose();
            shaper.ShapeText("hot".AsMemory(), options).Dispose();
            for (var i = 0; i < 512; ++i)
                shaper.ShapeText(("unique scan " + i.ToString(CultureInfo.InvariantCulture)).AsMemory(), options).Dispose();
            var skipped = 0;
            for (var i = 0; i < ShapedRunCache.ProbeInterval; ++i)
                if (!shaper.ShapedRunCache.ShouldProbe()) ++skipped;
            Assert.True(skipped > 0);
            for (var i = 0; i < ShapedRunCache.ProbeInterval * 2; ++i)
                shaper.ShapeText("hot".AsMemory(), options).Dispose();
            Assert.True(shaper.ShapedRunCache.ShouldProbe());
            using var hit = shaper.ShapeText("hot".AsMemory(), options);
            using var reference = shaper.ShapeText("hot".ToCharArray().AsMemory(), options);
            Assert.Equal(reference.Length, hit.Length);
            for (var i = 0; i < hit.Length; ++i) Assert.Equal(reference[i], hit[i]);
        }
        finally { font.Dispose(); }
    }
}
