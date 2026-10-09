using Avalonia.Controls;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiLocalResourceFastPathTests
{
    [Fact]
    public void Standalone_Leaf_Misses_Do_Not_Establish_A_Global_Cache_Dependency()
    {
        var dictionary = new ResourceDictionary();
        var epoch = ResourceLookupCache.Epoch;
        Assert.False(dictionary.TryGetResource("missing", null, out _));
        dictionary["missing"] = "local";
        Assert.Equal(epoch, ResourceLookupCache.Epoch);
        Assert.True(dictionary.TryGetResource("missing", null, out var value));
        Assert.Equal("local", value);
    }

    [Fact]
    public void A_Previously_Standalone_Leaf_Is_Tracked_When_Later_Visited_By_A_Parent()
    {
        var leaf = new ResourceDictionary();
        Assert.False(leaf.TryGetResource("key", null, out _));
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.False(root.TryGetResource("key", null, out _));
        var epoch = ResourceLookupCache.Epoch;
        leaf["key"] = "added";
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("added", value);
        leaf.Remove("key");
        Assert.False(root.TryGetResource("key", null, out _));
    }

    [Fact]
    public void Children_Added_After_A_Standalone_Miss_Are_Still_Resolved()
    {
        var root = new ResourceDictionary();
        Assert.False(root.TryGetResource("key", null, out _));
        var child = new ResourceDictionary { ["key"] = "first" };
        root.MergedDictionaries.Add(child);
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("first", value);
        child["key"] = "second";
        Assert.True(root.TryGetResource("key", null, out value));
        Assert.Equal("second", value);
    }
}
