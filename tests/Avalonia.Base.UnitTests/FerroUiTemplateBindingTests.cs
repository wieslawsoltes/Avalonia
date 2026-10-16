using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiTemplateBindingTests
{
    [Fact]
    public void Identity_Template_Binding_Preserves_Clear_Detach_Reattach_Sequence()
    {
        var parent = new Node { Number = 1 };
        var target = new Node { TemplatedParent = parent };
        using var binding = target.Bind(Node.NumberProperty, new TemplateBinding(Node.NumberProperty));
        var trace = new List<object?>();
        target.PropertyChanged += (_, e) => { if (e.Property == Node.NumberProperty) trace.Add(e.NewValue); };
        parent.Number = 2;
        parent.ClearValue(Node.NumberProperty);
        parent.Number = 3;
        target.TemplatedParent = null;
        target.TemplatedParent = parent;
        Assert.Equal(new object?[] { 2, 0, 3, 0, 3 }, trace);
    }

    [Fact]
    public void Same_Type_Converter_Is_Not_Bypassed()
    {
        var parent = new Node { Number = 1 };
        var target = new Node { TemplatedParent = parent };
        var calls = 0;
        using var binding = target.Bind(Node.NumberProperty, new TemplateBinding(Node.NumberProperty)
        {
            Converter = new FuncValueConverter<int, int>(v => { ++calls; return v + 10; })
        });
        Assert.Equal(11, target.Number);
        parent.Number = 2;
        Assert.Equal(12, target.Number);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Different_Type_Conversion_And_TwoWay_Writeback_Are_Preserved()
    {
        var parent = new Node { Number = 7 };
        var text = new TextBlock { TemplatedParent = parent };
        using var conversion = text.Bind(TextBlock.TextProperty, new TemplateBinding(Node.NumberProperty));
        Assert.Equal("7", text.Text);
        var target = new Node { TemplatedParent = parent };
        using var twoWay = target.Bind(Node.NumberProperty, new TemplateBinding(Node.NumberProperty) { Mode = BindingMode.TwoWay });
        target.SetCurrentValue(Node.NumberProperty, 8);
        Assert.Equal(8, parent.Number);
        Assert.Equal("8", text.Text);
    }

    private sealed class Node : Control
    {
        public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<Node, int>("Number");
        public int Number { get => GetValue(NumberProperty); set => SetValue(NumberProperty, value); }
    }
}
