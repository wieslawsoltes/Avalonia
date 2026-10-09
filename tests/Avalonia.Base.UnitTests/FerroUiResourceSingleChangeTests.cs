using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceSingleChangeTests
{
    [Fact]
    public void A_Proven_Absent_Key_Can_Be_Restored_And_Removed_Without_A_Full_Search()
    {
        var (root, leaf, cache) = MissingWithCandidate();
        for (var i = 0; i < 32; ++i)
        {
            leaf["key"] = i;
            Assert.True(cache.TryGet("key", null, out var restored));
            Assert.Same(leaf, restored);
            Assert.True(root.TryGetResource("key", null, out var value));
            Assert.Equal(i, value);
            leaf.Remove("key");
            Assert.True(cache.TryGet("key", null, out var missing));
            Assert.Null(missing);
            Assert.False(root.TryGetResource("key", null, out _));
        }
    }

    [Fact]
    public void Removing_An_Unproven_Winner_Still_Searches_For_Lower_Priority_Values()
    {
        var low = new ResourceDictionary { ["key"] = "low" };
        var high = new ResourceDictionary { ["key"] = "high" };
        var root = new ResourceDictionary { MergedDictionaries = { low, high } };
        Assert.True(root.TryGetResource("key", null, out _));
        high.Remove("key");
        Assert.False(Cache(root).TryGet("key", null, out _));
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("low", value);
        high["key"] = "restored high";
        Assert.False(Cache(root).TryGet("key", null, out _));
        Assert.True(root.TryGetResource("key", null, out value));
        Assert.Equal("restored high", value);
    }

    [Fact]
    public void A_Detached_Candidate_Cannot_Be_Revived()
    {
        var (root, leaf, _) = MissingWithCandidate();
        root.MergedDictionaries.Clear();
        Assert.False(root.TryGetResource("key", null, out _));
        leaf["key"] = "detached";
        Assert.False(root.TryGetResource("key", null, out _));
        root.MergedDictionaries.Add(new ResourceDictionary { ["key"] = "attached" });
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("attached", value);
    }

    [Fact]
    public void An_Unrelated_Dictionary_And_Multiple_Epochs_Cannot_Advance_A_Candidate()
    {
        var (root, leaf, cache) = MissingWithCandidate();
        var other = new ResourceDictionary();
        other.MarkResourceLookupDependency();
        other["key"] = "unrelated";
        Assert.False(cache.TryGet("key", null, out _));
        Assert.False(root.TryGetResource("key", null, out _));
        leaf["key"] = "local";
        root.MergedDictionaries.Add(new ResourceDictionary { ["key"] = "higher" });
        Assert.False(cache.TryGet("key", null, out _));
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("higher", value);
    }

    [Fact]
    public void Found_Null_Is_Not_A_Negative_Answer()
    {
        var (root, leaf, cache) = MissingWithCandidate();
        leaf["key"] = null;
        Assert.True(cache.TryGet("key", null, out var found));
        Assert.Same(leaf, found);
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Deferred_Insertion_Does_Not_Use_Plain_Entry_Advancement()
    {
        var (root, leaf, cache) = MissingWithCandidate();
        var calls = 0;
        leaf.AddDeferred("key", _ => { ++calls; return "factory"; });
        Assert.False(cache.TryGet("key", null, out _));
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("factory", value);
        Assert.Equal(1, calls);
        Assert.True(root.TryGetResource("key", null, out _));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mutable_Stored_Key_Equality_Is_Always_Probed(bool addAfterLookup)
    {
        var opaque = new MutableKey("key");
        var leaf = new ResourceDictionary();
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        if (addAfterLookup) Assert.False(root.TryGetResource("key", null, out _));
        leaf[opaque] = "dynamic";
        Assert.False(root.TryGetResource("key", null, out _));
        opaque.Match = true;
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("dynamic", value);
        opaque.Match = false;
        Assert.False(root.TryGetResource("key", null, out _));
    }

    [Fact]
    public void Arbitrary_Type_Subclasses_Are_Not_Assumed_To_Have_Stable_Equality()
    {
        Assert.True(ResourceLookupCache.IsEligible(typeof(Button)));
        Assert.False(ResourceLookupCache.IsEligible(new TypeDelegator(typeof(Button))));
        Assert.False(ResourceLookupCache.IsStableStoredKey(new TypeDelegator(typeof(Button))));
    }

    [Fact]
    public void Repeated_Real_Insertion_Removal_And_Advancement_Allocate_No_Cache_Objects()
    {
        var (root, leaf, _) = MissingWithCandidate();
        var value = new object();
        void Cycle()
        {
            leaf["key"] = value;
            root.TryGetResource("key", null, out _);
            leaf.Remove("key");
            root.TryGetResource("key", null, out _);
        }
        for (var i = 0; i < 64; ++i) Cycle();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; ++i) Cycle();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (ResourceDictionary Root, ResourceDictionary Leaf, ResourceLookupCache Cache) MissingWithCandidate()
    {
        var leaf = new ResourceDictionary { ["key"] = "initial" };
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.True(root.TryGetResource("key", null, out _));
        leaf.Remove("key");
        Assert.False(root.TryGetResource("key", null, out _));
        return (root, leaf, Cache(root));
    }

    private static ResourceLookupCache Cache(ResourceDictionary dictionary) =>
        (ResourceLookupCache)typeof(ResourceDictionary).GetField("_lookupCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dictionary)!;

    private sealed class MutableKey(string key)
    {
        public bool Match;
        public override int GetHashCode() => key.GetHashCode();
        public override bool Equals(object? other) => ReferenceEquals(this, other) || Match && other is string s && s == key;
    }
}
