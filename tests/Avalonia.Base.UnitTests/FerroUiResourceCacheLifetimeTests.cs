using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceCacheLifetimeTests
{
    [Fact]
    public void An_Unused_Cache_Does_Not_Keep_A_Removed_Resource_Graph_Alive()
    {
        var cache = new ResourceLookupCache();
        var reference = Populate(cache);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        Assert.False(cache.TryGet("key", null, out _));
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Populate(ResourceLookupCache cache)
    {
        var dictionary = new ResourceDictionary { ["key"] = new byte[4096] };
        cache.Add("key", null, dictionary, ResourceLookupCache.Epoch);
        Assert.True(cache.TryGet("key", null, out var found));
        Assert.Same(dictionary, found);
        return new WeakReference(dictionary);
    }
}
