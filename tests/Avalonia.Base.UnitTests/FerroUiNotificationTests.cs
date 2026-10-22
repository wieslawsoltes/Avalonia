using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Reactive;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiNotificationTests
{
    [Fact]
    public void Inpc_Reuses_Only_Name_Arguments_And_Preserves_Senders()
    {
        var a = new Node();
        var b = new Node();
        var events = new List<(object?, PropertyChangedEventArgs)>();
        PropertyChangedEventHandler handler = (sender, e) => events.Add((sender, e));
        ((INotifyPropertyChanged)a).PropertyChanged += handler;
        ((INotifyPropertyChanged)b).PropertyChanged += handler;
        a.Value = 1;
        a.Value = 2;
        b.Value = 3;
        Assert.Equal(3, events.Count);
        Assert.Same(a, events[0].Item1);
        Assert.Same(a, events[1].Item1);
        Assert.Same(b, events[2].Item1);
        Assert.Same(events[0].Item2, events[1].Item2);
        Assert.Same(events[0].Item2, events[2].Item2);
        Assert.Equal("Value", events[0].Item2.PropertyName);
    }

    [Fact]
    public void Reentrant_Changes_Preserve_Outer_Values_And_Notification_Order()
    {
        var node = new Node();
        var trace = new List<string>();
        node.PropertyChanged += (_, e) =>
        {
            trace.Add($"first:{e.OldValue}:{e.NewValue}");
            if ((int)e.NewValue! == 1)
                node.Value = 2;
        };
        node.PropertyChanged += (_, e) => trace.Add($"second:{e.OldValue}:{e.NewValue}");
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => trace.Add($"inpc:{e.PropertyName}");
        node.Value = 1;
        Assert.Equal(new[] { "first:0:1", "first:1:2", "second:1:2", "inpc:Value", "second:0:1", "inpc:Value" }, trace);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Observer_Mutation_Uses_A_Subscription_Order_Snapshot(int count)
    {
        var subject = new LightweightSubject<int>();
        var trace = new List<int>();
        var subscriptions = new List<IDisposable>();
        for (var i = 0; i < count; ++i)
        {
            var index = i;
            subscriptions.Add(subject.Subscribe(new Observer(_ =>
            {
                trace.Add(index);
                if (index == 0)
                {
                    subscriptions[count - 1].Dispose();
                    subject.Subscribe(new Observer(_ => trace.Add(99)));
                }
            })));
        }
        subject.OnNext(1);
        Assert.Equal(count, trace.Count);
        for (var i = 0; i < count; ++i)
            Assert.Equal(i, trace[i]);
    }

    [Fact]
    public void Throwing_Pooled_Observer_Does_Not_Corrupt_Next_Publication()
    {
        var subject = new LightweightSubject<int>();
        var trace = new List<int>();
        var bad = subject.Subscribe(new Observer(_ => throw new InvalidOperationException()));
        for (var i = 0; i < 7; ++i)
        {
            var index = i;
            subject.Subscribe(new Observer(_ => trace.Add(index)));
        }
        Assert.Throws<InvalidOperationException>(() => subject.OnNext(1));
        Assert.Empty(trace);
        bad.Dispose();
        subject.OnNext(2);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, trace);
    }

    private sealed class Observer(Action<int> next) : IObserver<int>
    {
        public void OnNext(int value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }

    private sealed class Node : AvaloniaObject
    {
        public static readonly StyledProperty<int> ValueProperty = AvaloniaProperty.Register<Node, int>("Value");
        public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
}
