using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Core.Parsers;
using Avalonia.Utilities;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiReflectionBindingTests
{
    [Fact]
    public void Reused_Description_Has_Independent_Sources_Observers_And_TwoWay_Writes()
    {
        var firstSource = new Model { Name = "first" };
        var secondSource = new Model { Name = "second" };
        var first = new TextBlock { DataContext = firstSource };
        var second = new TextBlock { DataContext = secondSource };
        var description = new ReflectionBinding("Name") { Mode = BindingMode.TwoWay };
        using var firstBinding = first.Bind(TextBlock.TextProperty, description);
        using var secondBinding = second.Bind(TextBlock.TextProperty, description);
        Assert.Equal("first", first.Text);
        Assert.Equal("second", second.Text);
        first.SetCurrentValue(TextBlock.TextProperty, "written");
        Assert.Equal("written", firstSource.Name);
        Assert.Equal("second", secondSource.Name);
        firstBinding.Dispose();
        secondSource.Name = "still subscribed";
        Assert.Equal("still subscribed", second.Text);
        Assert.Equal(0, firstSource.ListenerCount);
        Assert.True(secondSource.ListenerCount > 0);
    }

    [Fact]
    public void Syntax_Is_Reused_By_Value_And_Replaced_When_Path_Changes()
    {
        var source = new Model { Name = "one", Other = "two" };
        var target = new TextBlock();
        var description = new ReflectionBinding("Name") { Source = source };
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("one", target.Text);
        var syntax = GetSyntax(description);
        description.Path = new string("Name".ToCharArray());
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("one", target.Text);
        Assert.Same(syntax, GetSyntax(description));
        description.Path = "Other";
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("two", target.Text);
        Assert.NotSame(syntax, GetSyntax(description));
        description.Path = "Name.";
        Assert.ThrowsAny<Exception>(() => target.Bind(TextBlock.TextProperty, description));
        description.Path = "Name";
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("one", target.Text);
    }

    [Fact]
    public void Cached_Syntax_Does_Not_Cache_Type_Resolution()
    {
        var source = new Control();
        source.SetValue(FirstOwner.LabelProperty, "first");
        source.SetValue(SecondOwner.LabelProperty, "second");
        var calls = 0;
        var type = typeof(FirstOwner);
        var description = new ReflectionBinding("(test:Owner.Label)")
        {
            Source = source,
            TypeResolver = (_, _) => { ++calls; return type; }
        };
        var target = new TextBlock();
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("first", target.Text);
        type = typeof(SecondOwner);
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("second", target.Text);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Reentrant_Type_Resolution_Cannot_Overwrite_The_Outer_Syntax()
    {
        var source = new Control();
        source.SetValue(FirstOwner.LabelProperty, "label");
        var description = new ReflectionBinding("(test:Owner.Label).Length")
        {
            Source = source,
            TypeResolver = (_, _) =>
            {
                // The grammar's scratch-list API is also used by other parser clients.
                var reader = new CharacterReader("Unrelated.Path".AsSpan());
                BindingExpressionGrammar.ParseToPooledList(ref reader);
                return typeof(FirstOwner);
            }
        };
        var target = new Control();
        for (var i = 0; i < 2; ++i)
            using (target.Bind(Control.TagProperty, description))
                Assert.Equal(5, target.Tag);
    }

    [Fact]
    public void NameScope_Is_Resolved_For_Each_Instance()
    {
        var firstScope = new NameScope();
        var secondScope = new NameScope();
        firstScope.Register("part", new Control { Tag = "first" });
        secondScope.Register("part", new Control { Tag = "second" });
        var description = new ReflectionBinding("#part.Tag")
        {
            NameScope = new WeakReference<INameScope?>(firstScope)
        };
        var target = new TextBlock();
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("first", target.Text);
        description.NameScope = new WeakReference<INameScope?>(secondScope);
        using (target.Bind(TextBlock.TextProperty, description))
            Assert.Equal("second", target.Text);
    }

    [Fact]
    public void Indexer_Conversion_State_Is_Not_Shared_Between_Targets()
    {
        var description = new ReflectionBinding("[1]");
        var first = new TextBlock { DataContext = new[] { "zero", "one" } };
        var second = new TextBlock { DataContext = new Dictionary<string, string> { ["1"] = "string key" } };
        using var a = first.Bind(TextBlock.TextProperty, description);
        using var b = second.Bind(TextBlock.TextProperty, description);
        Assert.Equal("one", first.Text);
        Assert.Equal("string key", second.Text);
        first.DataContext = new[] { "zero", "recycled" };
        Assert.Equal("recycled", first.Text);
        Assert.Equal("string key", second.Text);
    }

    private static object GetSyntax(ReflectionBinding binding) =>
        typeof(ReflectionBinding).GetField("_parsedPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!;

    private sealed class FirstOwner : AvaloniaObject
    {
        public static readonly AttachedProperty<string?> LabelProperty =
            AvaloniaProperty.RegisterAttached<FirstOwner, Control, string?>("Label");
    }

    private sealed class SecondOwner : AvaloniaObject
    {
        public static readonly AttachedProperty<string?> LabelProperty =
            AvaloniaProperty.RegisterAttached<SecondOwner, Control, string?>("Label");
    }

    public sealed class Model : INotifyPropertyChanged
    {
        private string? _name;
        private PropertyChangedEventHandler? _changed;
        public int ListenerCount { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _changed += value; ++ListenerCount; }
            remove { _changed -= value; --ListenerCount; }
        }
        public string? Name
        {
            get => _name;
            set { _name = value; _changed?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }
        public string? Other { get; set; }
    }
}
