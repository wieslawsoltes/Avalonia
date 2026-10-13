using System;
using Avalonia.Markup.Xaml;

namespace PInvoke;

public static class ResourceFactories
{
    [CompiledResourceFactory("avares://PInvoke/FactoryResources.axaml")]
    public static object CreateDictionary() => throw new InvalidOperationException("Not compiled.");

    [CompiledResourceFactory("avares://PInvoke/MainWindow.axaml")]
    public static object CreateWindow() => throw new InvalidOperationException("Not compiled.");
}
