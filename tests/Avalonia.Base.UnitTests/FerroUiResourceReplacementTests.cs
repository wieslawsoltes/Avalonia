using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceReplacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_Replacement_Preserves_Locations_And_Reads_Live_Values(bool useTypeKey)
    {
        object key = useTypeKey ? typeof(Button) : "key";
        var leaf = new ResourceDictionary { [key] = "before" };
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.Equal("before", Find(root, key));
        Assert.False(root.TryGetResource("missing", null, out _));
        var epoch = ResourceLookupCache.Epoch;
        foreach (var value in new object?[] { "after", null, 42 })
        {
            leaf[key] = value;
            Assert.Equal(epoch, ResourceLookupCache.Epoch);
            Assert.Equal(value, Find(root, key));
            Assert.False(root.TryGetResource("missing", null, out _));
        }
        leaf["missing"] = null;
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Null(Find(root, "missing"));
    }

    [Fact]
    public void Theme_Locations_Are_Preserved_But_Variant_Values_Remain_Independent()
    {
        var light = new ResourceDictionary { [typeof(Button)] = "light" };
        var dark = new ResourceDictionary { [typeof(Button)] = "dark" };
        var root = new ResourceDictionary();
        root.ThemeDictionaries[ThemeVariant.Light] = light;
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        Assert.Equal("light", Find(root, typeof(Button), ThemeVariant.Light));
        Assert.Equal("dark", Find(root, typeof(Button), ThemeVariant.Dark));
        var epoch = ResourceLookupCache.Epoch;
        light[typeof(Button)] = "new light";
        Assert.Equal(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("new light", Find(root, typeof(Button), ThemeVariant.Light));
        Assert.Equal("dark", Find(root, typeof(Button), ThemeVariant.Dark));
        root.ThemeDictionaries[ThemeVariant.Light] = dark;
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("dark", Find(root, typeof(Button), ThemeVariant.Light));
    }

    [Fact]
    public void Notification_Reentrancy_Can_Resize_And_Add_A_Higher_Priority_Location()
    {
        var host = new Control();
        var leaf = new ResourceDictionary { ["key"] = "before" };
        var higher = new ResourceDictionary();
        host.Resources.MergedDictionaries.Add(leaf);
        host.Resources.MergedDictionaries.Add(higher);
        Assert.Equal("before", host.FindResource("key"));
        var observed = new List<object?>();
        var changed = false;
        host.ResourcesChanged += (_, _) =>
        {
            observed.Add(host.FindResource("key"));
            if (!changed)
            {
                changed = true;
                leaf.EnsureCapacity(4096);
                higher["key"] = "higher";
            }
        };
        leaf["key"] = "after";
        Assert.Equal(new object?[] { "after", "higher" }, observed);
        Assert.Equal("after", leaf["key"]);
        Assert.Equal("higher", host.FindResource("key"));
    }

    [Fact]
    public void Throwing_Notification_Does_Not_Undo_The_Replacement()
    {
        var host = new Control();
        var leaf = new ResourceDictionary { ["key"] = "before" };
        host.Resources.MergedDictionaries.Add(leaf);
        Assert.Equal("before", host.FindResource("key"));
        var epoch = ResourceLookupCache.Epoch;
        host.ResourcesChanged += (_, _) => throw new InvalidOperationException("observer");
        Assert.Throws<InvalidOperationException>(() => leaf["key"] = "after");
        Assert.Equal(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("after", host.FindResource("key"));
    }

    [Fact]
    public void Deferred_Transitions_Invalidate_And_NonShared_Resources_Are_Not_Frozen()
    {
        var leaf = new ResourceDictionary();
        var factory = new Factory();
        leaf.AddNotSharedDeferred("key", factory);
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.NotSame(Find(root, "key"), Find(root, "key"));
        Assert.Equal(2, factory.Calls);
        var epoch = ResourceLookupCache.Epoch;
        leaf["key"] = "plain";
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("plain", Find(root, "key"));
        epoch = ResourceLookupCache.Epoch;
        var shared = new Factory();
        leaf["key"] = shared;
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        var value = Find(root, "key");
        Assert.Same(value, Find(root, "key"));
        Assert.Equal(1, shared.Calls);
        leaf["key"] = new Factory();
        epoch = ResourceLookupCache.Epoch;
        // Replacing a not-yet-materialized deferred entry must also invalidate.
        leaf["key"] = null;
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Null(Find(root, "key"));
    }

    [Fact]
    public void Bulk_Replacement_Remains_Conservatively_Invalidated()
    {
        var leaf = new ResourceDictionary { ["key"] = "before" };
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.Equal("before", Find(root, "key"));
        var epoch = ResourceLookupCache.Epoch;
        leaf.SetItems(new[] { new KeyValuePair<object, object?>("key", "after") });
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("after", Find(root, "key"));
    }

    [Fact]
    public void The_Setter_Does_Not_Add_An_Extra_User_Key_Hash_Call()
    {
        var key = new CallbackKey();
        var dictionary = new ResourceDictionary { [key] = 1 };
        key.Calls = 0;
        dictionary[key] = 2;
        Assert.Equal(1, key.Calls);
        Assert.Equal(2, dictionary[key]);
        var root = new ResourceDictionary { MergedDictionaries = { dictionary } };
        Assert.False(root.TryGetResource("missing", null, out _));
        key.Calls = 0;
        dictionary[key] = 3;
        Assert.Equal(1, key.Calls);
    }

    [Fact]
    public void Graph_Changes_In_A_Key_Callback_Force_Conservative_Invalidation()
    {
        var key = new CallbackKey();
        var dictionary = new ResourceDictionary { [key] = 1 };
        var other = new ResourceDictionary();
        var root = new ResourceDictionary { MergedDictionaries = { dictionary, other } };
        Assert.False(root.TryGetResource("new location", null, out _));
        var callbackEpoch = ResourceLookupCache.Epoch;
        key.Callback = () =>
        {
            other["new location"] = 1;
            callbackEpoch = ResourceLookupCache.Epoch;
        };
        dictionary[key] = 2;
        Assert.Equal(unchecked(callbackEpoch + 1), ResourceLookupCache.Epoch);
        Assert.Equal(2, dictionary[key]);
        Assert.Equal(1, Find(root, "new location"));
    }

    [Fact]
    public void Unrelated_Local_Mutations_Do_Not_Flush_Existing_Caches()
    {
        var leaf = new ResourceDictionary { ["key"] = 1 };
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        Assert.Equal(1, Find(root, "key"));
        var unrelated = new ResourceDictionary();
        var epoch = ResourceLookupCache.Epoch;
        unrelated["local"] = 1;
        unrelated["local"] = 2;
        unrelated.Add("another", 3);
        unrelated.Remove("another");
        unrelated.Clear();
        Assert.Equal(epoch, ResourceLookupCache.Epoch);
        Assert.Equal(1, Find(root, "key"));
    }

    [Fact]
    public void First_Dependency_Created_During_A_Key_Callback_Is_Rechecked_After_Write()
    {
        var key = new CallbackKey();
        var dictionary = new ResourceDictionary { [key] = 1 };
        var root = new ResourceDictionary { MergedDictionaries = { dictionary } };
        var callbackEpoch = ResourceLookupCache.Epoch;
        key.Callback = () =>
        {
            Assert.False(root.TryGetResource("missing", null, out _));
            callbackEpoch = ResourceLookupCache.Epoch;
        };
        dictionary[key] = 2;
        Assert.Equal(unchecked(callbackEpoch + 1), ResourceLookupCache.Epoch);
        Assert.Equal(2, dictionary[key]);
    }

    private static object? Find(ResourceDictionary dictionary, object key, ThemeVariant? theme = null)
    {
        Assert.True(dictionary.TryGetResource(key, theme, out var value));
        return value;
    }

    private sealed class Factory : IDeferredContent
    {
        internal int Calls;
        public object Build(IServiceProvider? _) { ++Calls; return new object(); }
    }

    private sealed class CallbackKey
    {
        internal int Calls;
        internal Action? Callback;
        public override int GetHashCode()
        {
            ++Calls;
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            return 42;
        }
    }
}
