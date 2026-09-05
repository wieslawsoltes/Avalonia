using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avalonia.Themes.MacOS;

public partial class MacOSTheme
{
    /// <summary>Gets application overrides, including per-variant dictionaries.
    /// Use this dictionary rather than mutating shared default brushes.</summary>
    public ResourceDictionary Tokens { get; } = new();

    /// <summary>Overrides a design token. Call on the UI thread.</summary>
    /// <typeparam name="T">The resource value type.</typeparam>
    /// <param name="token">A key from <see cref="MacOSTokens"/>.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variant">Optional light/dark/custom variant; null overrides all variants.</param>
    public void SetToken<T>(MacOSToken<T> token, T value, ThemeVariant? variant = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(value);
        if (variant is null)
        {
            Tokens[token.Key] = value;
        }
        else
        {
            if (!Tokens.ThemeDictionaries.TryGetValue(variant, out var provider))
            {
                provider = new ResourceDictionary();
                Tokens.ThemeDictionaries.Add(variant, provider);
            }
            if (provider is not ResourceDictionary dictionary)
                throw new InvalidOperationException("The token variant must be a ResourceDictionary.");
            dictionary[token.Key] = value;
        }
    }

    /// <summary>Removes an override and restores the underlying theme resource.</summary>
    /// <typeparam name="T">The resource value type.</typeparam>
    /// <param name="token">The token to reset.</param>
    /// <param name="variant">The variant originally passed to SetToken.</param>
    /// <returns>Whether an override was removed.</returns>
    public bool ResetToken<T>(MacOSToken<T> token, ThemeVariant? variant = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(token);
        if (variant is null)
            return Tokens.Remove(token.Key);
        return Tokens.ThemeDictionaries.TryGetValue(variant, out var provider)
            && provider is ResourceDictionary dictionary && dictionary.Remove(token.Key);
    }
}
