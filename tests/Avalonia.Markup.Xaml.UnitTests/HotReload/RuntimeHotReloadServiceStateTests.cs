#if !NET47
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.HotReload;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.UnitTests;
using Xunit;

#nullable enable

namespace Avalonia.Markup.Xaml.UnitTests.HotReload;

public class RuntimeHotReloadServiceStateTests : ScopedTestBase
{
    private const string SnapshotOriginalXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Avalonia.Markup.Xaml.UnitTests.HotReload"
             x:Class="Avalonia.Markup.Xaml.UnitTests.HotReload.SnapshotHotReloadControl">
  <local:SnapshotHotReloadControl.Tags>
    <x:String>OriginalTag</x:String>
  </local:SnapshotHotReloadControl.Tags>
  <TextBlock Text="Original"/>
</UserControl>
""";

    private const string SnapshotUpdatedXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Avalonia.Markup.Xaml.UnitTests.HotReload"
             x:Class="Avalonia.Markup.Xaml.UnitTests.HotReload.SnapshotHotReloadControl">
  <local:SnapshotHotReloadControl.Tags>
    <x:String>UpdatedTag</x:String>
  </local:SnapshotHotReloadControl.Tags>
  <TextBlock Text="Updated"/>
</UserControl>
""";

    private const string CustomStateOriginalXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Avalonia.Markup.Xaml.UnitTests.HotReload"
             x:Class="Avalonia.Markup.Xaml.UnitTests.HotReload.CustomStateHotReloadControl">
  <TextBlock Text="Before"/>
</UserControl>
""";

    private const string CustomStateUpdatedXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Avalonia.Markup.Xaml.UnitTests.HotReload"
             x:Class="Avalonia.Markup.Xaml.UnitTests.HotReload.CustomStateHotReloadControl">
  <TextBlock Text="After"/>
</UserControl>
""";

    private static readonly MethodInfo s_captureSnapshotMethod = typeof(RuntimeHotReloadService)
        .GetMethod("CaptureInstanceSnapshot", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(RuntimeHotReloadService), "CaptureInstanceSnapshot");

    private static readonly MethodInfo s_restoreSnapshotMethod = typeof(RuntimeHotReloadService)
        .GetMethod("RestoreInstanceSnapshot", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(RuntimeHotReloadService), "RestoreInstanceSnapshot");

    private static RuntimeXamlLoaderConfiguration CreateConfiguration() => new()
    {
        LocalAssembly = typeof(RuntimeHotReloadServiceStateTests).Assembly
    };

    [Fact]
    public void HotReload_Preserves_ReadOnly_Collections()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var control = new SnapshotHotReloadControl();

        AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(control, SnapshotOriginalXaml), configuration);
        control.Tags.Add("RuntimeTag");
        var original = control.Tags.ToArray();

        var snapshot = CaptureSnapshot(control);
        Assert.NotNull(snapshot);

        AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(control, SnapshotUpdatedXaml), configuration);

        Assert.Contains("UpdatedTag", control.Tags);
        Assert.False(control.Tags.SequenceEqual(original));

        RestoreSnapshot(control, snapshot);

        Assert.True(control.Tags.SequenceEqual(original));
    }

    [Fact]
    public void HotReload_Invokes_Custom_State_Provider()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var control = new CustomStateHotReloadControl();

        AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(control, CustomStateOriginalXaml), configuration);
        control.SetCounter(42);

        var snapshot = CaptureSnapshot(control);
        Assert.NotNull(snapshot);
        var capturedState = control.LastCapturedState;
        Assert.NotNull(capturedState);

        control.SetCounter(0);
        AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(control, CustomStateUpdatedXaml), configuration);
        Assert.Equal(0, control.Counter);

        RestoreSnapshot(control, snapshot);

        Assert.Equal(42, control.Counter);
        Assert.Same(capturedState, control.LastRestoredState);
    }

    private static object? CaptureSnapshot(object instance)
        => s_captureSnapshotMethod.Invoke(null, new object?[] { instance });

    private static void RestoreSnapshot(object instance, object? snapshot)
        => s_restoreSnapshotMethod.Invoke(null, new object?[] { instance, snapshot });
}

public class SnapshotHotReloadControl : UserControl
{
    private readonly List<string> _tags = new();

    public IList<string> Tags => _tags;
}

public class CustomStateHotReloadControl : UserControl, IXamlHotReloadStateProvider
{
    private sealed record CounterSnapshot(int Counter);

    public int Counter { get; private set; }

    public object? LastCapturedState { get; private set; }

    public object? LastRestoredState { get; private set; }

    public void SetCounter(int value) => Counter = value;

    public object? CaptureHotReloadState()
    {
        var snapshot = new CounterSnapshot(Counter);
        LastCapturedState = snapshot;
        return snapshot;
    }

    public void RestoreHotReloadState(object? state)
    {
        LastRestoredState = state;
        if (state is CounterSnapshot snapshot)
            Counter = snapshot.Counter;
    }
}
#endif
