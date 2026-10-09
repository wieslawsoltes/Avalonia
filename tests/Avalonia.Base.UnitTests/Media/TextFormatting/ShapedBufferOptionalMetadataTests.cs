using System;
using System.Reflection;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.Utilities;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class ShapedBufferOptionalMetadataTests
{
    [Fact]
    public void Ordinary_Buffers_Have_No_PerInstance_Metrics_Fields()
    {
        foreach (var field in typeof(ShapedBuffer).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            Assert.DoesNotContain("sharedMetrics", field.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cold_Buffers_Do_Not_Pay_For_Optional_Metadata()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var glyphs = new[] { new GlyphInfo(1, 0, 10), new GlyphInfo(2, 1, 20) };
        var cache = new DefaultTextLineMetricsCache();
        long Measure(bool cached)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 256; ++i)
            {
                using var buffer = cached ? new ShapedBuffer("ab".AsMemory(), glyphs, font, 16, 0, cache) :
                    new ShapedBuffer("ab".AsMemory(), 2, font, 16, 0);
                GC.KeepAlive(buffer);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Measure(false);
        Measure(true);
        var cold = Measure(false);
        var cached = Measure(true);
        Assert.True(cached >= cold + 256 * IntPtr.Size, $"Cold {cold}, metadata {cached}");
    }

    [Fact]
    public void Initialization_Produces_Indices_And_Mutation_Still_Invalidates_Aliases()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        using var buffer = new ShapedBuffer("ab".AsMemory(), 2, font, 16, 0);
        buffer.InitializeGlyph(0, new GlyphInfo(1, 0, 10));
        buffer.InitializeGlyph(1, new GlyphInfo(2, 1, 20));
        Assert.Equal(new ushort[] { 1, 2 }, buffer.GlyphIndices.ToArray());
        Assert.Equal(30, buffer.TotalGlyphAdvance);
        var cache = new DefaultTextLineMetricsCache();
        buffer.AttachMetricsCache(cache);
        Assert.Same(cache, buffer.SharedMetrics);
        using var alias = buffer.WithBidiLevel(1);
        // A different view never inherits cached whole-line metrics.
        Assert.Null(alias.SharedMetrics);
        alias[0] = new GlyphInfo(3, 0, 50);
        Assert.Null(buffer.SharedMetrics);
        Assert.Equal(3, buffer.GlyphIndices[0]);
        Assert.Equal(70, buffer.TotalGlyphAdvance);
        buffer.Dispose();
        Assert.Equal(2, alias.Length);
        Assert.Equal(3, alias.GlyphIndices[0]);
    }

    [Fact]
    public void Cache_Hit_Storage_Is_Independent_And_Metadata_Can_Be_Replaced()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var glyphs = new[] { new GlyphInfo(1, 0, 10) };
        var first = new DefaultTextLineMetricsCache();
        var second = new DefaultTextLineMetricsCache();
        using var a = new ShapedBuffer("a".AsMemory(), glyphs, font, 16, 0, first);
        using var b = new ShapedBuffer("a".AsMemory(), glyphs, font, 16, 0, first);
        a.AttachMetricsCache(second);
        a[0] = new GlyphInfo(3, 0, 99);
        Assert.Null(a.SharedMetrics);
        Assert.Same(first, b.SharedMetrics);
        Assert.Equal(1, b.GlyphIndices[0]);
        Assert.Equal(1, glyphs[0].GlyphIndex);
        a.Dispose();
        Assert.Same(first, b.SharedMetrics);
    }

    [Fact]
    public void Stateful_References_Preserve_Ownership_And_Do_Not_Copy_View_State()
    {
        var item = new DisposableItem();
        var original = RefCountable.Create(item);
        var stateful = RefCountable.CloneWithState(original, "metadata");
        Assert.Equal(2, original.RefCount);
        Assert.Equal("metadata", ((IRefWithState<string>)stateful).State);
        var clone = stateful.Clone();
        var cast = stateful.CloneAs<object>();
        Assert.False(clone is IRefWithState<string>);
        Assert.False(cast is IRefWithState<string>);
        original.Dispose();
        stateful.Dispose();
        stateful.Dispose();
        Assert.Equal(0, item.Disposals);
        clone.Dispose();
        Assert.Equal(0, item.Disposals);
        cast.Dispose();
        Assert.Equal(1, item.Disposals);
        Assert.Throws<ObjectDisposedException>(() => RefCountable.CloneWithState(stateful, 1));
    }

    private static GlyphTypeface CreateFont()
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new CustomPlatformTypeface(stream));
    }

    private sealed class DisposableItem : IDisposable
    {
        public int Disposals;
        public void Dispose() => ++Disposals;
    }
}
