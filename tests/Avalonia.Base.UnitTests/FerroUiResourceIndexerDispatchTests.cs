using System;
using Avalonia.Controls;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiResourceIndexerDispatchTests
{
    [Fact]
    public void String_Writes_Notify_After_Commit_And_Can_Establish_Parent_Dependencies()
    {
        var leaf = new ResourceDictionary();
        var owner = new StyledElement { Resources = leaf };
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        var notifications = 0;
        ((IResourceHost)owner).ResourcesChanged += (_, _) =>
        {
            ++notifications;
            Assert.True(root.TryGetResource("key", null, out var value));
            Assert.Equal(leaf["key"], value);
        };
        leaf["key"] = 1;
        leaf["key"] = 1; // Identical values must still notify resource hosts.
        leaf["key"] = null;
        Assert.Equal(3, notifications);
        Assert.True(root.TryGetResource("key", null, out var final));
        Assert.Null(final);
    }

    [Fact]
    public void String_Write_With_Opaque_Stored_Key_Rechecks_Dependency_After_Equality()
    {
        var leaf = new ResourceDictionary();
        var root = new ResourceDictionary { MergedDictionaries = { leaf } };
        var invoked = false;
        var key = new CallbackKey(() =>
        {
            invoked = true;
            Assert.False(root.TryGetResource("unrelated", null, out _));
        });
        leaf.Add(key, "opaque");
        var epoch = ResourceLookupCache.Epoch;
        leaf["key"] = "string";
        Assert.True(invoked);
        Assert.NotEqual(epoch, ResourceLookupCache.Epoch);
        Assert.Equal("string", leaf["key"]);
    }

    [Fact]
    public void Runtime_Type_And_String_Keys_Retain_Separate_Values()
    {
        var dictionary = new ResourceDictionary { [typeof(Control)] = "type", ["key"] = "first" };
        dictionary["key"] = "second";
        Assert.Equal("type", dictionary[typeof(Control)]);
        Assert.Equal("second", dictionary["key"]);
        Assert.Null(dictionary["missing"]);
    }

    private sealed class CallbackKey(Action callback)
    {
        public override int GetHashCode() => "key".GetHashCode();
        public override bool Equals(object? obj)
        {
            if (obj is string text && text == "key") callback();
            return ReferenceEquals(this, obj);
        }
    }
}
