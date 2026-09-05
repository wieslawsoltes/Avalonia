namespace Avalonia.Themes.MacOS;

/// <summary>A strongly typed, stable design-token resource key.</summary>
/// <typeparam name="T">The resource value type.</typeparam>
public sealed class MacOSToken<T>
{
    internal MacOSToken(string key) => Key = key;

    /// <summary>Gets the XAML resource key.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public override string ToString() => Key;
}
