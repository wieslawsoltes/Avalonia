using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Styling;

public class FerroUiSelectorPlanTests
{
    [Fact]
    public void Plan_Is_Reused_While_Properties_And_Classes_Are_Evaluated_Live()
    {
        var selector = default(Selector).Is<Button>().PropertyEquals(Control.TagProperty, "match").Class("active");
        var target = new Button { Tag = "match", Classes = { "active" } };
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        var plan = Plan(selector);
        Assert.NotNull(plan);
        target.Tag = "miss";
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        target.Tag = "match";
        target.Classes.Remove("active");
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        target.Classes.Add("active");
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        Assert.Same(plan, Plan(selector));
    }

    [Fact]
    public void Cached_Plan_Does_Not_Freeze_Or_Alternatives()
    {
        var alternatives = new List<Selector> { default(Selector).Is<Button>(), default(Selector).Is<Border>() };
        var selector = Selectors.Or(alternatives).PropertyEquals(Control.TagProperty, "match");
        var target = new TextBlock { Tag = "match" };
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        var plan = Plan(selector);
        alternatives.Add(default(Selector).Is<TextBlock>());
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        Assert.Same(plan, Plan(selector));
    }

    [Fact]
    public void Combinator_Still_Observes_A_Changed_Logical_Parent()
    {
        var selector = default(Selector).Is<Border>().Child().Is<TextBlock>().PropertyEquals(Control.TagProperty, "match");
        var target = new TextBlock { Tag = "match" };
        var border = new Border { Child = target };
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
        border.Child = null;
        var panel = new StackPanel { Children = { target } };
        Assert.False(selector.Match(target, subscribe: false).IsMatch);
        panel.Children.Clear();
        border.Child = target;
        Assert.True(selector.Match(target, subscribe: false).IsMatch);
    }

    [Fact]
    public void Simple_Selector_Does_Not_Allocate_A_Plan()
    {
        var selector = default(Selector).Is<Button>();
        selector.Match(new Button(), subscribe: false);
        Assert.Null(Plan(selector));
    }

    [Fact]
    public void Live_Activation_Is_Not_Replaced_With_A_Cached_Match()
    {
        var target = new Button { Tag = "match" };
        var style = new Style(x => x.Is<Button>().PropertyEquals(Control.TagProperty, "match").Class("active"))
        { Setters = { new Setter(Button.ContentProperty, "selected") } };
        StyleHelpers.TryAttach(style, target);
        Assert.Null(target.Content);
        target.Classes.Add("active");
        Assert.Equal("selected", target.Content);
        target.Tag = "miss";
        Assert.Null(target.Content);
        target.Tag = "match";
        Assert.Equal("selected", target.Content);
    }

    private static object? Plan(Selector selector) => typeof(Selector)
        .GetField("_evaluationPlan", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(selector);
}
