using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FerroUiSharedBindingPathTests
{
    [Fact]
    public void Separate_Descriptions_Share_Syntax_But_Not_Source_Values()
    {
        var first = new ReflectionBinding("Tag") { Source = new Control { Tag = "first" } };
        var second = new ReflectionBinding(new string("Tag".ToCharArray()))
        {
            Source = new Control { Tag = "second" }
        };
        var a = new TextBlock();
        var b = new TextBlock();
        using var firstBinding = a.Bind(TextBlock.TextProperty, first);
        using var secondBinding = b.Bind(TextBlock.TextProperty, second);
        Assert.Equal("first", a.Text);
        Assert.Equal("second", b.Text);
        var field = typeof(ReflectionBinding).GetField("_parsedPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Same(field.GetValue(first), field.GetValue(second));
        ((Control)first.Source!).Tag = "changed";
        Assert.Equal("changed", a.Text);
        Assert.Equal("second", b.Text);
    }

    [Fact]
    public void Shared_Syntax_Cache_Is_Bounded()
    {
        for (var i = 0; i < 512; ++i)
            Parse("FerroUiBoundedPath" + i);
        var cache = (IDictionary)typeof(ReflectionBinding)
            .GetField("s_pathCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.InRange(cache.Count, 1, 128);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Large_Paths_Or_Syntax_Trees_Are_Not_Globally_Retained(bool longPath)
    {
        var path = longPath ? new string('A', 257) : string.Join(".", Enumerable.Repeat("A", 33));
        Assert.NotSame(Parse(path), Parse(path));
    }

    private static object Parse(string path) => typeof(ReflectionBinding)
        .GetMethod("GetParsedPath", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { path })!;
}
