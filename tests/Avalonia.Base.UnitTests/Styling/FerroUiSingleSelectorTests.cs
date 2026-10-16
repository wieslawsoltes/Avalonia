using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Styling;

public class FerroUiSingleSelectorTests
{
    [Fact]
    public void Single_Class_Activator_Remains_Live()
    {
        var target = new Button();
        var style = new Style(x => x.Class("active"))
        { Setters = { new Setter(Control.TagProperty, "selected") } };
        StyleHelpers.TryAttach(style, target);
        Assert.Null(target.Tag);
        target.Classes.Add("active");
        Assert.Equal("selected", target.Tag);
        target.Classes.Remove("active");
        Assert.Null(target.Tag);
    }

    [Fact]
    public void Single_Node_Before_A_Combinator_Still_Observes_Parent_Changes()
    {
        var selector = default(Selector).Is<Border>().Child().Is<TextBlock>();
        var child = new TextBlock();
        var parent = new Border { Child = child };
        Assert.True(selector.Match(child, subscribe: false).IsMatch);
        parent.Child = null;
        Assert.False(selector.Match(child, subscribe: false).IsMatch);
        var other = new StackPanel { Children = { child } };
        Assert.False(selector.Match(child, subscribe: false).IsMatch);
        other.Children.Clear();
        parent.Child = child;
        Assert.True(selector.Match(child, subscribe: false).IsMatch);
    }

    [Fact]
    public void Owned_Type_Cache_Does_Not_Skip_A_StyleKey_Read()
    {
        var target = new DynamicStyleKey();
        var selector = default(Selector).Is<Button>();
        for (var i = 0; i < 10; ++i) Assert.False(selector.Match(target, subscribe: false).IsMatch);
        Assert.Equal(10, target.Reads);
        target.Key = typeof(Button);
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        Assert.Equal(11, target.Reads);
    }

    [Fact]
    public void Custom_Type_Assignability_Is_Not_Frozen_By_The_Owned_Constraint_Cache()
    {
        var type = new MutableType();
        var selector = TypeNameAndClassSelector.Is(null, type);
        var target = new Button();
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        type.Matches = true;
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        type.Matches = false;
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        Assert.Equal(3, type.Calls);
    }

    [Fact]
    public void Concrete_And_Instance_Mismatches_Retain_Their_Result_Categories()
    {
        var type = default(Selector).OfType<Button>();
        Assert.Equal(SelectorMatchResult.NeverThisType, type.Match(new TextBlock(), subscribe: false).Result);
        Assert.Equal(SelectorMatchResult.AlwaysThisType, type.Match(new Button(), subscribe: false).Result);
        var name = TypeNameAndClassSelector.ForName(null, "expected");
        Assert.Equal(SelectorMatchResult.NeverThisInstance, name.Match(new Button(), subscribe: false).Result);
        Assert.Equal(SelectorMatchResult.AlwaysThisInstance,
            name.Match(new Button { Name = "expected" }, subscribe: false).Result);
    }

    private sealed class MutableType() : TypeDelegator(typeof(Button))
    {
        public bool Matches;
        public int Calls;
        public override bool IsAssignableFrom(Type? c) { ++Calls; return Matches; }
    }

    private sealed class DynamicStyleKey : Control
    {
        public Type Key = typeof(TextBlock);
        public int Reads;
        protected override Type StyleKeyOverride { get { ++Reads; return Key; } }
    }
}
