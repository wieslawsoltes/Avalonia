#if !NET47
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.HotReload;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.UnitTests;
using Avalonia.Threading;
using Xunit;

namespace Avalonia.Markup.Xaml.UnitTests.HotReload;

public class RuntimeHotReloadServiceStressTests : ScopedTestBase
{
    private const string XamlTemplate = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Avalonia.Markup.Xaml.UnitTests.HotReload.StressHotReloadControl">
  <TextBlock Text="{0}"/>
</UserControl>
""";

    [Fact]
    public async Task HotReload_Completes_Long_Edit_Session()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        RuntimeHotReloadService.ClearManifestProviders();

        var configuration = CreateConfiguration();
        var controlKey = typeof(StressHotReloadControl).FullName!;
        var control = new StressHotReloadControl();
        RuntimeHotReloadService.Track(control);

        try
        {
            var metadata = CaptureBuilderMetadata(() =>
                AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(FormatXaml("Initial")), configuration));

            metadata = metadata with
            {
                SourcePath = null,
                RelativeSourcePath = null
            };

            RuntimeHotReloadService.Register(controlKey, metadata);

            AvaloniaRuntimeXamlLoader.Load(
                new RuntimeXamlLoaderDocument(control, FormatXaml("Initial")),
                configuration);

            const int iterations = 5;
            for (int i = 0; i < iterations; i++)
            {
                var content = $"Iteration {i}";
                await RuntimeHotReloadService.ApplyHotReloadAsync(controlKey, FormatXaml(content));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var textBlock = Assert.IsType<TextBlock>(control.Content);
                Assert.Equal($"Iteration {iterations - 1}", textBlock.Text);
            }, DispatcherPriority.Send);
        }
        finally
        {
            RuntimeHotReloadService.ClearManifestProviders();
        }
    }

    private static RuntimeXamlLoaderConfiguration CreateConfiguration() =>
        new()
        {
            LocalAssembly = typeof(StressHotReloadControl).Assembly
        };

    private static RuntimeHotReloadMetadata CaptureBuilderMetadata(Func<object> loader)
    {
        var before = new HashSet<Type>(GetDynamicPopulateTypes());
        loader();
        var after = new HashSet<Type>(GetDynamicPopulateTypes());
        after.ExceptWith(before);
        var builderType = Assert.Single(after);

        var populateMethod = builderType.GetMethod("__AvaloniaXamlIlPopulate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var buildMethod = builderType.GetMethod("__AvaloniaXamlIlBuild",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var populateTarget = populateMethod?.GetParameters().ElementAtOrDefault(1)?.ParameterType ?? typeof(object);

        var assemblyName = builderType.Assembly.GetName().Name ?? builderType.Assembly.FullName ?? string.Empty;
        return new RuntimeHotReloadMetadata(
            assemblyName,
            builderType.FullName ?? builderType.Name,
            populateMethod?.Name,
            populateTarget.FullName ?? populateTarget.Name,
            buildMethod?.Name,
            buildMethod?.ReturnType.FullName ?? buildMethod?.ReturnType.Name,
            null,
            null);
    }

    private static IEnumerable<Type> GetDynamicPopulateTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();
            }

            foreach (var type in types)
            {
                if (type.GetMethod("__AvaloniaXamlIlPopulate",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is not null)
                    yield return type;
            }
        }
    }

    private static string FormatXaml(string text) => string.Format(XamlTemplate, text);

}

public class StressHotReloadControl : UserControl
{
}
#endif
