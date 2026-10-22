using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceMutationAllocationTests
{
    [Fact]
    public void Revalidating_A_Warm_Key_Does_Not_Allocate_Another_Weak_Handle()
    {
        var cache = new ResourceLookupCache();
        var location = new ResourceDictionary();
        for (var i = 0; i < 16; ++i)
        {
            ResourceLookupCache.Invalidate();
            cache.TryGet("key", null, out _);
            cache.Add("key", null, location, ResourceLookupCache.Epoch);
        }
        var misses = 0;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; ++i)
        {
            ResourceLookupCache.Invalidate();
            if (!cache.TryGet("key", null, out _)) ++misses;
            cache.Add("key", null, location, ResourceLookupCache.Epoch);
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(1000, misses);
        Assert.Equal(0, allocated);
        Assert.True(cache.TryGet("key", null, out var found));
        Assert.Same(location, found);
    }

    [Fact]
    public void Revalidating_One_Entry_Does_Not_Make_Other_Entries_Current()
    {
        var cache = new ResourceLookupCache();
        var oldLocation = new ResourceDictionary();
        var newLocation = new ResourceDictionary();
        cache.Add("key", ThemeVariant.Light, oldLocation, ResourceLookupCache.Epoch);
        cache.Add("key", ThemeVariant.Dark, oldLocation, ResourceLookupCache.Epoch);
        ResourceLookupCache.Invalidate();
        cache.Add("key", ThemeVariant.Light, newLocation, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet("key", ThemeVariant.Light, out var current));
        Assert.Same(newLocation, current);
        Assert.False(cache.TryGet("key", ThemeVariant.Dark, out _));
        cache.Add("key", ThemeVariant.Dark, null, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet("key", ThemeVariant.Dark, out var missing));
        Assert.Null(missing);
        ResourceLookupCache.Invalidate();
        cache.Add("key", ThemeVariant.Dark, newLocation, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet("key", ThemeVariant.Dark, out current));
        Assert.Same(newLocation, current);
    }

    [Fact]
    public void Results_From_An_Old_Generation_Cannot_Be_Published()
    {
        var cache = new ResourceLookupCache();
        var generation = ResourceLookupCache.Epoch;
        ResourceLookupCache.Invalidate();
        cache.Add("key", null, new ResourceDictionary(), generation);
        Assert.False(cache.TryGet("key", null, out _));
        Assert.Equal(0, cache.Count);
    }
}
