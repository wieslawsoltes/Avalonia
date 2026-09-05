# Avalonia.Themes.MacOS

A standalone macOS 26-inspired theme built from the complete Avalonia Fluent
control-template baseline. The runtime assembly does not reference Fluent.
It preserves the framework control contracts while providing a separate visual
identity, light/dark semantic palettes and strongly typed live design tokens.

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mac="using:Avalonia.Themes.MacOS">
  <Application.Styles>
    <mac:MacOSTheme />
  </Application.Styles>
</Application>
```

Use a package version built from the **same Avalonia revision** as your application.
This fork targets .NET 8 and .NET 10; it is not an Avalonia 11 compatibility shim.

```csharp
var theme = new Avalonia.Themes.MacOS.MacOSTheme();
Application.Current!.Styles.Add(theme);
theme.SetToken(MacOSTokens.ButtonPadding, new Thickness(16, 5));
theme.SetToken(MacOSSemanticTokens.Control, Color.Parse("#45454A"), ThemeVariant.Dark);
theme.ReduceTransparency = true;
```

Call live mutation APIs on the UI thread. Do not mutate default shared brushes;
replace a brush token or override its semantic color instead.

For ColorPicker, also include its independently packaged companion:
`avares://Avalonia.Controls.ColorPicker/Themes/MacOS/MacOS.xaml`.

The package contains `design-tokens/token-manifest.json`. The repository guide
is `docs/themes/macos26/README.md`, with control coverage and validation status.
Material brushes are a portable approximation, **not Apple's Liquid Glass renderer**.
Apple fonts and SF Symbols assets are not included.

Direct `dotnet pack` output is an intermediate package requiring a matching
framework feed. Production publishing uses the repository's existing Numerge
pipeline. See the repository guide for the isolated package-consumer test.
