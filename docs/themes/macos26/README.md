# macOS 26 theme

## Scope and source provenance

`Avalonia.Themes.MacOS` is an independent theme assembly based on the complete
Fluent tree at `b709c58c6b1b8aa3b90866c7c001b7bf82b6353b`. The initial copy is a
separate commit; later commits isolate resources and implement the appearance.
The original Fluent files are not changed. All 83 implicit control themes in the
baseline are retained. ColorPicker has companion styles in its own assembly.
External controls such as separately distributed DataGrid/TreeDataGrid packages
are not implicitly covered by the built-in control inventory.

This work is a **macOS-inspired Avalonia theme**, not an AppKit control wrapper.
Behavior, accessibility peers, input routing, bindings, virtualization and popup
placement remain implemented by Avalonia controls. Existing contract preservation
is checked separately from appearance, rendering, and native platform validation.

## Installation and appearance

Reference `src/Avalonia.Themes.MacOS/Avalonia.Themes.MacOS.csproj` when working in
this source tree, or consume the matching build's NuGet package. Do not mix this
fork's development package with a different Avalonia release.

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mac="using:Avalonia.Themes.MacOS"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <mac:MacOSTheme DensityStyle="Normal" />
    <!-- Include only when using Avalonia.Controls.ColorPicker: -->
    <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/MacOS/MacOS.xaml" />
  </Application.Styles>
</Application>
```

`RequestedThemeVariant="Default"` follows the application/platform appearance;
`Light` and `Dark` opt into an explicit appearance. `ThemeVariantScope` can host
both appearances in one visual tree. System fonts are requested through
`$Default`; fonts and symbols licensed by Apple are never redistributed here.

## Design-token contract

The checked-in manifest `src/Avalonia.Themes.MacOS/Tokens/token-manifest.json`
is the authoritative inventory of keys, types, source dictionaries and baseline
control coverage. `MacOSTokens` exposes strongly typed keys. `MacOSSemanticTokens`
provides readable aliases for the shared semantic color and brush layer.

The initial migration exposes 998 inherited visual resources; additional semantic,
geometry and motion tokens bring the current inventory to 1,050 keys. Colors,
brushes, thicknesses, radii, font families/weights, sizes, grid lengths, durations
and transforms retain their actual CLR types. ControlTheme/converter identities
are deliberately not renamed into value tokens.

```csharp
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.MacOS;

// Keep the installed theme instance in an application service.
var theme = new MacOSTheme();
Application.Current!.Styles.Add(theme);

theme.SetToken(MacOSTokens.ButtonPadding, new Thickness(16, 5));
theme.SetToken(MacOSSemanticTokens.Control, Color.Parse("#45454A"), ThemeVariant.Dark);
theme.SetToken(MacOSTokens.SystemAccentColor, Color.Parse("#8E44AD"));
theme.SetToken(MacOSTokens.ButtonBackground, new SolidColorBrush(Colors.Beige));

theme.ResetToken(MacOSTokens.ButtonBackground);
theme.ResetToken(MacOSSemanticTokens.Control, ThemeVariant.Dark);
```

The corresponding XAML resource key is exposed by `.Key`, for example
`MacOS.ButtonPadding`, `MacOS.Color.Control`, and `MacOS.SystemAccentColor`.
`theme.Tokens` is a ResourceDictionary for applications preferring dictionary
configuration over the typed API. Per-variant dictionaries live in
`theme.Tokens.ThemeDictionaries`. Runtime mutations require the UI thread and
notify existing resource bindings. Theme-value references use DynamicResource;
applications should replace tokens rather than mutate default shared brushes.

Resource resolution order inside the theme is: accessibility policy; explicit
`Tokens` overrides; compact-density resources; normal theme resources. Accessible
foreground/contrast policy intentionally takes priority over decorative overrides.
Closer control-scoped resources follow normal Avalonia resource precedence.
Legacy Fluent-style value keys are normalized on theme lookup for compatibility
with existing application resources; new application code should use `MacOS.*`.

Template path geometries, binding expressions and named parts are implementation
contracts rather than arbitrary design-token values. Preserving these avoids
turning a color/spacing customization into a behavior change.

## Accessibility and materials

`IncreaseContrast` is nullable: null follows Avalonia's platform contrast setting;
true or false is an explicit application policy. The resource provider subscribes
only while owned and detaches when the theme is removed. It strengthens labels,
separators, control outlines and keyboard focus rings. Accent label color is
computed from the resolved accent, including custom bright accents.

`ReduceMotion` suppresses interaction/switch/split-view transition durations.
Functional progress indicators are not frozen. It does not currently suppress
every inherited navigation/progress animation. `ReduceTransparency` makes popup
material surfaces opaque. These two preferences are explicit properties: this
version does **not** automatically read macOS NSWorkspace motion/transparency
preferences; bind them to an application accessibility-preference service.

Materials currently use portable translucent/opaque semantic brushes. They do not
claim native Liquid Glass refraction, backdrop sampling, private Apple shaders,
or an embedded NSGlassEffectView. Native window vibrancy, title-bar appearance,
traffic lights, menu-bar integration and backend capabilities must be validated
separately on macOS. This distinction is a release requirement, not an implied
feature hidden behind a glass-named brush.

## ControlCatalog and screenshots

Select **MacOS** in the existing ControlCatalog theme selector and open the
**macOS 26** page. Other existing catalog pages use the same theme. The new
`ControlCatalog.MacOS` executable is a deterministic capture harness for the
actual ControlCatalog application and real page factories.

```sh
dotnet build src/Avalonia.Themes.MacOS/Avalonia.Themes.MacOS.csproj -c Release
dotnet run --project tests/Avalonia.Themes.MacOS.UnitTests -c Release
dotnet run --project samples/ControlCatalog.MacOS -c Release -- --output artifacts/macos-native
# Linux/CI without a display:
dotnet run --project samples/ControlCatalog.MacOS -c Release -- --headless --output artifacts/macos-headless
python3 eng/macos26/audit.py
```

For a source-native macOS build, build the repository's Avalonia.Native Xcode
project first, as the theme workflow does. Every screenshot manifest records
commit, OS, architecture, backend, appearance, density, accessibility flags,
client-area dimensions and SHA-256. Captures are RenderTargetBitmap renders of
running Avalonia controls, not generated artwork, browser mockups or desktop
screen grabs. Native popup/window-compositor surfaces require additional desktop
capture to establish their visual fidelity. Headless and native evidence must
never be mislabeled as interchangeable.

## Validation and release gates

See `PROGRESS.md` for evidence and `CONTROL-COVERAGE.md` for the baseline inventory.
A successful compile is not release certification. Before a stable release,
require passing runtime tests, reviewed light/dark captures, package consumption
against a matching framework build, native macOS 26 testing (including Retina,
keyboard navigation, VoiceOver, focus, popups and settings changes), and measured
accessibility contrast. Multi-control coverage does not imply every interaction
state was visually inspected.

## Design references

These inform the authored visual direction; none of their proprietary assets or
private design-token names are copied into the package.

- Apple Human Interface Guidelines, Materials: https://developer.apple.com/design/human-interface-guidelines/materials
- Apple, Adopting Liquid Glass: https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass
- Apple WWDC25, Meet Liquid Glass: https://developer.apple.com/videos/play/wwdc2025/219/
