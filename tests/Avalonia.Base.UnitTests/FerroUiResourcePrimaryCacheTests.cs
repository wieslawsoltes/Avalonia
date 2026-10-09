using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourcePrimaryCacheTests
{
    [Fact]
    public void Revalidated_Primary_Is_Flushed_Before_Switching_Keys()
    {
        var cache = new ResourceLookupCache();
        var a = new ResourceDictionary();
        var b = new ResourceDictionary();
        cache.Add("one", null, a, ResourceLookupCache.Epoch);
        ResourceLookupCache.Invalidate();
        Assert.False(cache.TryGet("one", null, out _));
        cache.Add("one", null, b, ResourceLookupCache.Epoch);
        cache.Add("two", null, a, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet(new string("one".ToCharArray()), null, out var actual));
        Assert.Same(b, actual);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Alternating_Found_And_Missing_States_Do_Not_Resurrect_Old_Locations()
    {
        var cache = new ResourceLookupCache();
        var location = new ResourceDictionary();
        cache.Add("key", null, location, ResourceLookupCache.Epoch);
        for (var i = 0; i < 32; ++i)
        {
            ResourceLookupCache.Invalidate();
            cache.Add("key", null, null, ResourceLookupCache.Epoch);
            cache.Add("other", null, location, ResourceLookupCache.Epoch);
            Assert.True(cache.TryGet("key", null, out var missing));
            Assert.Null(missing);
            cache.Add("key", null, location, ResourceLookupCache.Epoch);
            Assert.True(cache.TryGet("key", null, out var found));
            Assert.Same(location, found);
        }
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Variant_Identity_And_Entry_Epoch_Are_Checked_Independently()
    {
        var cache = new ResourceLookupCache();
        var light = new ResourceDictionary();
        var dark = new ResourceDictionary();
        cache.Add("key", ThemeVariant.Light, light, ResourceLookupCache.Epoch);
        cache.Add("key", ThemeVariant.Dark, dark, ResourceLookupCache.Epoch);
        ResourceLookupCache.Invalidate();
        cache.Add("key", ThemeVariant.Dark, dark, ResourceLookupCache.Epoch);
        Assert.False(cache.TryGet("key", ThemeVariant.Light, out _));
        Assert.True(cache.TryGet("key", ThemeVariant.Dark, out var found));
        Assert.Same(dark, found);
    }

    [Fact]
    public void Capacity_Clear_Cannot_Leave_A_Primary_Outside_The_Bound()
    {
        var cache = new ResourceLookupCache();
        var location = new ResourceDictionary();
        cache.Add("first", null, location, ResourceLookupCache.Epoch);
        for (var i = 0; i < ResourceLookupCache.Capacity; ++i)
            cache.Add("key" + i, null, location, ResourceLookupCache.Epoch);
        Assert.False(cache.TryGet("first", null, out _));
        Assert.InRange(cache.Count, 1, ResourceLookupCache.Capacity);
        cache.Add("key127", null, null, ResourceLookupCache.Epoch);
        cache.Add(typeof(Button), null, location, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet("key127", null, out var missing));
        Assert.Null(missing);
        Assert.True(cache.TryGet(typeof(Button), null, out var found));
        Assert.Same(location, found);
    }

    [Fact]
    public void Repeated_Inline_Revalidation_Allocates_No_New_Handles()
    {
        var cache = new ResourceLookupCache();
        var location = new ResourceDictionary();
        cache.Add("key", null, location, ResourceLookupCache.Epoch);
        void Cycle()
        {
            ResourceLookupCache.Invalidate();
            cache.Add("key", null, null, ResourceLookupCache.Epoch);
            ResourceLookupCache.Invalidate();
            cache.Add("key", null, location, ResourceLookupCache.Epoch);
        }
        for (var i = 0; i < 64; ++i) Cycle();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; ++i) Cycle();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void A_Dirty_Primary_Does_Not_Retain_Its_Dictionary()
    {
        var cache = new ResourceLookupCache();
        var weak = Populate(cache);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.False(cache.TryGet("key", null, out _));
        Assert.Equal(0, cache.Count);
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Populate(ResourceLookupCache cache)
    {
        var location = new ResourceDictionary();
        cache.Add("key", null, location, ResourceLookupCache.Epoch);
        ResourceLookupCache.Invalidate();
        cache.Add("key", null, location, ResourceLookupCache.Epoch);
        return new WeakReference(location);
    }
}
