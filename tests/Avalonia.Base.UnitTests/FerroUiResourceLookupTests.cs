using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceLookupTests
{
    [Fact]
    public void Cached_Locations_Preserve_Local_Merged_And_Missing_Precedence()
    {
        var root = new ResourceDictionary();
        var first = new ResourceDictionary { ["key"] = "first" };
        var last = new ResourceDictionary();
        root.MergedDictionaries.Add(first);
        root.MergedDictionaries.Add(last);
        Assert.Equal("first", Find(root, "key"));
        Assert.Equal("first", Find(root, "key"));
        last["key"] = "last";
        Assert.Equal("last", Find(root, "key"));
        root["key"] = "local";
        Assert.Equal("local", Find(root, "key"));
        root.Remove("key");
        last.Remove("key");
        Assert.Equal("first", Find(root, "key"));
        first.Clear();
        Assert.False(root.TryGetResource("key", null, out _));
        Assert.False(root.TryGetResource("key", null, out _));
        first["key"] = null;
        Assert.True(root.TryGetResource("key", null, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Unowned_Nested_Mutations_And_Collection_Reordering_Invalidate()
    {
        var root = new ResourceDictionary();
        var nested = new ResourceDictionary();
        var a = new ResourceDictionary { ["key"] = 1 };
        var b = new ResourceDictionary { ["key"] = 2 };
        nested.MergedDictionaries.Add(a);
        nested.MergedDictionaries.Add(b);
        root.MergedDictionaries.Add(nested);
        Assert.Equal(2, Find(root, "key"));
        Assert.Equal(2, Find(root, "key"));
        nested.MergedDictionaries.Remove(b);
        nested.MergedDictionaries.Insert(0, b);
        Assert.Equal(1, Find(root, "key"));
        a["key"] = 3;
        Assert.Equal(3, Find(root, "key"));
        root.MergedDictionaries.Clear();
        Assert.False(root.TryGetResource("key", null, out _));
    }

    [Fact]
    public void Theme_Cache_Distinguishes_Variants_And_Replacement()
    {
        var root = new ResourceDictionary();
        var light = new ResourceDictionary { ["key"] = "light" };
        var dark = new ResourceDictionary { ["key"] = "dark" };
        root.ThemeDictionaries[ThemeVariant.Light] = light;
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        root.ThemeDictionaries[ThemeVariant.Default] = new ResourceDictionary { ["fallback"] = 10 };
        for (var i = 0; i < 3; ++i)
        {
            Assert.Equal("light", Find(root, "key", ThemeVariant.Light));
            Assert.Equal("dark", Find(root, "key", ThemeVariant.Dark));
            Assert.Equal(10, Find(root, "fallback", ThemeVariant.Dark));
        }
        root.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["key"] = "replacement" };
        Assert.Equal("replacement", Find(root, "key", ThemeVariant.Dark));
        light["key"] = "changed";
        Assert.Equal("changed", Find(root, "key", ThemeVariant.Light));
    }

    [Fact]
    public void NonShared_Deferred_Resources_Are_Rebuilt_On_Cache_Hits()
    {
        var root = new ResourceDictionary();
        var nested = new ResourceDictionary();
        var factory = new Factory();
        nested.AddNotSharedDeferred("key", factory);
        root.MergedDictionaries.Add(nested);
        var first = Find(root, "key");
        var second = Find(root, "key");
        var third = Find(root, "key");
        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Equal(3, factory.Calls);
    }

    [Fact]
    public void Custom_Provider_Is_Probed_On_Every_Lookup()
    {
        var root = new ResourceDictionary();
        var fallback = new ResourceDictionary { ["key"] = "fallback" };
        var provider = new ChangingProvider();
        root.MergedDictionaries.Add(fallback);
        root.MergedDictionaries.Add(provider);
        Assert.Equal("fallback", Find(root, "key"));
        provider.Value = "dynamic";
        Assert.Equal("dynamic", Find(root, "key"));
        provider.Value = null;
        Assert.Equal("fallback", Find(root, "key"));
        Assert.Equal(3, provider.Calls);
    }

    [Fact]
    public void Host_Callback_Sees_New_Resource_Before_Child_Notifications()
    {
        var host = new Control();
        var nested = new ResourceDictionary { ["key"] = "before" };
        host.Resources.MergedDictionaries.Add(nested);
        Assert.Equal("before", host.FindResource("key"));
        Assert.Equal("before", host.FindResource("key"));
        object? observed = null;
        host.ResourcesChanged += (_, _) => observed = host.FindResource("key");
        nested["key"] = "after";
        Assert.Equal("after", observed);
    }

    [Fact]
    public void Bulk_Iterator_Reentrancy_Sees_Each_Partial_Update()
    {
        var root = new ResourceDictionary();
        var nested = new ResourceDictionary { ["key"] = 0 };
        root.MergedDictionaries.Add(nested);
        Assert.Equal(0, Find(root, "key"));
        nested.SetItems(Values());
        Assert.Equal(2, Find(root, "key"));
        IEnumerable<KeyValuePair<object, object?>> Values()
        {
            yield return new("key", 1);
            Assert.Equal(1, Find(root, "key"));
            yield return new("key", 2);
            Assert.Equal(2, Find(root, "key"));
        }
    }

    [Fact]
    public void Location_Cache_Is_Bounded_And_Does_Not_Admit_Long_Keys()
    {
        var cache = new ResourceLookupCache();
        for (var i = 0; i < 1000; ++i)
            cache.Add("key" + i, null, null, ResourceLookupCache.Epoch);
        Assert.InRange(cache.Count, 1, ResourceLookupCache.Capacity);
        Assert.False(ResourceLookupCache.IsEligible(new string('x', 257)));
    }

    private static object? Find(ResourceDictionary dictionary, object key, ThemeVariant? theme = null)
    {
        Assert.True(dictionary.TryGetResource(key, theme, out var value));
        return value;
    }
    private sealed class Factory : IDeferredContent
    {
        public int Calls;
        public object Build(IServiceProvider? _) { ++Calls; return new object(); }
    }
    private sealed class ChangingProvider : ResourceProvider
    {
        public string? Value;
        public int Calls;
        public override bool HasResources => true;
        public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
        { ++Calls; value = Value; return Value is not null; }
    }
}
