using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Xunit;

namespace Avalonia.Base.UnitTests.Styling;

public class FerroUiSelectorDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_Type_Miss_Does_Not_Read_Instance_Classes(bool subscribe)
    {
        var target = new ChangingStyleKey();
        var selector = default(Selector).Is<Button>().Class("active");
        Assert.Equal(SelectorMatchResult.NeverThisType, selector.Match(target, subscribe: subscribe).Result);
        Assert.Equal(1, target.Reads);
        target.Key = typeof(Button);
        target.Classes.Add("active");
        Assert.True(selector.Match(target, subscribe: subscribe).IsMatch);
        Assert.Equal(2, target.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_Type_Must_Not_Bypass_Container_Query(bool subscribe)
    {
        var selector = default(Selector).Is<Button>();
        Assert.Equal(SelectorMatchResult.NeverThisInstance,
            selector.Match(new Button(), new ContainerQuery(), subscribe).Result);
    }

    [Fact]
    public void Compound_Match_With_Combinator_Retains_Instance_Result()
    {
        var target = new TextBlock { Tag = "ready" };
        var parent = new Border { Child = target };
        var selector = default(Selector).Is<Border>().Child().Is<TextBlock>()
            .PropertyEquals(Control.TagProperty, "ready");
        for (var i = 0; i < 5; ++i)
        {
            Assert.Equal(SelectorMatchResult.AlwaysThisInstance, selector.Match(target, subscribe: false).Result);
            target.Tag = "not-ready";
            Assert.False(selector.Match(target, subscribe: false).IsMatch);
            target.Tag = "ready";
        }
        parent.Child = null;
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
    }

    private sealed class ChangingStyleKey : Control
    {
        public Type Key = typeof(TextBlock);
        public int Reads;
        protected override Type StyleKeyOverride { get { ++Reads; return Key; } }
    }
}
