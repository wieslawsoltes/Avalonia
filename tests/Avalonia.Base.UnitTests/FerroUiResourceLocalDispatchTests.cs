using System;
using Avalonia.Controls;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceLocalDispatchTests
{
    [Fact]
    public void Found_Null_Does_Not_Enter_NonLocal_Resolver()
    {
        var built = 0;
        var child = new ResourceDictionary();
        child.AddDeferred("key", _ => { ++built; return "child"; });
        var root = new ResourceDictionary { ["key"] = null, MergedDictionaries = { child } };
        for (var i = 0; i < 5; ++i)
        {
            Assert.True(root.TryGetResource("key", null, out var value));
            Assert.Null(value);
        }
        Assert.Equal(0, built);
        root.Remove("key");
        Assert.True(root.TryGetResource("key", null, out var fallback));
        Assert.Equal("child", fallback);
        Assert.Equal(1, built);
    }

    [Fact]
    public void Local_Comparer_Can_Add_Children_Before_NonLocal_Dispatch()
    {
        var root = new ResourceDictionary();
        var child = new ResourceDictionary { ["key"] = "child" };
        var added = false;
        root.Add(new CallbackKey("key", () =>
        {
            if (!added) { added = true; root.MergedDictionaries.Add(child); }
        }), "unrelated");
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("child", value);
        Assert.True(added);
        child["key"] = "updated";
        Assert.True(root.TryGetResource("key", null, out value));
        Assert.Equal("updated", value);
    }

    [Fact]
    public void Local_Setter_Keeps_A_Parent_Location_Live()
    {
        var leaf = new ResourceDictionary { ["key"] = "first" };
        var parent = new ResourceDictionary { MergedDictionaries = { leaf } };
        for (var i = 0; i < 5; ++i) Assert.True(parent.TryGetResource("key", null, out _));
        leaf["key"] = null;
        Assert.True(parent.TryGetResource("key", null, out var value));
        Assert.Null(value);
        leaf["key"] = "last";
        Assert.True(parent.TryGetResource("key", null, out value));
        Assert.Equal("last", value);
    }

    private sealed class CallbackKey(string key, Action callback)
    {
        public override int GetHashCode() => key.GetHashCode();
        public override bool Equals(object? obj) { callback(); return ReferenceEquals(this, obj); }
    }
}
