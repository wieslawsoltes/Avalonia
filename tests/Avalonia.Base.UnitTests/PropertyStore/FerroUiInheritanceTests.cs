using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.PropertyStore;
using Xunit;

namespace Avalonia.Base.UnitTests.PropertyStore;

public class FerroUiInheritanceTests
{
    [Fact]
    public void Same_Ancestor_Does_Not_Consume_A_Pooled_Dictionary()
    {
        var parent = new Node();
        parent.SetValue(Node.FirstProperty, 1);
        // Warm the general path explicitly; a singleton no longer needs a rental.
        parent.SetValue(Node.SecondProperty, 2);
        var child = new Node { Parent = parent };
        child.Parent = null;
        child.Parent = parent;
        var pool = GetPool();
        var before = pool.Count;
        Assert.True(before > 0);
        for (var i = 0; i < 20; ++i)
            child.GetValueStore().SetInheritanceParent(parent);
        Assert.Equal(before, pool.Count);
    }

    [Fact]
    public void Throwing_Notification_Returns_The_Rental()
    {
        var parent = new Node();
        parent.SetValue(Node.FirstProperty, 1);
        parent.SetValue(Node.SecondProperty, 2);
        var warmup = new Node { Parent = parent };
        warmup.Parent = null;
        var pool = GetPool();
        var before = pool.Count;
        var child = new Node();
        child.PropertyChanged += (_, _) => throw new InvalidOperationException("test callback");
        Assert.Throws<InvalidOperationException>(() => child.Parent = parent);
        Assert.Equal(before, pool.Count);
    }

    [Fact]
    public void Reparent_Preserves_Property_Major_Order_And_Local_Pruning()
    {
        var oldParent = new Node();
        var newParent = new Node();
        oldParent.SetValue(Node.FirstProperty, 1);
        oldParent.SetValue(Node.SecondProperty, 2);
        newParent.SetValue(Node.FirstProperty, 3);
        newParent.SetValue(Node.SecondProperty, 4);
        var child = new Node { Parent = oldParent };
        var grandchild = new Node { Parent = child };
        var local = new Node { Parent = child };
        local.SetValue(Node.FirstProperty, 99);
        var leaf = new Node { Parent = local };
        var trace = new List<string>();
        child.PropertyChanged += (_, e) => trace.Add($"child:{e.Property.Name}");
        grandchild.PropertyChanged += (_, e) => trace.Add($"grandchild:{e.Property.Name}");
        local.PropertyChanged += (_, e) => trace.Add($"local:{e.Property.Name}");
        leaf.PropertyChanged += (_, e) => trace.Add($"leaf:{e.Property.Name}");
        child.Parent = newParent;
        Assert.Equal(new[] { "child:First", "grandchild:First", "child:Second", "grandchild:Second", "local:Second", "leaf:Second" }, trace);
        Assert.Equal(99, leaf.GetValue(Node.FirstProperty));
        Assert.Equal(4, leaf.GetValue(Node.SecondProperty));
    }

    [Fact]
    public void Detach_And_Reattach_Still_Publish_The_Detached_Value()
    {
        var parent = new Node();
        parent.SetValue(Node.FirstProperty, 7);
        var child = new Node { Parent = parent };
        var values = new List<object?>();
        child.PropertyChanged += (_, e) => values.Add(e.NewValue);
        child.Parent = null;
        child.Parent = parent;
        Assert.Equal(new object?[] { 0, 7 }, values);
    }

    private static ICollection GetPool()
    {
        var valueType = typeof(ValueStore).GetNestedType("OldNewValue", BindingFlags.NonPublic)!;
        var poolType = typeof(AvaloniaPropertyDictionaryPool<>).MakeGenericType(valueType);
        return (ICollection)poolType.GetField("_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }

    private sealed class Node : AvaloniaObject
    {
        public static readonly StyledProperty<int> FirstProperty = AvaloniaProperty.Register<Node, int>("First", inherits: true);
        public static readonly StyledProperty<int> SecondProperty = AvaloniaProperty.Register<Node, int>("Second", inherits: true);
        public AvaloniaObject? Parent { set => InheritanceParent = value; }
    }
}
