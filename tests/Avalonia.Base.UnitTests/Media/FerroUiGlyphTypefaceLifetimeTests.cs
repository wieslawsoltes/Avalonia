using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.UnitTests;
using Moq;
using Xunit;

namespace Avalonia.Base.UnitTests.Media;

public class FerroUiGlyphTypefaceLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shaping_Typeface_Is_Disposed_Before_Font_Memory_Exactly_Once(bool throws)
    {
        var events = new List<string>();
        var shapingTypeface = new Mock<ITextShaperTypeface>();
        shapingTypeface.Setup(x => x.Dispose()).Callback(() =>
        {
            events.Add("shaper");
            if (throws)
                throw new InvalidOperationException("dispose");
        });
        var shaper = new Mock<ITextShaperImpl>();
        shaper.Setup(x => x.CreateTypeface(It.IsAny<GlyphTypeface>())).Returns(shapingTypeface.Object);
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper.Object));
        var font = CreateFont(events);
        Assert.Same(shapingTypeface.Object, font.TextShaperTypeface);
        if (throws)
            Assert.Throws<InvalidOperationException>(font.Dispose);
        else
            font.Dispose();
        font.Dispose();
        Assert.Equal(new[] { "shaper", "platform" }, events);
        shaper.Verify(x => x.CreateTypeface(font), Times.Once);
        shapingTypeface.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void Disposing_An_Unshaped_Font_Does_Not_Create_A_Shaping_Typeface()
    {
        var events = new List<string>();
        var shaper = new Mock<ITextShaperImpl>(MockBehavior.Strict);
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper.Object));
        var font = CreateFont(events);
        font.Dispose();
        Assert.Equal(new[] { "platform" }, events);
        shaper.Verify(x => x.CreateTypeface(It.IsAny<GlyphTypeface>()), Times.Never);
    }

    [Fact]
    public void A_Cached_Run_Does_Not_Hide_Disposal_Of_Its_Font()
    {
        var shaper = new HarfBuzzTextShaper();
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface.With(textShaperImpl: shaper));
        var font = CreateFont(new List<string>());
        try
        {
            var options = new TextShaperOptions(font, 16);
            // First touch records admission, second installs the snapshot, third exercises a hit.
            shaper.ShapeText("cached".AsMemory(), options).Dispose();
            Assert.Equal(0, shaper.ShapedRunCache.Count);
            shaper.ShapeText("cached".AsMemory(), options).Dispose();
            shaper.ShapeText("cached".AsMemory(), options).Dispose();
            Assert.Equal(1, shaper.ShapedRunCache.Count);
            var face = (HarfBuzzTypeface)font.TextShaperTypeface;
            font.Dispose();
            Assert.True(face.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => shaper.ShapeText("cached".AsMemory(), options));
        }
        finally
        {
            font.Dispose();
        }
    }

    private static GlyphTypeface CreateFont(List<string> events)
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new TrackingTypeface(new CustomPlatformTypeface(stream), events));
    }

    private sealed class TrackingTypeface(IPlatformTypeface inner, List<string> events) : IPlatformTypeface
    {
        public string FamilyName => inner.FamilyName;
        public FontWeight Weight => inner.Weight;
        public FontStyle Style => inner.Style;
        public FontStretch Stretch => inner.Stretch;
        public FontSimulations FontSimulations => inner.FontSimulations;
        public bool TryGetStream([NotNullWhen(true)] out Stream? stream) => inner.TryGetStream(out stream);
        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table) => inner.TryGetTable(tag, out table);
        public void Dispose()
        {
            events.Add("platform");
            inner.Dispose();
        }
    }
}
