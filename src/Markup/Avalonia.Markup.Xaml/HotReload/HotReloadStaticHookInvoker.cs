using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Discovers and runs methods annotated with <see cref="OnHotReloadAttribute"/>.
/// </summary>
internal static class HotReloadStaticHookInvoker
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<Type, MethodInfo[]> s_cache = new();

    [RequiresUnreferencedCode("Hot reload hook discovery requires reflection over runtime types.")]
    public static void Invoke(Type? type)
    {
        if (type is null)
            return;

        var methods = GetOrCreateMethods(type);
        if (methods.Length == 0)
            return;

        foreach (var method in methods)
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                HotReloadDiagnostics.ReportError(
                    "OnHotReload method '{0}.{1}' threw an exception.",
                    ex,
                    method.DeclaringType?.FullName ?? type.FullName ?? "<unknown>",
                    method.Name);
            }
        }
    }

    [RequiresUnreferencedCode("Hot reload hook discovery requires reflection over runtime types.")]
    private static MethodInfo[] GetOrCreateMethods(Type type)
    {
        lock (s_gate)
        {
            if (!s_cache.TryGetValue(type, out var methods))
            {
                methods = DiscoverMethods(type);
                s_cache[type] = methods;
            }

            return methods;
        }
    }

    [RequiresUnreferencedCode("Hot reload hook discovery requires reflection over runtime types.")]
    private static MethodInfo[] DiscoverMethods(Type type)
    {
        var result = new List<MethodInfo>();
        var current = type;

        while (current is not null && current != typeof(object))
        {
            foreach (var method in current.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(OnHotReloadAttribute), inherit: false))
                    continue;

                if (!method.IsStatic)
                {
                    HotReloadDiagnostics.ReportWarning(
                        "Ignoring OnHotReload method '{0}.{1}' because it is not static.",
                        method.DeclaringType?.FullName ?? current.FullName ?? "<unknown>",
                        method.Name);
                    continue;
                }

                if (method.GetParameters().Length != 0)
                {
                    HotReloadDiagnostics.ReportWarning(
                        "Ignoring OnHotReload method '{0}.{1}' because it declares parameters.",
                        method.DeclaringType?.FullName ?? current.FullName ?? "<unknown>",
                        method.Name);
                    continue;
                }

                result.Add(method);
            }

            current = current.BaseType;
        }

        return result.ToArray();
    }
}
