using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.PropertyStore;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiTypedTemplateBindingTests
{
    [Fact]
    public void Plain_Styled_Properties_Select_The_Typed_Entry()
    {
        var target = new Node();
        var expression = new TemplateBinding(Node.NumberProperty).CreateInstance(target, Node.NumberProperty, null);
        Assert.IsType<TypedTemplateBindingExpression<int>>(expression);
        Assert.IsAssignableFrom<IValueEntry<int>>(expression);
        Assert.Equal(BindingPriority.Template, expression.DefaultPriority);
    }

    [Fact]
    public void Converters_Different_Types_And_Sentinels_Retain_General_Expression()
    {
        var target = new Node();
        Assert.IsType<TemplateBindingExpression>(new TemplateBinding(Node.NumberProperty)
        {
            Converter = new FuncValueConverter<int, int>(v => v + 1)
        }.CreateInstance(target, Node.NumberProperty, null));
        Assert.IsType<TemplateBindingExpression>(new TemplateBinding(Node.NumberProperty)
            .CreateInstance(target, TextBlock.TextProperty, null));
        Assert.IsType<TemplateBindingExpression>(new TemplateBinding(Control.TagProperty)
            .CreateInstance(target, Control.TagProperty, null));
        Assert.IsType<TemplateBindingExpression>(new TemplateBinding()
            .CreateInstance(target, Control.TagProperty, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Trace_Matches_General_Expression_With_Reentrancy_Clear_And_Reparent(bool twoWay)
    {
        Assert.Equal(Trace(false, twoWay), Trace(true, twoWay));
    }

    [Fact]
    public void Higher_Priority_Values_And_Resume_Match_General_Expression()
    {
        Assert.Equal(PriorityTrace(false), PriorityTrace(true));
    }

    [Fact]
    public void Null_Is_A_Value_And_Not_A_Missing_Entry()
    {
        var parent = new TextBlock { Text = "value" };
        var target = new TextBlock { TemplatedParent = parent };
        using var binding = target.Bind(TextBlock.TextProperty, new TemplateBinding(TextBlock.TextProperty));
        Assert.Equal("value", target.Text);
        parent.Text = null;
        Assert.Null(target.Text);
        parent.Text = "next";
        Assert.Equal("next", target.Text);
    }

    [Fact]
    public void Disposed_Expression_No_Longer_Changes_The_Target()
    {
        var parent = new Node { Number = 1 };
        var target = new Node { TemplatedParent = parent };
        var binding = target.Bind(Node.NumberProperty, new TemplateBinding(Node.NumberProperty));
        binding.Dispose();
        parent.Number = 2;
        Assert.Equal(0, target.Number);
        target.TemplatedParent = new Node { Number = 3 };
        Assert.Equal(0, target.Number);
    }

    private static List<string> Trace(bool typed, bool twoWay)
    {
        var result = new List<string>();
        var parent = new Node { Number = 1 };
        var other = new Node { Number = 20 };
        var target = new Node { TemplatedParent = parent };
        var description = new TemplateBinding(Node.NumberProperty) { Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay };
        using var binding = Attach(target, description, typed);
        target.PropertyChanged += (_, e) =>
        {
            if (e.Property != Node.NumberProperty) return;
            result.Add($"target:{e.OldValue}:{e.NewValue}");
            if (target.Number == 2) parent.Number = 3;
        };
        parent.PropertyChanged += (_, e) =>
        {
            if (e.Property == Node.NumberProperty) result.Add($"source:{e.OldValue}:{e.NewValue}");
        };
        parent.Number = 2;
        parent.ClearValue(Node.NumberProperty);
        target.TemplatedParent = null;
        target.TemplatedParent = other;
        target.SetCurrentValue(Node.NumberProperty, 25);
        result.Add($"final:{target.Number}:{parent.Number}:{other.Number}");
        return result;
    }

    private static List<int> PriorityTrace(bool typed)
    {
        var parent = new Node { Number = 1 };
        var target = new Node { TemplatedParent = parent };
        using var style = target.SetValue(Node.NumberProperty, 99, BindingPriority.Style);
        using var binding = Attach(target, new TemplateBinding(Node.NumberProperty), typed);
        var values = new List<int> { target.Number };
        target.SetValue(Node.NumberProperty, 42);
        parent.Number = 2;
        values.Add(target.Number);
        target.ClearValue(Node.NumberProperty);
        values.Add(target.Number);
        target.TemplatedParent = null;
        values.Add(target.Number);
        target.TemplatedParent = parent;
        values.Add(target.Number);
        return values;
    }

    private static IDisposable Attach(Node target, TemplateBinding description, bool typed)
    {
        if (typed) return target.Bind(Node.NumberProperty, description);
        return target.GetValueStore().AddBinding(Node.NumberProperty,
            new TemplateBindingExpression(Node.NumberProperty, null, null, null, description.Mode));
    }

    private sealed class Node : Control
    {
        public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<Node, int>("Number");
        public int Number { get => GetValue(NumberProperty); set => SetValue(NumberProperty, value); }
    }
}
