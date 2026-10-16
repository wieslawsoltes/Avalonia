using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Avalonia.PropertyStore;
using Xunit;

namespace Avalonia.Base.UnitTests.PropertyStore;

public class SinglePropertyInheritanceTests
{
    private static readonly Action<ValueStore, ValueStore?, ValueStore?> General =
        (Action<ValueStore, ValueStore?, ValueStore?>)typeof(ValueStore)
            .GetMethod("SetInheritanceParentGeneral", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Action<ValueStore, ValueStore?, ValueStore?>));

    [Theory]
    [InlineData("singleton")]
    [InlineData("equal-values")]
    [InlineData("different-properties")]
    [InlineData("multiple-properties")]
    [InlineData("ancestor-chain")]
    [InlineData("local-pruning")]
    [InlineData("detach-reattach")]
    [InlineData("reentrant")]
    public void Observable_Trace_Matches_The_General_Snapshot(string scenario)
    {
        Assert.Equal(Trace(scenario, false), Trace(scenario, true));
    }

    [Fact]
    public void Singleton_Transitions_Keep_The_Pool_Untouched_During_Notifications()
    {
        var compound = new Node { First = 1, Second = 2 };
        var warm = new Node { Parent = compound };
        warm.Parent = null;
        var pool = Pool();
        var before = pool.Count;
        Assert.True(before > 0);
        var a = new Node { First = 1 };
        var b = new Node { First = 2 };
        var child = new Node { Parent = a };
        var calls = 0;
        child.PropertyChanged += (_, _) => { ++calls; Assert.Equal(before, pool.Count); };
        child.Parent = b;
        child.Parent = null;
        child.Parent = a;
        Assert.Equal(3, calls);
        Assert.Equal(before, pool.Count);
    }

    [Fact]
    public void Throwing_Singleton_Callback_Preserves_Ancestor_And_Future_Updates()
    {
        var a = new Node { First = 1 };
        var b = new Node { First = 2 };
        var child = new Node { Parent = a };
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, _) => throw new InvalidOperationException("callback");
        child.PropertyChanged += handler;
        Assert.Throws<InvalidOperationException>(() => child.Parent = b);
        Assert.Equal(2, child.First);
        child.PropertyChanged -= handler;
        b.First = 3;
        Assert.Equal(3, child.First);
        child.Parent = a;
        Assert.Equal(1, child.First);
    }

    [Fact]
    public void Inherited_Inpc_Reuses_Only_Name_Arguments_And_Preserves_Senders()
    {
        var parent = new Node { First = 1 };
        var a = new Node { Parent = parent };
        var b = new Node { Parent = parent };
        var args = new List<PropertyChangedEventArgs>();
        var senders = new List<object?>();
        ((INotifyPropertyChanged)a).PropertyChanged += (s, e) => { senders.Add(s); args.Add(e); };
        ((INotifyPropertyChanged)b).PropertyChanged += (s, e) => { senders.Add(s); args.Add(e); };
        parent.First = 2;
        parent.First = 3;
        parent.ClearValue(Node.FirstProperty);
        Assert.Equal(new object[] { a, b, a, b, a, b }, senders);
        Assert.Equal(6, args.Count);
        foreach (var arg in args)
        {
            Assert.Same(args[0], arg);
            Assert.Equal("First", arg.PropertyName);
        }
    }

    private static List<string> Trace(string scenario, bool optimized)
    {
        var old = new Node { First = 1 };
        var next = new Node { First = scenario == "equal-values" ? 1 : 2 };
        var third = new Node { First = 3 };
        old.SetValue(Node.LocalProperty, "not inherited");
        next.SetValue(Node.LocalProperty, "also local");
        if (scenario == "different-properties")
        {
            next.ClearValue(Node.FirstProperty);
            next.Second = 20;
        }
        if (scenario == "multiple-properties") { old.Second = 10; next.Second = 20; }
        if (scenario == "ancestor-chain")
        {
            old.Parent = new Node { Second = 10 };
            next.Parent = new Node { Second = 20 };
        }
        var child = new Node { Parent = old };
        var leaf = new Node { Parent = child };
        var local = new Node { Parent = child };
        if (scenario == "local-pruning") local.First = 99;
        var localLeaf = new Node { Parent = local };
        var trace = new List<string>();
        var reentered = false;
        void Move(Node? destination)
        {
            var store = child.GetValueStore();
            if (optimized) store.SetInheritanceParent(destination);
            else General(store, store.InheritanceAncestor, destination?.GetValueStore());
        }
        void Watch(Node node, string name)
        {
            node.PropertyChanged += (_, e) =>
            {
                trace.Add($"{name}:{e.Property.Name}:{e.OldValue}>{e.NewValue}:{node.First}:{node.Second}");
                if (scenario == "reentrant" && name == "child" && !reentered)
                {
                    reentered = true;
                    Move(third);
                }
            };
        }
        Watch(child, "child"); Watch(leaf, "leaf"); Watch(local, "local"); Watch(localLeaf, "localLeaf");
        if (scenario == "detach-reattach") Move(null);
        Move(next);
        trace.Add($"final:{child.First}:{leaf.First}:{local.First}:{localLeaf.First}:{child.Second}:{leaf.Second}");
        return trace;
    }

    private static ICollection Pool()
    {
        var valueType = typeof(ValueStore).GetNestedType("OldNewValue", BindingFlags.NonPublic)!;
        return (ICollection)typeof(AvaloniaPropertyDictionaryPool<>).MakeGenericType(valueType)
            .GetField("_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }

    private sealed class Node : AvaloniaObject
    {
        public static readonly StyledProperty<int> FirstProperty = AvaloniaProperty.Register<Node, int>("First", inherits: true);
        public static readonly StyledProperty<int> SecondProperty = AvaloniaProperty.Register<Node, int>("Second", inherits: true);
        public static readonly StyledProperty<string?> LocalProperty = AvaloniaProperty.Register<Node, string?>("Local");
        public int First { get => GetValue(FirstProperty); set => SetValue(FirstProperty, value); }
        public int Second { get => GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
        public AvaloniaObject? Parent { set => InheritanceParent = value; }
    }
}
