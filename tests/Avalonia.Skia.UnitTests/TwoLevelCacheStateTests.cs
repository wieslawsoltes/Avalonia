using System;
using System.Collections.Generic;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public class TwoLevelCacheStateTests
{
    [Fact]
    public void State_Factory_Runs_Only_On_Miss_In_Both_Levels()
    {
        var cache = new TwoLevelCache<int, object>();
        var a = new object();
        var b = new object();
        Assert.Same(a, cache.GetOrAdd(1, a, static (_, state) => state));
        Assert.Same(b, cache.GetOrAdd(2, b, static (_, state) => state));
        Assert.Same(a, cache.GetOrAdd(1, 0, static (_, _) => throw new Exception()));
        Assert.Same(b, cache.GetOrAdd(2, 0, static (_, _) => throw new Exception()));
    }

    [Fact]
    public void State_Factory_Preserves_Eviction_And_Disposal()
    {
        var disposed = new List<object?>();
        var cache = new TwoLevelCache<int, object>(1, disposed.Add);
        var a = new object();
        var b = new object();
        var c = new object();
        cache.GetOrAdd(1, a, static (_, state) => state);
        cache.GetOrAdd(2, b, static (_, state) => state);
        cache.GetOrAdd(3, c, static (_, state) => state);
        Assert.Equal(new[] { b }, disposed);
        cache.ClearAndDispose();
        Assert.Equal(new[] { b, a, c }, disposed);
    }

    [Fact]
    public void Throwing_Factory_Does_Not_Change_The_Cache()
    {
        var cache = new TwoLevelCache<int, object>();
        Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd(1, 0,
            static (_, _) => throw new InvalidOperationException()));
        Assert.False(cache.TryGet(1, out _));
        var value = new object();
        Assert.Same(value, cache.GetOrAdd(1, value, static (_, state) => state));
    }

    [Fact]
    public void Warm_State_Factory_Hits_Do_Not_Allocate()
    {
        var cache = new TwoLevelCache<int, object>();
        var value = new object();
        Func<int, object, object> factory = static (_, state) => state;
        for (var i = 0; i < 100; ++i)
            cache.GetOrAdd(i % 2, value, factory);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; ++i)
            cache.GetOrAdd(i % 2, value, factory);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
