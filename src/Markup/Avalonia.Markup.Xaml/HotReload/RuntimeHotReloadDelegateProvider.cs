using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Describes the dynamic builder type emitted by the runtime XAML compiler.
/// </summary>
public sealed record RuntimeHotReloadMetadata(
    string AssemblyName,
    string BuilderTypeName,
    string? PopulateMethodName,
    string? PopulateTargetTypeName,
    string? BuildMethodName,
    string? BuildReturnTypeName,
    string? SourcePath,
    string? RelativeSourcePath);

/// <summary>
/// Provides compiled delegates for building and populating XAML instances.
/// </summary>
public sealed record RuntimeHotReloadDelegates(
    Func<IServiceProvider, object> Build,
    Action<IServiceProvider, object> Populate);

/// <summary>
/// Creates delegates for dynamic builder types emitted by the runtime XAML compiler.
/// </summary>
public static class RuntimeHotReloadDelegateProvider
{
    /// <summary>
    /// Creates delegates that call into the generated build and populate methods.
    /// </summary>
    /// <param name="metadata">Metadata describing the builder type.</param>
    /// <param name="assemblyResolver">
    /// Optional assembly resolver. When omitted, scans loaded dynamic assemblies for the matching name.
    /// </param>
    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    public static RuntimeHotReloadDelegates CreateDelegates(
        RuntimeHotReloadMetadata metadata,
        Func<string, Assembly?>? assemblyResolver = null)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        assemblyResolver ??= ResolveAssembly;

        var builderType = GetBuilderType(metadata, assemblyResolver);

        var populateMethod = GetMethod(builderType, metadata.PopulateMethodName);
        var populateTargetType = ResolveType(metadata.PopulateTargetTypeName, populateMethod.GetParameters().ElementAtOrDefault(1)?.ParameterType);

        var populateDelegate = PopulateDelegate(populateMethod, populateTargetType);
        var buildDelegate = CreateBuildDelegate(metadata, builderType, populateTargetType, populateDelegate);

        return new RuntimeHotReloadDelegates(buildDelegate, populateDelegate);
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static Func<IServiceProvider, object> BuildDelegate(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type builderType,
        string? buildMethodName,
        string? buildReturnTypeName)
    {
        if (string.IsNullOrEmpty(buildMethodName))
            throw new InvalidOperationException("Build method name was not provided.");

        var buildMethod = GetMethod(builderType, buildMethodName);

        var serviceProviderParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        Expression call = Expression.Call(buildMethod, serviceProviderParameter);

        var expectedReturnType = ResolveType(buildReturnTypeName, buildMethod.ReturnType);
        if (expectedReturnType != typeof(object))
            call = Expression.Convert(call, typeof(object));

        return Expression.Lambda<Func<IServiceProvider, object>>(call, serviceProviderParameter).Compile();
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static Func<IServiceProvider, object> CreateBuildDelegate(
        RuntimeHotReloadMetadata metadata,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type builderType,
        Type populateTargetType,
        Action<IServiceProvider, object> populateDelegate)
    {
        if (!string.IsNullOrEmpty(metadata.BuildMethodName))
            return BuildDelegate(builderType, metadata.BuildMethodName, metadata.BuildReturnTypeName);

        var expectedReturnType = ResolveType(metadata.BuildReturnTypeName, populateTargetType);
        if (!expectedReturnType.IsAssignableFrom(populateTargetType))
        {
            throw new InvalidOperationException(
                $"Populate target '{populateTargetType.FullName}' is not assignable to build return type '{expectedReturnType.FullName}'.");
        }

        if (populateTargetType.IsAbstract)
            throw new InvalidOperationException(
                $"Unable to build instances of abstract type '{populateTargetType.FullName}' without a generated build method.");

        return serviceProvider =>
        {
            object instance;
            try
            {
                instance = Activator.CreateInstance(populateTargetType)!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create an instance of '{populateTargetType.FullName}' for hot reload. Provide a parameterless constructor or a build method.",
                    ex);
            }

            populateDelegate(serviceProvider, instance);
            return instance;
        };
    }

    private static Action<IServiceProvider, object> PopulateDelegate(MethodInfo method, Type targetType)
    {
        if (method is null)
            throw new ArgumentNullException(nameof(method));
        if (targetType is null)
            throw new ArgumentNullException(nameof(targetType));

        var serviceProviderParameter = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var targetParameter = Expression.Parameter(typeof(object), "target");

        var call = Expression.Call(
            method,
            serviceProviderParameter,
            Expression.Convert(targetParameter, targetType));

        return Expression
            .Lambda<Action<IServiceProvider, object>>(call, serviceProviderParameter, targetParameter)
            .Compile();
    }

    private static MethodInfo GetMethod([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type builderType, string? methodName)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new InvalidOperationException("Method name was not provided.");

        return builderType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   $"Method '{methodName}' was not found on builder type '{builderType.FullName}'.");
    }

    private static Assembly? ResolveAssembly(string assemblyName)
    {
        Assembly? staticMatch = null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName();
            if (!string.Equals(name.Name, assemblyName, StringComparison.Ordinal))
                continue;

            if (assembly.IsDynamic)
                return assembly;

            staticMatch ??= assembly;
        }

        if (staticMatch != null)
            return staticMatch;

        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch
        {
            return null;
        }
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static Type ResolveType(string? typeName, Type? fallback)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var resolved = Type.GetType(typeName, throwOnError: false);
            if (resolved != null)
                return resolved;

            resolved = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, throwOnError: false))
                .FirstOrDefault(t => t != null);

            if (resolved != null)
                return resolved;
        }

        if (fallback != null)
            return fallback;

        throw new InvalidOperationException($"Unable to resolve type '{typeName}'.");
    }

    [RequiresUnreferencedCode("Runtime hot reload requires dynamic access to generated builder types.")]
    private static Type GetBuilderType(RuntimeHotReloadMetadata metadata, Func<string, Assembly?> assemblyResolver)
    {
        var assembly = assemblyResolver(metadata.AssemblyName)
                       ?? throw new InvalidOperationException(
                           $"Unable to locate assembly '{metadata.AssemblyName}'.");

        var type = assembly.GetType(metadata.BuilderTypeName)
                   ?? throw new InvalidOperationException(
                       $"Builder type '{metadata.BuilderTypeName}' was not found in assembly '{assembly.FullName}'.");

        return type;
    }
}
