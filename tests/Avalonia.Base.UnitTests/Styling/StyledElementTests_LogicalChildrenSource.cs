using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    public void Custom_Source_Attaches_Without_Disconnecting_Owned_Children()
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
        Assert.True(((ILogical)omitted).IsAttachedToLogicalTree);
        root.Child = null;
        Assert.Equal(1, detached);
        Assert.False(((ILogical)enumerated).IsAttachedToLogicalTree);
        Assert.False(((ILogical)omitted).IsAttachedToLogicalTree);
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
        Assert.Equal(hasOwnedChildren ? 1 : 0, omittedNotified);
        Assert.Equal("value", enumerated.FindResource("key"));
    }

    [Fact]
    public void Removed_Styles_Detach_From_Custom_Source()
    {
        var owner = new SourceControl();
        var child = new Control();
        var owned = new Control();
        owner.Own(owned);
        ((ISetLogicalParent)child).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { child, owned, child };
        var style = new Style(x => x.OfType<Control>())
        {
            Setters = { new Setter(Control.TagProperty, "styled") },
        };
        owner.Styles.Add(style);
        var root = new TestRoot(owner);
        Assert.Equal("styled", child.Tag);
        Assert.Equal("styled", owned.Tag);

        owner.Styles.Remove(style);

        Assert.Null(child.Tag);
        Assert.Null(owned.Tag);
    }

    [Fact]
    public void DataContext_BeginUpdate_Traces_Custom_Source_Before_Parent_Change()
    {
        var calls = new List<string>();
        var owner = new SourceControl { Name = "parent", Calls = calls };
        var child = new SourceControl { Name = "child", Calls = calls };
        var owned = new SourceControl { Name = "owned", Calls = calls };
        owner.Own(owned);
        ((ISetLogicalParent)child).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { child, owned, child };
        owner.DataContextChanged += (_, _) => calls.Add("parent changed");
        calls.Clear();

        owner.DataContext = "context";

        Assert.Equal("context", child.DataContext);
        Assert.Equal("context", owned.DataContext);
        Assert.True(calls.IndexOf("child begin") < calls.IndexOf("parent changed"));
        Assert.True(calls.IndexOf("owned begin") < calls.IndexOf("parent changed"));
        Assert.Single(calls.FindAll(value => value == "child begin"));
        Assert.Single(calls.FindAll(value => value == "owned begin"));
        Assert.Contains("child end", calls);
        Assert.Contains("owned end", calls);
        Assert.Contains("parent end", calls);
    }

    [Fact]
    public void Internal_Traversal_Deduplicates_Projection_And_Respects_Actual_Parents()
    {
        var owner = new SourceControl();
        var owned = new Control();
        var projected = new Control();
        var foreign = new Control();
        var unowned = new Control();
        var other = new SourceControl();
        owner.Own(owned);
        other.Own(foreign);
        ((ISetLogicalParent)projected).SetParent(owner);
        var source = new AvaloniaList<ILogical> { owned, projected, projected, owner, foreign, unowned };
        owner.Source = source;
        var visited = new List<ILogical>();

        foreach (var child in new LogicalChildrenTraversal(owner, owner.Children, source))
            visited.Add(child);

        Assert.Equal(new[] { owned, projected }, visited);
        Assert.Same(source, ((ILogical)owner).LogicalChildren);
        var root = new TestRoot(owner);
        Assert.True(((ILogical)owned).IsAttachedToLogicalTree);
        Assert.True(((ILogical)projected).IsAttachedToLogicalTree);
        Assert.False(((ILogical)foreign).IsAttachedToLogicalTree);
        Assert.False(((ILogical)unowned).IsAttachedToLogicalTree);
    }

    [Fact]
    public void Empty_Projection_Preserves_Owned_Lifecycle_And_Resource_Updates()
    {
        var owner = new SourceControl { Source = new AvaloniaList<ILogical>() };
        var owned = new Control();
        owner.Own(owned);
        var root = new TestRoot(owner);
        var resourcesChanged = 0;
        owned.ResourcesChanged += (_, _) => ++resourcesChanged;

        owner.Resources["key"] = "value";
        owner.DataContext = "data";

        Assert.Empty(((ILogical)owner).LogicalChildren);
        Assert.True(((ILogical)owned).IsAttachedToLogicalTree);
        Assert.Equal(1, resourcesChanged);
        Assert.Equal("data", owned.DataContext);
        root.Child = null;
        Assert.False(((ILogical)owned).IsAttachedToLogicalTree);
    }

    [Fact]
    public void Weak_Projection_Remains_Stable_During_Resource_Callback_Garbage_Collection()
    {
        var owner = new SourceControl { WeakChildren = true };
        var first = new Control();
        ((ISetLogicalParent)first).SetParent(owner);
        var source = new WeakProjection();
        source.Add(first);
        var (second, keepAlive) = AddWeakChild(owner, source);
        owner.Source = source;
        first.ResourcesChanged += (_, _) =>
        {
            keepAlive.Value = null;
            Collect();
            Assert.True(second.TryGetTarget(out _));
        };

        owner.Resources["key"] = "value";

        Collect();
        Assert.False(second.TryGetTarget(out _));
        Assert.Single(source);
        GC.KeepAlive(first);
        GC.KeepAlive(owner);
    }

    [Fact]
    public void Projected_Child_Reparented_During_Propagation_Is_Not_Notified_By_Old_Parent()
    {
        var owner = new SourceControl();
        var first = new Control();
        var second = new Control();
        ((ISetLogicalParent)first).SetParent(owner);
        ((ISetLogicalParent)second).SetParent(owner);
        owner.Source = new AvaloniaList<ILogical> { first, second };
        var notifications = 0;
        first.ResourcesChanged += (_, _) => ((ISetLogicalParent)second).SetParent(null);
        second.ResourcesChanged += (_, _) => ++notifications;

        owner.Resources["key"] = "value";

        // Detachment itself raises one resource notification. The old owner's
        // still-running propagation must not issue another notification.
        Assert.Equal(1, notifications);
        Assert.Null(second.Parent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<Control>, StrongBox<Control?>) AddWeakChild(
        SourceControl owner, WeakProjection source)
    {
        var child = new Control();
        ((ISetLogicalParent)child).SetParent(owner);
        source.Add(child);
        return (new(child), new(child));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class WeakProjection : IAvaloniaReadOnlyList<ILogical>
    {
        private readonly List<WeakReference<ILogical>> _children = new();
        public void Add(ILogical child) => _children.Add(new(child));
        public int Count
        {
            get
            {
                var count = 0;
                foreach (var _ in this) ++count;
                return count;
            }
        }
        public ILogical this[int index]
        {
            get
            {
                foreach (var child in this)
                    if (index-- == 0) return child;
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        public IEnumerator<ILogical> GetEnumerator()
        {
            foreach (var child in _children)
                if (child.TryGetTarget(out var target)) yield return target;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class SourceControl : Control
    {
        public bool WeakChildren { get => UseWeakInheritanceChildren; set => UseWeakInheritanceChildren = value; }
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
