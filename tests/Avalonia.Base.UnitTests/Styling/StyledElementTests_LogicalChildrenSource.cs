using System.Collections.Generic;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Styling;

public class StyledElementTests_LogicalChildrenSource
{
    [Fact]
    public void Default_Source_Preserves_Ownership_Collection()
    {
        var owner = new SourceControl();
        var child = new Control();
        owner.Own(child);

        Assert.Same(owner.Children, ((ILogical)owner).LogicalChildren);
        Assert.Same(owner, child.Parent);
        owner.Disown(child);
        Assert.Null(child.Parent);
    }

    [Fact]
    public void Custom_Source_Does_Not_Change_Ownership()
    {
        var owner = new SourceControl();
        var owned = new Control();
        var enumerated = new Control();
        var source = new AvaloniaList<ILogical> { enumerated };
        owner.Own(owned);
        owner.Source = source;

        Assert.Same(source, ((ILogical)owner).LogicalChildren);
        Assert.Same(owner, owned.Parent);
        Assert.Null(enumerated.Parent);
        Assert.Equal(new[] { enumerated }, owner.GetLogicalChildren());

        source.Clear();
        Assert.Empty(owner.GetLogicalChildren());
        owner.Source = null;
        Assert.Equal(new[] { owned }, owner.GetLogicalChildren());
    }

    [Fact]
    public void Custom_Source_Drives_Attach_And_Detach()
    {
        var owner = new SourceControl();
        var omitted = new Control();
        var enumerated = new Control();
        owner.Own(omitted);
        ((ISetLogicalParent)enumerated).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { enumerated };
        var attached = 0;
        var detached = 0;
        enumerated.AttachedToLogicalTree += (_, _) => ++attached;
        enumerated.DetachedFromLogicalTree += (_, _) => ++detached;

        var root = new TestRoot(owner);

        Assert.Equal(1, attached);
        Assert.True(((ILogical)enumerated).IsAttachedToLogicalTree);
        Assert.False(((ILogical)omitted).IsAttachedToLogicalTree);
        root.Child = null;
        Assert.Equal(1, detached);
        Assert.False(((ILogical)enumerated).IsAttachedToLogicalTree);
        Assert.Same(owner, enumerated.Parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resource_Changes_Traverse_Custom_Source_With_Or_Without_Owned_Children(bool hasOwnedChildren)
    {
        var owner = new SourceControl();
        var enumerated = new Control();
        var omitted = new Control();
        if (hasOwnedChildren)
            owner.Own(omitted);
        ((ISetLogicalParent)enumerated).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { enumerated };
        var notified = 0;
        var omittedNotified = 0;
        enumerated.ResourcesChanged += (_, _) => ++notified;
        omitted.ResourcesChanged += (_, _) => ++omittedNotified;

        owner.Resources["key"] = "value";

        Assert.Equal(1, notified);
        Assert.Equal(0, omittedNotified);
        Assert.Equal("value", enumerated.FindResource("key"));
    }

    [Fact]
    public void Removed_Styles_Detach_From_Custom_Source()
    {
        var owner = new SourceControl();
        var child = new Control();
        ((ISetLogicalParent)child).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { child };
        var style = new Style(x => x.OfType<Control>())
        {
            Setters = { new Setter(Control.TagProperty, "styled") },
        };
        owner.Styles.Add(style);
        var root = new TestRoot(owner);
        Assert.Equal("styled", child.Tag);

        owner.Styles.Remove(style);

        Assert.Null(child.Tag);
    }

    [Fact]
    public void DataContext_BeginUpdate_Traces_Custom_Source_Before_Parent_Change()
    {
        var calls = new List<string>();
        var owner = new SourceControl { Name = "parent", Calls = calls };
        var child = new SourceControl { Name = "child", Calls = calls };
        ((ISetLogicalParent)child).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { child };
        owner.DataContextChanged += (_, _) => calls.Add("parent changed");
        calls.Clear();

        owner.DataContext = "context";

        Assert.Equal("context", child.DataContext);
        Assert.True(calls.IndexOf("child begin") < calls.IndexOf("parent changed"));
        Assert.Contains("child end", calls);
        Assert.Contains("parent end", calls);
    }

    private sealed class SourceControl : Control
    {
        public IAvaloniaList<ILogical> Children => LogicalChildren;
        public IAvaloniaReadOnlyList<ILogical>? Source
        {
            get => LogicalChildrenSource;
            set => LogicalChildrenSource = value;
        }
        public List<string>? Calls { get; init; }
        public void Own(ILogical child) => LogicalChildren.Add(child);
        public void Disown(ILogical child) => LogicalChildren.Remove(child);
        protected override void OnDataContextBeginUpdate()
        {
            Calls?.Add(Name + " begin");
            base.OnDataContextBeginUpdate();
        }
        protected override void OnDataContextEndUpdate()
        {
            Calls?.Add(Name + " end");
            base.OnDataContextEndUpdate();
        }
    }
}
