using System;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Identifies a static method that should be invoked whenever hot reload applies updates to the declaring type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnHotReloadAttribute : Attribute
{
}
