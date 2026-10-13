using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class AvaloniaObjectTests_WeakInheritanceChildren
{
    [Fact]
    public void Default_Inheritance_Children_Are_Strong()
    {
        var parent = new Node();
        var child = AddChild(parent);
        Collect();
        Assert.False(parent.WeakChildren);
        Assert.True(child.TryGetTarget(out _));
        GC.KeepAlive(parent);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 12)]
    [InlineData(true, 12)]
    public void Weak_Children_Can_Be_Collected_Before_Or_After_OptIn(bool optInBefore, int count)
    {
        var parent = new Node { WeakChildren = optInBefore };
        var children = new List<WeakReference<Node>>();
        for (var i = 0; i < count; ++i)
            children.Add(AddChild(parent));
        parent.WeakChildren = true;
        parent.Value = "propagate before collection";
        Collect();
        foreach (var child in children)
            Assert.False(child.TryGetTarget(out _));
        parent.Value = "changed";
        using var live = parent.GetInheritanceChildren();
        Assert.Equal(0, live.Count);
        GC.KeepAlive(parent);
    }

    [Fact]
    public void Weak_Children_Still_Receive_Inherited_Updates_And_Detach()
    {
        var parent = new Node { WeakChildren = true, Value = "first" };
        var child = new Node { Parent = parent };
        var grandchild = new Node { Parent = child };
        var changes = new List<object?>();
        child.PropertyChanged += (_, e) =>
        {
            if (e.Property == Node.ValueProperty)
                changes.Add(e.NewValue);
        };

        parent.Value = "second";
        Assert.Equal("second", child.Value);
        Assert.Equal("second", grandchild.Value);
        parent.ClearValue(Node.ValueProperty);
        Assert.Equal("unset", child.Value);
        child.Parent = null;
        parent.Value = "third";
        Assert.Equal("unset", child.Value);
        Assert.Equal(new object?[] { "second", "unset" }, changes);
        using var children = parent.GetInheritanceChildren();
        Assert.Equal(0, children.Count);
    }

    [Fact]
    public void Disabling_Weak_Children_Restores_Strong_Ownership()
    {
        var parent = new Node { WeakChildren = true };
        var child = AddChild(parent);
        parent.WeakChildren = false;
        Collect();
        Assert.True(child.TryGetTarget(out _));
        parent.Value = "still inherited";
        Assert.True(child.TryGetTarget(out var retained));
        Assert.Equal("still inherited", retained!.Value);
        GC.KeepAlive(parent);
    }

    [Fact]
    public void Logical_Ownership_Remains_Strong_When_Inheritance_Uses_Weak_References()
    {
        var parent = new OwnerControl();
        var child = AddOwnedChild(parent);
        Collect();
        Assert.True(child.TryGetTarget(out var retained));
        parent.DataContext = "context";
        Assert.Equal("context", retained!.DataContext);
        GC.KeepAlive(parent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Node> AddChild(Node parent) => new(new Node { Parent = parent });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control> AddOwnedChild(OwnerControl parent)
    {
        var child = new Control();
        parent.Add(child);
        return new(child);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class Node : AvaloniaObject
    {
        public static readonly StyledProperty<string> ValueProperty =
            AvaloniaProperty.Register<Node, string>(nameof(Value), "unset", inherits: true);
        public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public AvaloniaObject? Parent { get => InheritanceParent; set => InheritanceParent = value; }
        public bool WeakChildren { get => UseWeakInheritanceChildren; set => UseWeakInheritanceChildren = value; }
    }

    private sealed class OwnerControl : Control
    {
        public OwnerControl() => UseWeakInheritanceChildren = true;
        public void Add(ILogical child) => LogicalChildren.Add(child);
    }
}
