using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceTraversalTests
{
    [Fact]
    public void Tail_Traversal_Preserves_Deep_Mutation_And_Negative_Lookups()
    {
        var root = new ResourceDictionary();
        var tail = root;
        for (var depth = 0; depth < 512; ++depth)
        {
            var next = new ResourceDictionary();
            tail.MergedDictionaries.Add(next);
            for (var i = 0; i < 8; ++i)
                tail.MergedDictionaries.Add(new ResourceDictionary { ["other" + i] = i });
            tail = next;
        }
        for (var i = 0; i < 20; ++i)
        {
            Assert.False(root.TryGetResource("changing", null, out _));
            tail["changing"] = i;
            Assert.True(root.TryGetResource("changing", null, out var value));
            Assert.Equal(i, value);
            Assert.True(tail.Remove("changing"));
            Assert.False(root.TryGetResource("changing", null, out _));
        }
    }

    [Fact]
    public void NonTail_Branch_Misses_Continue_In_The_Original_Order()
    {
        var calls = new List<int>();
        var root = new ResourceDictionary();
        root.MergedDictionaries.Add(new CallbackProvider(() => calls.Add(0)));
        var middle = new ResourceDictionary();
        middle.MergedDictionaries.Add(new CallbackProvider(() => calls.Add(1)));
        middle.MergedDictionaries.Add(new CallbackProvider(() => calls.Add(2)));
        root.MergedDictionaries.Add(middle);
        root.MergedDictionaries.Add(new CallbackProvider(() => calls.Add(3)));
        for (var i = 0; i < 2; ++i)
        {
            calls.Clear();
            Assert.False(root.TryGetResource("key", null, out _));
            Assert.Equal(new[] { 3, 2, 1, 0 }, calls);
        }
    }

    [Fact]
    public void A_Missing_Provider_Can_Replace_The_Pending_Tail()
    {
        var root = new ResourceDictionary();
        root.MergedDictionaries.Add(new ResourceDictionary { ["key"] = "old" });
        var calls = 0;
        root.MergedDictionaries.Add(new CallbackProvider(() =>
        {
            ++calls;
            root.MergedDictionaries[0] = new ResourceDictionary { ["key"] = "new" };
        }));
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Equal("new", value);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Leaf_Miss_Rechecks_Children_Added_By_Key_Equality(bool themeChild)
    {
        const string key = "lookup";
        var stored = new EqualityCallbackKey(key.GetHashCode());
        var leaf = new ResourceDictionary { [stored] = "unrelated" };
        var root = new ResourceDictionary();
        // Put the leaf in a non-tail slot to exercise the direct leaf probe.
        root.MergedDictionaries.Add(new ResourceDictionary { [key] = "fallback" });
        root.MergedDictionaries.Add(leaf);
        var child = new ResourceDictionary { [key] = "added during equality" };
        stored.Callback = () =>
        {
            if (themeChild) leaf.ThemeDictionaries[ThemeVariant.Default] = child;
            else leaf.MergedDictionaries.Add(child);
        };
        Assert.True(root.TryGetResource(key, null, out var value));
        Assert.Equal("added during equality", value);
        Assert.Equal(1, stored.Calls);
    }

    [Fact]
    public void Theme_Misses_Precede_Tail_Merged_Traversal()
    {
        var calls = new List<int>();
        var root = new ResourceDictionary();
        root.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            MergedDictionaries = { new CallbackProvider(() => calls.Add(0)) }
        };
        root.ThemeDictionaries[ThemeVariant.Default] = new ResourceDictionary
        {
            MergedDictionaries = { new CallbackProvider(() => calls.Add(1)) }
        };
        root.MergedDictionaries.Add(new CallbackProvider(() => calls.Add(2)));
        Assert.False(root.TryGetResource("key", ThemeVariant.Dark, out _));
        Assert.Equal(new[] { 0, 1, 2 }, calls);
    }

    [Fact]
    public void Throwing_Deferred_Content_Restores_Reentrancy_State()
    {
        var leaf = new ResourceDictionary();
        leaf.AddDeferred("key", _ => throw new InvalidOperationException("factory"));
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        var depth = ResourceLookupCache.DeferredDepth;
        for (var i = 0; i < 2; ++i)
        {
            Assert.Throws<InvalidOperationException>(() => root.TryGetResource("key", null, out _));
            Assert.Equal(depth, ResourceLookupCache.DeferredDepth);
        }
        leaf["key"] = null;
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Null(value);
    }

    private sealed class CallbackProvider(Action callback) : ResourceProvider
    {
        public override bool HasResources => false;
        public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            callback();
            value = null;
            return false;
        }
    }

    private sealed class EqualityCallbackKey(int hash)
    {
        public Action? Callback;
        public int Calls;
        public override int GetHashCode() => hash;
        public override bool Equals(object? other)
        {
            if (ReferenceEquals(this, other)) return true;
            ++Calls;
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            return false;
        }
    }
}
