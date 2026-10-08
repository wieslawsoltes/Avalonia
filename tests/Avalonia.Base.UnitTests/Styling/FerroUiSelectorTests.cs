using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Styling;

public class FerroUiSelectorTests
{
    [Fact]
    public void Assignability_Cache_Uses_The_Current_StyleKey_Without_Skipping_Class_Changes()
    {
        var selector = default(Selector).Is<Button>().Class("active");
        var target = new ChangingStyleKey { Key = typeof(TextBlock) };
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        target.Key = typeof(Button);
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        target.Classes.Add("active");
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        target.Classes.Remove("active");
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        Assert.Equal(5, target.Reads);
    }

    [Fact]
    public void Mutable_Or_And_Replaced_Selectors_Are_Not_Negatively_Cached()
    {
        var alternatives = new List<Selector> { default(Selector).Is<Button>() };
        var style = new Style { Selector = Selectors.Or(alternatives),
            Setters = { new Setter(Control.TagProperty, "hit") } };
        var target = new TextBlock();
        StyleHelpers.TryAttach(style, target);
        Assert.Null(target.Tag);
        alternatives.Add(default(Selector).Is<TextBlock>());
        StyleHelpers.TryAttach(style, target);
        Assert.Equal("hit", target.Tag);
        style.Selector = default(Selector).Is<TextBlock>();
        var second = new TextBlock();
        StyleHelpers.TryAttach(style, second);
        Assert.Equal("hit", second.Tag);
    }

    private sealed class ChangingStyleKey : Control
    {
        public Type Key { get; set; } = typeof(Control);
        public int Reads { get; private set; }
        protected override Type StyleKeyOverride { get { ++Reads; return Key; } }
    }
}
