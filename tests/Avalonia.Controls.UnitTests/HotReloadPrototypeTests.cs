using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.HotReload;
using Avalonia.Markup.Xaml.HotReload;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.UnitTests;
#nullable enable

using Xunit;

namespace Avalonia.Controls.UnitTests;

public class HotReloadPrototypeTests : ScopedTestBase
{
    private const string OriginalXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Avalonia.Controls.UnitTests.HotReloadPrototypeControl">
  <TextBlock Text="Original"/>
</UserControl>
""";

    private const string UpdatedXaml = """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Avalonia.Controls.UnitTests.HotReloadPrototypeControl">
  <TextBlock Text="Updated"/>
</UserControl>
""";

    [Fact]
    public void RuntimeCompiler_Populates_Existing_XClass_Instance()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        var control = new HotReloadPrototypeControl();
        var configuration = CreateConfiguration();

        AvaloniaRuntimeXamlLoader.Load(
            new RuntimeXamlLoaderDocument(control, OriginalXaml),
            configuration);

        var textBlock = Assert.IsType<TextBlock>(control.Content);
        Assert.Equal("Original", textBlock.Text);

        AvaloniaRuntimeXamlLoader.Load(
            new RuntimeXamlLoaderDocument(control, UpdatedXaml),
            configuration);

        textBlock = Assert.IsType<TextBlock>(control.Content);
        Assert.Equal("Updated", textBlock.Text);
    }

    [Fact]
    public void RuntimeCompiler_Produces_Populate_Delegate_For_Reuse()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();

        var introspection = CaptureBuilderIntrospection(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        var populateMethod = Assert.IsAssignableFrom<MethodInfo>(introspection.PopulateMethod);
        Assert.Equal(typeof(IServiceProvider), populateMethod.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(HotReloadPrototypeControl), introspection.PopulateTargetType);

        var serviceProviderParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var targetParameterExpression = Expression.Parameter(typeof(object), "target");
        var callExpression = Expression.Call(
            populateMethod,
            serviceProviderParameter,
            Expression.Convert(targetParameterExpression, introspection.PopulateTargetType));
        var populate = Expression
            .Lambda<Action<IServiceProvider, object>>(callExpression, serviceProviderParameter, targetParameterExpression)
            .Compile();

        var serviceProvider = XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null);
        var control = new HotReloadPrototypeControl();

        populate(serviceProvider, control);

        var textBlock = Assert.IsType<TextBlock>(control.Content);
        Assert.Equal("Original", textBlock.Text);
    }

    [Fact]
    public void RuntimeCompiler_Produces_Build_Delegate_For_Reuse()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();

        var introspection = CaptureBuilderIntrospection(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        var buildMethod = Assert.IsAssignableFrom<MethodInfo>(introspection.BuildMethod);
        Assert.Equal(typeof(HotReloadPrototypeControl), buildMethod.ReturnType);

        var serviceProviderParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var callExpression = Expression.Convert(
            Expression.Call(buildMethod, serviceProviderParameter),
            typeof(object));
        var build = Expression
            .Lambda<Func<IServiceProvider, object>>(callExpression, serviceProviderParameter)
            .Compile();

        var serviceProvider = XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null);
        var created1 = build(serviceProvider);
        var created2 = build(serviceProvider);

        Assert.IsType<HotReloadPrototypeControl>(created1);
        Assert.IsType<HotReloadPrototypeControl>(created2);
        Assert.NotSame(created1, created2);
        Assert.Equal("Original", Assert.IsType<TextBlock>(((UserControl)created1).Content).Text);
        Assert.Equal("Original", Assert.IsType<TextBlock>(((UserControl)created2).Content).Text);
    }

    [Fact]
    public void RuntimeCompiler_Emits_Metadata_Snapshot()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();

        var metadata = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        Assert.Equal(typeof(HotReloadPrototypeControl).FullName, metadata.PopulateTargetTypeName);
        Assert.Equal("__AvaloniaXamlIlPopulate", metadata.PopulateMethodName);
        Assert.Equal("__AvaloniaXamlIlBuild", metadata.BuildMethodName);
        Assert.Equal(typeof(HotReloadPrototypeControl).FullName, metadata.BuildReturnTypeName);
        Assert.StartsWith("Builder_", metadata.BuilderTypeName);

        var manifestPath = Path.Combine(Path.GetTempPath(), $"avalonia-hotreload-{Guid.NewGuid():N}.json");
        try
        {
            RuntimeHotReloadManifest.Save(manifestPath, new Dictionary<string, RuntimeHotReloadMetadata>
            {
                [typeof(HotReloadPrototypeControl).FullName!] = metadata
            });

            var loaded = RuntimeHotReloadManifest.Load(manifestPath);
            Assert.True(loaded.TryGetValue(typeof(HotReloadPrototypeControl).FullName!, out var loadedEntry));
            Assert.Equal(metadata, loadedEntry);
        }
        finally
        {
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
        }
    }

    [Fact]
    public void RuntimeHotReloadManager_Builds_And_Populates()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var controlKey = typeof(HotReloadPrototypeControl).FullName!;

        var originalMetadata = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        var manager = new RuntimeHotReloadManager();
        manager.Register(controlKey, originalMetadata);

        var created = (HotReloadPrototypeControl)manager.Build(controlKey);
        Assert.Equal("Original", Assert.IsType<TextBlock>(created.Content).Text);

        var existing = new HotReloadPrototypeControl();
        manager.Populate(controlKey, existing);
        Assert.Equal("Original", Assert.IsType<TextBlock>(existing.Content).Text);
        var updatedMetadata = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(UpdatedXaml), configuration));
        manager.Register(controlKey, updatedMetadata);

        var updatedInstance = (HotReloadPrototypeControl)manager.Build(controlKey);
        Assert.Equal("Updated", Assert.IsType<TextBlock>(updatedInstance.Content).Text);

        Assert.Equal("Updated", Assert.IsType<TextBlock>(existing.Content).Text);
    }

    [Fact]
    public void RuntimeHotReloadService_Provides_Singleton_Manager()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        var first = RuntimeHotReloadService.GetOrCreate();
        var second = RuntimeHotReloadService.GetOrCreate();

        Assert.Same(first, second);

        var configuration = CreateConfiguration();
        var key = typeof(HotReloadPrototypeControl).FullName!;
        RuntimeHotReloadService.Register(key, CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration)));

        var control = (HotReloadPrototypeControl)RuntimeHotReloadService.GetOrCreate().Build(key);
        Assert.Equal("Original", Assert.IsType<TextBlock>(control.Content).Text);
    }

    [Fact]
    public void RuntimeBuilderManifest_Provides_Runtime_Delegates()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var metadata = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        var delegates = RuntimeHotReloadDelegateProvider.CreateDelegates(metadata);

        var serviceProvider = XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null);
        var control = (HotReloadPrototypeControl)delegates.Build(serviceProvider);
        Assert.Equal("Original", Assert.IsType<TextBlock>(control.Content).Text);

        control.Content = null;
        delegates.Populate(serviceProvider, control);
        Assert.Equal("Original", Assert.IsType<TextBlock>(control.Content).Text);
    }

    [Fact]
    public void RuntimeHotReloadService_RegisterManifest_Loads_Metadata()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var key = typeof(HotReloadPrototypeControl).FullName!;

        var manifestPath = Path.Combine(Path.GetTempPath(), $"avalonia-hotreload-{Guid.NewGuid():N}.json");
        try
        {
            var manifest = new Dictionary<string, RuntimeHotReloadMetadata>
            {
                [key] = CaptureBuilderMetadata(() =>
                    AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration))
            };

            RuntimeHotReloadManifest.Save(manifestPath, manifest);
            RuntimeHotReloadService.RegisterManifest(manifestPath);

            var manager = RuntimeHotReloadService.GetOrCreate();
            var control = (HotReloadPrototypeControl)manager.Build(key);
            Assert.Equal("Original", Assert.IsType<TextBlock>(control.Content).Text);

            manifest[key] = CaptureBuilderMetadata(() =>
                AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(UpdatedXaml), configuration));
            RuntimeHotReloadManifest.Save(manifestPath, manifest);
            RuntimeHotReloadService.RegisterManifest(manifestPath);

            Assert.Equal("Updated", Assert.IsType<TextBlock>(control.Content).Text);
        }
        finally
        {
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
        }
    }

    [Fact]
    public void RuntimeHotReloadService_Reloads_Registered_Providers()
    {
        using var app = UnitTestApplication.Start(TestServices.MockThreadingInterface);
        var configuration = CreateConfiguration();
        var key = typeof(HotReloadPrototypeControl).FullName!;

        RuntimeHotReloadService.ClearManifestProviders();
        var manifest = new Dictionary<string, RuntimeHotReloadMetadata>();
        RuntimeHotReloadService.RegisterManifestProvider(() => manifest);

        manifest[key] = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(OriginalXaml), configuration));

        RuntimeHotReloadService.ReloadRegisteredManifests();

        var manager = RuntimeHotReloadService.GetOrCreate();
        var control = (HotReloadPrototypeControl)manager.Build(key);
        Assert.Equal("Original", Assert.IsType<TextBlock>(control.Content).Text);

        manifest[key] = CaptureBuilderMetadata(() =>
            AvaloniaRuntimeXamlLoader.Load(new RuntimeXamlLoaderDocument(UpdatedXaml), configuration));

        RuntimeHotReloadService.ReloadRegisteredManifests();
        Assert.Equal("Updated", Assert.IsType<TextBlock>(control.Content).Text);
    }

    private static RuntimeBuilderIntrospection CaptureBuilderIntrospection(Func<object> loader)
    {
        var (builderType, _) = CaptureBuilderType(loader);
        var populateMethod = builderType.GetMethod("__AvaloniaXamlIlPopulate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var buildMethod = builderType.GetMethod("__AvaloniaXamlIlBuild",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var populateTarget = populateMethod?.GetParameters().ElementAtOrDefault(1)?.ParameterType
                             ?? typeof(object);
        return new RuntimeBuilderIntrospection(builderType, populateMethod, buildMethod, populateTarget);
    }

    private static RuntimeHotReloadMetadata CaptureBuilderMetadata(Func<object> loader)
    {
        var introspection = CaptureBuilderIntrospection(loader);
        var builderType = introspection.BuilderType;
        var assemblyName = builderType.Assembly.GetName().Name
                           ?? builderType.Assembly.FullName
                           ?? string.Empty;
        return new RuntimeHotReloadMetadata(
            assemblyName,
            builderType.FullName ?? builderType.Name,
            introspection.PopulateMethod?.Name,
            introspection.PopulateTargetType.FullName ?? introspection.PopulateTargetType.Name,
            introspection.BuildMethod?.Name,
            introspection.BuildMethod?.ReturnType.FullName ?? introspection.BuildMethod?.ReturnType.Name,
            null,
            null);
    }

    private static RuntimeXamlLoaderConfiguration CreateConfiguration() =>
        new()
        {
            LocalAssembly = typeof(HotReloadPrototypeControl).Assembly
        };

    private static (Type builderType, object createdInstance) CaptureBuilderType(Func<object> loader)
    {
        var before = new HashSet<Type>(GetDynamicPopulateTypes());
        var created = loader();
        var after = new HashSet<Type>(GetDynamicPopulateTypes());
        after.ExceptWith(before);
        var builderType = Assert.Single(after);
        return (builderType, created);
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
                {
                    yield return type;
                }
            }
        }
    }

    private sealed record RuntimeBuilderIntrospection(
        Type BuilderType,
        MethodInfo? PopulateMethod,
        MethodInfo? BuildMethod,
        Type PopulateTargetType);

}

public class HotReloadPrototypeControl : UserControl
{
    public HotReloadPrototypeControl()
    {
    }
}
