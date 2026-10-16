using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiMutableThemeKeyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Requested_And_Inherited_Opaque_Keys_Use_The_Live_Dictionary(bool inherited)
    {
        var key = new MutableHashKey();
        var stored = new ThemeVariant(key, null);
        var requested = inherited ? new ThemeVariant("outer", stored) : stored;
        var root = new ResourceDictionary();
        root.ThemeDictionaries[stored] = new ResourceDictionary { ["resource"] = "value" };
        Assert.False(ResourceLookupCache.IsStableTheme(requested));
        Assert.True(root.TryGetResource("resource", requested, out var value));
        Assert.Equal("value", value);
        key.Hash = 19;
        // Mutable dictionary keys are unusual but legal. Match the original live lookup:
        // changing its hash means this entry is no longer reachable in that dictionary.
        Assert.False(root.TryGetResource("resource", requested, out _));
        key.Hash = 17;
        Assert.True(root.TryGetResource("resource", requested, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stored_Theme_Key_Equality_Is_Not_Frozen_Even_For_Default_Lookups(bool addAfterLookup)
    {
        var root = new ResourceDictionary();
        if (addAfterLookup)
        {
            root.MergedDictionaries.Add(new ResourceDictionary());
            Assert.False(root.TryGetResource("resource", null, out _));
        }
        var key = new MutableEqualityKey(ThemeVariant.Default.Key);
        var variant = new ThemeVariant(key, null);
        root.ThemeDictionaries[variant] = new ResourceDictionary { ["resource"] = "dynamic" };
        Assert.False(root.TryGetResource("resource", null, out _));
        key.Matches = true;
        Assert.True(root.TryGetResource("resource", null, out var value));
        Assert.Equal("dynamic", value);
        key.Matches = false;
        Assert.False(root.TryGetResource("resource", null, out _));
    }

    [Fact]
    public void Stable_BuiltIn_And_Custom_Theme_Chains_Remain_Eligible()
    {
        Assert.True(ResourceLookupCache.IsStableTheme(null));
        Assert.True(ResourceLookupCache.IsStableTheme(ThemeVariant.Default));
        Assert.True(ResourceLookupCache.IsStableTheme(ThemeVariant.Light));
        Assert.True(ResourceLookupCache.IsStableTheme(ThemeVariant.Dark));
        Assert.True(ResourceLookupCache.IsStableTheme(new ThemeVariant("custom", ThemeVariant.Light)));
        Assert.True(ResourceLookupCache.IsStableTheme(new ThemeVariant(typeof(Button), ThemeVariant.Dark)));
        var unstable = new ThemeVariant(new MutableHashKey(), null);
        Assert.False(ResourceLookupCache.IsStableTheme(new ThemeVariant("custom", unstable)));
    }

    private sealed class MutableHashKey
    {
        public int Hash = 17;
        public override int GetHashCode() => Hash;
        public override bool Equals(object? other) => ReferenceEquals(this, other);
    }

    private sealed class MutableEqualityKey(object target)
    {
        public bool Matches;
        public override int GetHashCode() => target.GetHashCode();
        public override bool Equals(object? other) => ReferenceEquals(this, other) || Matches && Equals(target, other);
    }
}
