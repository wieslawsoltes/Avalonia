using System;

namespace Avalonia.Markup.Xaml;

/// <summary>Requests a direct, compile-time factory for a XAML resource in this assembly.</summary>
/// <remarks>
/// Apply this attribute to a static, parameterless method returning <see cref="object"/>.
/// The XAML compiler replaces its body with a call to the compiled resource constructor.
/// The resource URI must be an absolute avares URI identifying a public resource in the
/// same assembly. Missing resources and invalid factory signatures are build errors.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CompiledResourceFactoryAttribute : Attribute
{
    /// <summary>Initializes a compiled resource factory declaration.</summary>
    /// <param name="resourceUri">The absolute avares URI of the resource.</param>
    public CompiledResourceFactoryAttribute(string resourceUri) => ResourceUri = resourceUri;

    /// <summary>Gets the resource URI resolved by the XAML compiler.</summary>
    public string ResourceUri { get; }
}
