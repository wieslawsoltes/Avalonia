# ControlCatalog Hot Reload Guide

This guide explains how the Avalonia ControlCatalog sample enables XAML hot reload, how to run it with diagnostics, and what components collaborate behind the scenes.

## Prerequisites

- .NET SDK 8.0.411 (the version pinned in `global.json`)
- Local build of the `Avalonia` repository, including generated hot-reload manifests (any `dotnet build` of the sample will produce them)

## Launching ControlCatalog with Hot Reload

1. From the repository root, start `dotnet watch` against the .NET Core sample:

   ```bash
   dotnet watch --project samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj run -- --hotreload
   ```

   The `--hotreload` flag ensures the application opts into runtime hot reload behavior.

2. Navigate to the page you plan to edit (for example, `TextBox` in the navigation pane). The runtime only tracks instances that have been constructed.

3. Modify and save `samples/ControlCatalog/Pages/TextBoxPage.xaml`. Within a second or two, the running ControlCatalog window updates in place.

### ControlCatalog-Specific Wiring

- `ControlCatalog.NetCore` copies the generated manifest into its output directory so the runtime can discover it automatically and references `Avalonia.Markup.Xaml.Loader` so runtime compilation is available (`samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj:27`).
- `App.Initialize` forces the hot reload service to spin up early, explicitly registers the manifest found in the output folder, logs the number of watched sources, and disables debug-only text layout pool verification to avoid false crashes during reload (`samples/ControlCatalog/App.xaml.cs:29`).
- `TextBoxPage` registers itself with the tracker after loading (`samples/ControlCatalog/Pages/TextBoxPage.xaml.cs:12`).
- Hot reload work now executes synchronously on the UI dispatcher with processing disabled, ensuring the entire app is paused during updates (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:472`).
- `Typeface.GlyphTypeface` now logs missing glyphs and falls back to the default font in Debug builds so dynamic reload doesn’t trigger fatal font-loading exceptions (`src/Avalonia.Base/Media/Typeface.cs:83`).
- The text formatting pool verification is skipped while the sample is running in Debug (`AVALONIA_DISABLE_TEXT_POOL_VERIFICATION=1`) to avoid false positives when the runtime swaps controls mid-layout (`src/Avalonia.Base/Media/TextFormatting/FormattingObjectPool.cs:22`).

## Enabling Diagnostics Output

Set `AVALONIA_HOTRELOAD_DIAGNOSTICS=1` (or `true`) to see the registration and reload log messages:

```bash
AVALONIA_HOTRELOAD_DIAGNOSTICS=1 \
dotnet watch --project samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj run -- --hotreload
```

You should see messages similar to:

- `Hot reload manifest path '…/ControlCatalog.axaml.hotreload.json' registered`
- `Tracking XAML source '…/Pages/TextBoxPage.xaml'`
- `Applying hot reload to 'ControlCatalog.Pages.TextBoxPage'`

If the manifest registration lines never appear, ensure the app creates the runtime service at startup (see “Runtime Initialization” below).

> **Tip:** Diagnostics are emitted through Avalonia’s logging pipeline. ControlCatalog doesn’t log to the console unless you opt in. Either run with both `AVALONIA_LOGGING_CONSOLE=1` and `AVALONIA_HOTRELOAD_DIAGNOSTICS=1`, or modify `Program.cs` to call `.LogToTrace()` so you can see the messages while debugging.

## Runtime Initialization Checklist

The following must happen for hot reload to work in any Avalonia app (including ControlCatalog):

- The assembly’s `*.axaml.hotreload.json` manifest is present alongside the binaries (`bin/Debug/net8.0` for the ControlCatalog projects).
- `AvaloniaXamlLoader` (or your startup code) calls `RuntimeHotReloadService.RegisterManifestPath` or `RuntimeHotReloadService.GetOrCreate()` once. ControlCatalog relies on `AvaloniaXamlLoader` to discover and register manifests automatically, but explicitly calling `RuntimeHotReloadService.GetOrCreate()` early in your app ensures registration happens even if no XAML is loaded yet.
- Live instances are tracked with `RuntimeHotReloadService.Track`. The runtime loader handles this automatically when you load XAML through `AvaloniaRuntimeXamlLoader`.

As soon as these conditions are met, the file watchers spin up and the service is ready to apply updates.

## Architecture Overview

Avalonia’s hot reload flow consists of coordinated build- and run-time stages:

### Build Pipeline

- While compiling XAML, the `HotReloadManifestWriter` emits a manifest (`*.axaml.hotreload.json`) that maps each `x:Class` to the generated builder metadata. For ControlCatalog you can inspect `samples/ControlCatalog/bin/Debug/net8.0/ControlCatalog.axaml.hotreload.json`.
- `build/Avalonia.HotReload.targets` copies those manifests into the application output so they ship with the assembly.

### Runtime Components

- `RuntimeHotReloadService` (singleton via `AvaloniaLocator`) discovers manifests, sets up file-system watchers, and orchestrates reload cycles. It keeps track of base directories, environment-provided manifest paths, and embedded manifests.
- `RuntimeHotReloadManager` caches metadata, compiles delegates to the generated build/populate methods, and keeps weak references to tracked instances so it can re-populate them after an update.
- `RuntimeHotReloadDelegateProvider` resolves the generated builder types (dynamic or static assemblies), creates delegates for build/populate invocations, and falls back to instantiate controls when only populate metadata is present.
- `RuntimeHotReloadManifest` loads/saves the JSON manifest files at runtime, so your app can refresh entries without rebuilding.
- `AvaloniaXamlLoader` automatically registers manifests: it checks for on-disk `.axaml.hotreload.json` files next to the assembly, then falls back to embedded resources in single-file or trimmed deployments.
- `RuntimeHotReloadMetadataUpdateHandler` integrates with .NET’s metadata update pipeline so `dotnet watch` / IDE hot reload triggers cause the Avalonia manifests to reload.

### Reload Loop

1. You edit and save `TextBoxPage.xaml`.
2. The `FileSystemWatcher` created by `RuntimeHotReloadService` notices the change and throttles duplicate notifications.
3. The file’s contents are read and passed to `AvaloniaRuntimeXamlLoader`. If parsing succeeds, the manager updates cached delegates and re-populates every tracked instance of `ControlCatalog.Pages.TextBoxPage`.
4. If your control implements `IXamlHotReloadable`, its `OnHotReload` hook runs at the end of the populate cycle for custom cleanup or state restoration.

## Troubleshooting Tips

- **No updates when editing XAML** – Check diagnostics output. If manifests are not registered, call `RuntimeHotReloadService.GetOrCreate()` during app startup. If files are watched but a page is not updating, make sure the instance is visible (or otherwise constructed) when you edit the XAML.
- **Build errors demanding `net8.0` assets for analyzers** – Avoid overriding the target framework (`-f`) when running `dotnet watch`; the analyzer projects intentionally target `netstandard2.0`.
- **Manifest path missing** – Verify `ControlCatalog.axaml.hotreload.json` exists in `samples/ControlCatalog/bin/Debug/net8.0`. Running a regular `dotnet build` (or the watch command once) regenerates it.
- **Debug pool assertions** – Hot reload can allocate text layout buffers while the DEBUG verification checks are active, so the ControlCatalog sample disables them by setting `AVALONIA_DISABLE_TEXT_POOL_VERIFICATION=1` during initialization.

## Further Reading

- `docs/hot-reload.md` (general Avalonia hot reload architecture)
- `src/Markup/Avalonia.Markup.Xaml/HotReload/` (runtime implementation)
- `build/Avalonia.HotReload.targets` (MSBuild setup)
