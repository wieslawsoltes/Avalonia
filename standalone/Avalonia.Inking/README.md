# Avalonia Inking

A standalone, pressure-aware vector inking system for **Avalonia 12.1.4 / .NET 10**, with reusable templated controls, replaceable light/dark themes, and a studio sample shared by desktop and WebAssembly. Version **0.1.0**.

This directory is self-contained and does not compile or modify the parent Avalonia framework. Validation evidence describes the exact tested capabilities; this release does not claim physical-digitizer certification or handwriting-recognition parity.

## Projects

| Project | Responsibility |
| --- | --- |
| `Avalonia.Inking.Core` | Immutable documents, seven-channel pen samples, brush geometry, spatial queries, selection, continuous erasing, transactional history, JSON and SVG. No Avalonia dependency. |
| `Avalonia.Inking` | `InkCanvas`, `InkToolbar`, `InkLayerPanel`, replaceable presenters, commands, pointer input, touch navigation, clipboard and PNG export. |
| `Avalonia.Inking.Themes` | Replaceable control templates and light/dark resource dictionaries. |
| `InkingStudio` | Shared studio UI and host storage abstraction. |
| `InkingStudio.Desktop` | Native desktop host and platform file pickers. |
| `InkingStudio.Browser` | Actual Avalonia WebAssembly application, browser storage adapters and sample-only diagnostics. |
| `Avalonia.Inking.Specs` | Independent executable regression suite, also executed inside the browser sample. |

## Capabilities

**Ink and input:** pressure-sensitive pen, tilt-aware vector pencil, translucent highlighter, pressure curves, elliptical nib geometry, tilt/twist retention, causal velocity-adaptive smoothing, taps, intermediate pointer samples, pen eraser/inversion/barrel-button handling, mouse, optional touch drawing, touch pan/pinch, a palm-rejection heuristic, pointer capture and cancellation.

**Editing:** whole-stroke and continuous partial erasing; rectangle and lasso selection; additive selection; translation, eight resize handles and a rotation handle; constrained transforms; keyboard nudging; duplicate/delete; front/back ordering; clipboard copy/cut/paste with fresh identities; line, rectangle and ellipse tools; square/circle/angle constraints; straightedge projection and grid snapping.

**Documents:** ordered layers with names, visibility, locks and opacity; immutable snapshots; one undo transaction per pointer gesture; reversible partial erasing; dirty/save-point tracking; bounded history; atomic validated imports; cancellation that preserves the redo branch; thread-affine mutation and notification reentrancy protection.

**Presentation:** lookless templated controls, styled properties, routed stroke/document/selection events, public commands, host-owned DataContext, independent default sessions, separate retained/live renderers, viewport culling, bounded geometry caches, adaptive paper grids, pan/zoom/fit, light/dark themes, keyboard shortcuts, tooltips and automation names.

**Files:** versioned reflection-free JSON, pressure-aware SVG outlines, PNG rendered through Avalonia, desktop file pickers, browser file input/downloads, and a debounced local recovery draft in the sample. The reusable library does not choose an application's storage location or silently persist documents.

## Build and run

Install the SDK in `global.json`. Run these commands **from this directory**, not the parent framework checkout.

```sh
# Core regressions.
dotnet run --project tests/Avalonia.Inking.Specs -c Release

# Desktop sample.
dotnet run --project samples/InkingStudio.Desktop -c Release

# Browser sample.
dotnet workload install wasm-tools
dotnet run --project samples/InkingStudio.Browser -c Release

# Publish and validate the actual WebAssembly application.
dotnet publish samples/InkingStudio.Browser -c Release -o artifacts/browser
python -m pip install -r validation/requirements.txt
python -m playwright install chromium --with-deps
python validation/browser.py

# Reusable NuGet packages.
dotnet pack src/Avalonia.Inking.Core -c Release -o artifacts/packages
dotnet pack src/Avalonia.Inking -c Release -o artifacts/packages
dotnet pack src/Avalonia.Inking.Themes -c Release -o artifacts/packages
```

Serve the published browser web root over HTTP(S), not `file://`. Keep the generated import map and fingerprinted framework filenames intact. The validation harness finds the real published root and serves WebAssembly with its correct MIME type. Clipboard and persistent browser storage remain subject to browser permissions and policy.

## Application integration

Reference `Avalonia.Inking` and `Avalonia.Inking.Themes` and add `InkTheme` after Fluent:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:inkThemes="using:Avalonia.Inking.Themes">
  <Application.Styles>
    <FluentTheme />
    <inkThemes:InkTheme />
  </Application.Styles>
</Application>
```

Expose an `InkSession` from the view model and compose the controls without replacing your DataContext:

```csharp
using Avalonia.Inking.Core;
public sealed class DrawingViewModel
{
    public InkSession Ink { get; } = new();
}
```

```xml
<Grid xmlns="https://github.com/avaloniaui"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ink="using:Avalonia.Inking"
      ColumnDefinitions="Auto,*,250">
  <ink:InkToolbar Target="{Binding #Drawing}" Margin="8" />
  <ink:InkCanvas x:Name="Drawing" Grid.Column="1"
                Session="{Binding Ink}" BrushSize="3"
                UsePressure="True" Smoothing="0.4" GridMode="Dots" />
  <ink:InkLayerPanel Grid.Column="2" Session="{Binding Ink}" Margin="16" />
</Grid>
```

For trimmed applications use compiled bindings and your view-model data type. The included studio and control themes use compiled bindings.

### Programmatic editing

```csharp
var session = new InkSession();
var stroke = new InkStroke(new[]
{
    new InkPoint(20, 30, Pressure: .2, TiltX: 10, Timestamp: 0),
    new InkPoint(80, 60, Pressure: .9, TiltX: 25, Timestamp: 16),
    new InkPoint(140, 35, Pressure: .4, TiltX: 15, Timestamp: 32)
}, InkPresets.Pen with { Color = 0xFF5265D9, Width = 8 });

session.AddStroke(stroke);
session.SetSelection(new[] { stroke.Id });
session.TransformSelection(InkTransform.Rotation(Math.PI / 12,
    session.SelectionBounds.Center));
session.Undo();

using (var edit = session.BeginTransaction("Grouped edit"))
{
    session.DuplicateSelection();
    session.AddLayer("Annotations");
    edit.Commit();
} // Without Commit(), disposal restores the exact previous state.

var json = InkSerializer.Serialize(session.Document);
var validated = InkSerializer.Deserialize(json);
session.Load(validated);
string svg = InkSerializer.ExportSvg(session.Document);
```

`InkExporter.ExportPng(document, scale: 2)` requires an initialized Avalonia rendering platform; JSON and SVG work with the independent core. For asynchronous saves capture `var snapshot = session.Document` and call `session.MarkSaved(snapshot)` only after your storage write succeeds. This avoids marking later edits clean. A browser adapter acknowledges download initiation, not proof that the user ultimately retained the file.

## Templating and behavior contracts

`InkCanvas`, `InkToolbar` and `InkLayerPanel` derive from `TemplatedControl`. `InkCanvas` delegates rendering to these required template parts:

| Part | Type | Purpose |
| --- | --- | --- |
| `PART_Presenter` | `InkPresenter` | Committed document ink and paper grid. |
| `PART_Adorner` | `InkAdornerPresenter` | Live ink, selection, handles, straightedge and eraser cursor. |

Preserve part names in replacement templates, keep presenters non-hit-testable, and retain a hit-testable background for pointer input. Retemplating or detaching cancels active gestures and releases pointer capture/subscriptions. Detached retained geometry caches are cleared.

Override palette resources `InkSurfaceBrush`, `InkPanelBrush`, `InkTextBrush`, `InkMutedBrush`, `InkBorderBrush`, `InkGridBrush`, `InkActiveBrush` and `InkAccentBrush`, or replace complete control themes. Canvas pseudo-classes include `:inking`, `:selecting`, `:erasing`, `:panning`, `:read-only` and `:has-selection`.

Important properties: `Session`, `Tool`, `InkColor`, `BrushSize`, `BrushOpacity`, `UsePressure`, `PressureCurve`, `Smoothing`, `EraserSize`, `Zoom`, `PanOffset`, `GridMode`, `GridSpacing`, `SnapToGrid`, `RulerEnabled`, `RulerAngle`, `RulerOrigin`, `AcceptsTouchInk`, `PalmRejection`, `IsReadOnly`, `ShowSelection`, `GridBrush` and `SelectionBrush`.

Handle `StrokeCollecting` to cancel a proposed stroke before insertion. `StrokeCollected`, `DocumentChanged` and `SelectionChanged` bubble. `OperationFailed` reports invalid or unsupported control operations. Commands are public and can be attached to application menus or ribbons.

Set `session.IsReadOnly` for a document-wide restriction. `canvas.IsReadOnly` restricts that view only; application code or another view may still edit the session. Layer locks and visibility are enforced in the editor, not only by disabled buttons.

Create and edit sessions on one thread, normally the Avalonia UI thread. Immutable documents may be retained for background serialization. Do not mutate a session synchronously inside its `Changed` event; dispatch the follow-on operation instead.

## Keyboard and navigation

| Input | Action |
| --- | --- |
| B / P / H | Pen / pencil / highlighter |
| E / Shift+E | Partial / whole-stroke eraser |
| V / L | Select / lasso |
| I / R / O | Line / rectangle / ellipse |
| Ctrl/Cmd+Z; Ctrl/Cmd+Shift+Z or Ctrl/Cmd+Y | Undo; redo |
| Ctrl/Cmd+A, C, X, V, D | Select all, copy, cut, paste, duplicate |
| Delete / Backspace | Delete selected editable ink |
| Arrow / Shift+Arrow | Nudge one / ten document units |
| Shift during a gesture | Constrain shapes, translation or rotation |
| Space+drag / middle-button drag | Pan |
| Ctrl/Cmd+wheel | Pointer-anchored zoom |
| Wheel / Shift+wheel | Vertical / horizontal pan |
| Ctrl/Cmd+0 | Reset view |
| Escape | Cancel active gesture |
| Touch / two fingers | Pan / pinch unless touch drawing is enabled |

## Validation and boundaries

`validation/browser.py` runs Chromium against the **published Avalonia WebAssembly application**. It executes the core suite again in WebAssembly, sends actual mouse/keyboard/CDP pen/touch input through Avalonia, checks resulting C# documents, exercises file pickers/downloads/clipboard, checks rendered pixels and observes a 10,000-stroke fixture. Sample-only `Probe` methods provide setup and observations, not replacement rendering or editing implementations.

CI preserves native/browser JSON reports, console output, screenshots, exported files, source identity and packages. Check `browser.json` and the workflow conclusion: publishing alone does not constitute browser validation.

See [architecture and limits](docs/architecture.md). This release does **not** include handwriting/OCR/ink-to-text, collaborative synchronization, native Windows Ink/ISF interoperability, PDF import or a textured brush physics engine. Pencil is a pressure/tilt-sensitive vector nib. Partial erasing conservatively expands by transformed nib radii rather than performing exact Boolean clipping of every outline. Synthetic browser pen tests are not physical digitizer, hardware latency or universal palm-classification certification. Desktop compilation does not certify every desktop platform/device.

MIT license. Third-party Avalonia and .NET components retain their respective licenses. No external service or API key is required for drawing, editing, persistence or validation.
