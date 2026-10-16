using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Base.UnitTests.Media.Fonts.Tables;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.Utilities;
using Xunit;

namespace Avalonia.Base.UnitTests.Media.TextFormatting;

public class PairedGlyphStorageTests
{
    private static readonly FieldInfo GlyphReference = typeof(ShapedBuffer)
        .GetField("_glyphRef", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(257)]
    public void Parallel_Arrays_Have_One_Owner_And_Independent_Runs_Do_Not_Alias(int length)
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var text = new string('x', length).AsMemory();
        using var first = new ShapedBuffer(text, length, font, 16, 0);
        using var second = new ShapedBuffer(text, length, font, 16, 0);
        var storage = Storage(first);
        Assert.Equal(1, Reference(first).RefCount);
        Assert.Null(typeof(ShapedBuffer).GetField("_glyphIndicesRef", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotSame(storage, Storage(second));
        if (length > 0)
        {
            Assert.NotSame(storage.Array, Storage(second).Array);
            Assert.NotSame(storage.Indices, Storage(second).Indices);
        }
        for (var i = 0; i < length; ++i)
        {
            first.InitializeGlyph(i, new GlyphInfo((ushort)(i + 1), i, 10));
            second.InitializeGlyph(i, new GlyphInfo(500, i, 20));
        }
        first.Dispose();
        AssertReleased(storage);
        Assert.Equal(length, second.GlyphIndices.Length);
        for (var i = 0; i < length; ++i)
            Assert.Equal((ushort)500, second.GlyphIndices[i]);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void Split_Views_Retain_Both_Arrays_Until_The_Last_Release(bool rtl, int order)
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        using var parent = new ShapedBuffer("abcd".AsMemory(), 4, font, 16, (sbyte)(rtl ? 1 : 0));
        for (var i = 0; i < 4; ++i)
        {
            var cluster = rtl ? 3 - i : i;
            parent.InitializeGlyph(i, new GlyphInfo((ushort)(cluster + 1), cluster, (cluster + 1) * 10));
        }
        var storage = Storage(parent);
        var split = parent.Split(2);
        using var first = split.First!;
        using var second = split.Second!;
        Assert.Same(storage, Storage(first));
        Assert.Same(storage, Storage(second));
        Assert.Equal(3, Reference(parent).RefCount);
        Assert.Equal("ab", first.Text.ToString());
        Assert.Equal("cd", second.Text.ToString());
        Assert.Equal(30, first.TotalGlyphAdvance);
        Assert.Equal(70, second.TotalGlyphAdvance);
        Assert.Equal(rtl ? new ushort[] { 2, 1 } : new ushort[] { 1, 2 }, first.GlyphIndices.ToArray());
        Assert.Equal(rtl ? new ushort[] { 4, 3 } : new ushort[] { 3, 4 }, second.GlyphIndices.ToArray());

        var release = order switch
        {
            0 => new[] { parent, first, second },
            1 => new[] { first, parent, second },
            _ => new[] { first, second, parent },
        };
        release[0].Dispose();
        Assert.Equal(2, Reference(release[2]).RefCount);
        release[1].Dispose();
        Assert.Equal(1, Reference(release[2]).RefCount);
        Assert.NotEmpty(storage.Array);
        Assert.NotEmpty(storage.Indices);
        Assert.True(release[2].TotalGlyphAdvance > 0);
        release[2].Dispose();
        release[2].Dispose();
        AssertReleased(storage);
    }

    [Fact]
    public void Metadata_Replacement_And_Alias_Mutation_Preserve_Paired_Ownership()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var originalMetrics = new DefaultTextLineMetricsCache();
        var replacementMetrics = new DefaultTextLineMetricsCache();
        var snapshot = new[] { new GlyphInfo(1, 0, 10), new GlyphInfo(2, 1, 20) };
        using var parent = new ShapedBuffer("ab".AsMemory(), snapshot, font, 16, 0, originalMetrics);
        var storage = Storage(parent);
        using var alias = parent.WithBidiLevel(1);
        Assert.Null(alias.SharedMetrics);
        parent.AttachMetricsCache(replacementMetrics);
        Assert.Same(replacementMetrics, parent.SharedMetrics);
        Assert.Same(storage, Storage(parent));
        Assert.Equal(2, Reference(parent).RefCount);
        alias[0] = new GlyphInfo(77, 0, 50);
        Assert.Null(parent.SharedMetrics);
        Assert.Equal((ushort)77, parent.GlyphIndices[0]);
        Assert.Equal((ushort)1, snapshot[0].GlyphIndex);
        parent.Dispose();
        Assert.Equal((ushort)77, alias.GlyphIndices[0]);
        alias.Dispose();
        AssertReleased(storage);
    }

    [Fact]
    public void Distinct_Alias_References_Can_Be_Released_Concurrently()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        using var parent = new ShapedBuffer("x".AsMemory(), 1, font, 16, 0);
        parent.InitializeGlyph(0, new GlyphInfo(42, 0, 10));
        var storage = Storage(parent);
        var aliases = Enumerable.Range(0, 64).Select(_ => parent.WithBidiLevel(1)).ToArray();
        try
        {
            Assert.Equal(65, Reference(parent).RefCount);
            Parallel.ForEach(aliases, alias => alias.Dispose());
            Assert.Equal(1, Reference(parent).RefCount);
            Assert.Equal((ushort)42, parent.GlyphIndices[0]);
            parent.Dispose();
            AssertReleased(storage);
        }
        finally
        {
            foreach (var alias in aliases) alias.Dispose();
        }
    }

    [Fact]
    public void Writable_Copies_Are_Not_Owned_By_The_Pooled_Pair()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        using var parent = new ShapedBuffer("x".AsMemory(), 1, font, 16, 0);
        parent.InitializeGlyph(0, new GlyphInfo(42, 0, 10));
        using var copy = parent.CloneWritable();
        var storage = Storage(parent);
        parent.Dispose();
        AssertReleased(storage);
        Assert.Equal((ushort)42, copy.GlyphIndices[0]);
        copy[0] = new GlyphInfo(43, 0, 20);
        Assert.Equal((ushort)43, copy.GlyphIndices[0]);
        Assert.Equal(20, copy.TotalGlyphAdvance);
    }

    [Fact]
    public void Abandoned_References_Still_Return_Both_Arrays()
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var storage = AbandonBuffer(font);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        AssertReleased(storage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cold_And_Metadata_Buffers_Allocate_Less_Than_The_Previous_Two_Owner_Construction(bool metadata)
    {
        var font = CreateFont();
        using var lifetime = Disposable.Create(font.Dispose);
        var text = "ab".AsMemory();
        var snapshot = new[] { new GlyphInfo(1, 0, 10), new GlyphInfo(2, 1, 20) };
        var metrics = new DefaultTextLineMetricsCache();
        Action previous = () => ConstructWithSeparateOwners(text, font);
        Action current = () =>
        {
            using var buffer = metadata ? new ShapedBuffer(text, snapshot, font, 16, 0, metrics) :
                new ShapedBuffer(text, text.Length, font, 16, 0);
            GC.KeepAlive(buffer);
        };
        for (var i = 0; i < 256; ++i) { previous(); current(); }
        var before = Measure(previous);
        var after = Measure(current);
        Assert.True(after <= before - 512 * 4 * IntPtr.Size,
            $"Two independent owners: {before}; paired owner (metadata={metadata}): {after}");
    }

    private static long Measure(Action action)
    {
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 512; ++i) action();
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConstructWithSeparateOwners(ReadOnlyMemory<char> text, GlyphTypeface font)
    {
        // Reconstruct the preceding allocation topology; no synthetic bytes are added.
        // The caller-storage constructor has the new (smaller) ShapedBuffer size, so this
        // comparison conservatively excludes the eliminated reference field's saving.
        using var glyphs = RefCountable.Create(new ShapedBuffer.PooledArray<GlyphInfo>(text.Length));
        using var indices = RefCountable.Create(new ShapedBuffer.PooledArray<ushort>(text.Length));
        using var buffer = new ShapedBuffer(text,
            new ArraySlice<GlyphInfo>(glyphs.Item.Array, 0, text.Length),
            new ArraySlice<ushort>(indices.Item.Array, 0, text.Length), font, 16, 0);
        GC.KeepAlive(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ShapedBuffer.PooledGlyphArray AbandonBuffer(GlyphTypeface font)
    {
        var buffer = new ShapedBuffer("ab".AsMemory(), 2, font, 16, 0);
        var storage = Storage(buffer);
        GC.KeepAlive(buffer);
        return storage;
    }

    private static IRef<ShapedBuffer.PooledArray<GlyphInfo>> Reference(ShapedBuffer buffer) =>
        (IRef<ShapedBuffer.PooledArray<GlyphInfo>>)GlyphReference.GetValue(buffer)!;

    private static ShapedBuffer.PooledGlyphArray Storage(ShapedBuffer buffer) =>
        Assert.IsType<ShapedBuffer.PooledGlyphArray>(Reference(buffer).Item);

    private static void AssertReleased(ShapedBuffer.PooledGlyphArray storage)
    {
        Assert.Throws<ObjectDisposedException>(() => storage.Array);
        Assert.Throws<ObjectDisposedException>(() => storage.Indices);
    }

    private static GlyphTypeface CreateFont()
    {
        using var stream = new StandardAssetLoader().Open(new Uri(
            "resm:Avalonia.Base.UnitTests.Assets.Inter-Regular.ttf?assembly=Avalonia.Base.UnitTests"));
        return new GlyphTypeface(new CustomPlatformTypeface(stream));
    }
}
