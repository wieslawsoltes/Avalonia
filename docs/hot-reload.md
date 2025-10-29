# Avalonia XAML Hot Reload

This document describes the runtime and build infrastructure that powers Avalonia’s XAML hot reload experience, and explains how to configure or extend it for specific applications, libraries, and tooling scenarios.

## Overview

The Avalonia runtime is capable of reloading XAML pages in-place without restarting the application. The workflow is composed of the following pieces:

1. **Hot reload manifests** are generated at build time for every assembly that contains compiled XAML. Each manifest is a JSON file (`*.axaml.hotreload.json`) that maps an `x:Class` name to the dynamic builder metadata emitted by the XAML compiler.
2. **Runtime discovery and tracking** is handled by `RuntimeHotReloadService`, which locates manifests, tracks live instances, and provides APIs to apply hot updates.
3. **Loader integration** ensures that manifests are discovered automatically when XAML is loaded at runtime and that edit sessions can reload the correct XAML.
4. **Tooling coordination** (for example dotnet-watch) ensures that edited source files are sent to the runtime without incurring expensive rebuild loops.

The sections below outline each component and the relevant configuration points.

## Build Pipeline

The MSBuild target `build/Avalonia.HotReload.targets` is imported by the Avalonia packages. It coordinates manifest generation and copies the manifests into the consuming application so the runtime can register them.

Key behaviours:

- Manifest generation occurs as part of the XAML compiler task. The `HotReloadManifestWriter` creates a JSON file alongside the intermediate assembly.
- The `AvaloniaCopyHotReloadManifests` target copies manifests into the output directory. When `DotNetWatchBuild=true` (i.e. when dotnet-watch is generating its watch list) the copy is skipped to avoid feeding build outputs back into the watch pipeline.
- By default only `.axaml` / `.xaml` resources are added to the `Watch` item list. You can opt back into watching all assets by setting `AvaloniaHotReloadWatchAssets=true` in a project file.
- The `Watch` items themselves are disabled unless you explicitly set `AvaloniaHotReloadEnableDotNetWatchRebuild=true`. This avoids dotnet-watch rebuild loops; runtime file watchers handle the live updates.

### Opt-in properties

Add these properties to a project that should override the defaults:

```xml
<!-- Enable dotnet-watch rebuilds for this project -->
<AvaloniaHotReloadEnableDotNetWatchRebuild>true</AvaloniaHotReloadEnableDotNetWatchRebuild>

<!-- Also watch non-XAML resources -->
<AvaloniaHotReloadWatchAssets>true</AvaloniaHotReloadWatchAssets>
```

## Runtime Components

- `RuntimeHotReloadService` registers manifests, tracks live instances, exposes a file watcher that reloads XAML when source files change, and snapshots mutable state (including read-only lists/dictionaries) so user interactions survive the reload.
- `RuntimeHotReloadManager` manages dynamic delegates for the generated builder types and invokes `IXamlHotReloadable.OnHotReload()` hooks.
- `RuntimeHotReloadDelegateProvider` reflects the dynamic assembly emitted by the runtime XAML compiler to obtain `Build` / `Populate` delegates.
- `RuntimeHotReloadMetadataUpdateHandler` hooks the .NET metadata update pipeline and clears cached delegates when Roslyn applies an edit-and-continue update.
- `AvaloniaXamlLoader` now registers both on-disk and embedded hot reload manifests. If the `.axaml.hotreload.json` is embedded into the assembly (for example in single-file distributions) the loader registers a manifest provider that re-hydrates the JSON stream on each update cycle.
- `IXamlHotReloadStateProvider` is an optional interface that controls can implement to capture and restore bespoke state if the automatic property/collection snapshots are not sufficient.
- `IHotReloadableView` works together with `IHotReloadViewHandler` to provide pre/post reload hooks. Avalonia assigns a `DefaultHotReloadViewHandler` that reapplies templates, invalidates layout, and refreshes visuals; controls can swap in a custom handler when additional platform work is required.

### Opt-in State Preservation

Most views can rely on the automatic property/collection snapshotting added to `RuntimeHotReloadService`, but some controls carry richer runtime state that deserves explicit handling. Good candidates for implementing `IXamlHotReloadStateProvider` include:

- `SelectingItemsControl` derivatives (`ListBox`, `ComboBox`, `NavigationView`) to keep the user’s current selection.
- `TabControl` and `TreeView` to retain which tabs or nodes are expanded.
- `DataGrid` to preserve column order, sort descriptors, and the current cell.
- Composite layouts such as `SplitView`, `Grid` + `GridSplitter`, or docking shells that track user-resized panels.

Core controls now ship with built-in implementations where it adds immediate value: `TreeView` remembers expanded branches and selection, `TabControl` keeps the active tab, and the `DataGrid` restores column sizes/reordering after each reload.

Implementations can capture any lightweight structure and replay it after the populate pass:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml.HotReload;

public sealed class HotReloadAwareTreeView : TreeView, IXamlHotReloadStateProvider
{
    private sealed record Snapshot(object? Selected, IReadOnlyList<object> Expanded);

    public object? CaptureHotReloadState()
    {
        return new Snapshot(SelectedItem, GetExpandedNodes());
    }

    public void RestoreHotReloadState(object? state)
    {
        if (state is not Snapshot snapshot)
            return;

        SelectedItem = snapshot.Selected;
        RestoreExpandedNodes(snapshot.Expanded);
    }

    private IReadOnlyList<object> GetExpandedNodes()
    {
        // Walk the item containers to capture which branches the user opened.
        return Array.Empty<object>();
    }

    private void RestoreExpandedNodes(IEnumerable<object> nodes)
    {
        // Use the recorded identifiers to re-expand the matching branches.
    }
}
```

### Re-registering platform handlers

When a control bridges to native adapters (for example through `Control.Handler`) you may need to detach and recreate that integration when a reload swaps types. Assign a custom `ReloadHandler` while delegating to the built-in handler so template refresh still occurs:

```csharp
public sealed class NativeHostView : NativeControlHost, IHotReloadableView
{
    public IHotReloadViewHandler? ReloadHandler { get; set; } = new NativeHandlerReload();

    public void Reload()
    {
        // Re-run bindings or local initialization if required.
    }

    public void TransferState(IControl newView)
    {
        if (newView is NativeHostView host)
            host.ConnectionDescriptor = ConnectionDescriptor;
    }

    private sealed class NativeHandlerReload : IHotReloadViewHandler
    {
        public void OnBeforeReload(IHotReloadableView view)
        {
            if (view is Control control)
                control.Handler?.Detach();
        }

        public void OnAfterReload(IHotReloadableView view)
        {
            if (view is Control control)
            {
                control.Handler?.Detach();
                control.Handler = PlatformHandlerRegistry.Attach(control);

                // Preserve Avalonia's default template/style refresh.
                DefaultHotReloadViewHandler.Instance.OnAfterReload(view);
            }
        }
    }
}
```

Guidance for custom handlers:

- Detach existing handlers in `OnBeforeReload` to avoid leaking native resources.
- Recreate the handler via the usual attachment path in `OnAfterReload`.
- Call `DefaultHotReloadViewHandler.Instance.OnAfterReload(view)` so templates, styles, and layout invalidations still run.
- Because `HotReloadViewRegistry` automatically tracks every `IHotReloadableView`, simply assigning a handler is enough to participate in the lifecycle.

Keep snapshots focused—capture identifiers or lightweight DTOs rather than full control instances—and prefer immutable records so they can be reused if multiple reloads happen before a user interaction.

### Testing and diagnostics entry points

- `RuntimeHotReloadService.ApplyHotReloadAsync(string typeName, string xaml)` allows test harnesses to push in-memory XAML without touching the file system. The method queues the update on the UI dispatcher so tests can await completion deterministically.
- `RuntimeHotReloadService.ApplyHotReloadForTests` remains as a synchronous helper for legacy tests; prefer the async overload for new scenarios so you can await completion inside `Dispatcher.UIThread`.
- Structured logging is available through `HotReloadDiagnostics`. Set `AVALONIA_HOTRELOAD_DIAGNOSTICS=1` (and optionally `AVALONIA_LOGGING_CONSOLE=1`) to see start/success/failure events with durations for each reload scope.

### Status snapshot for tooling

Tooling can call `RuntimeHotReloadService.GetStatusSnapshot()` to inspect the current runtime state. The returned `RuntimeHotReloadStatus` includes:

- `ManifestPaths` / `WatcherPaths` — the manifests registered and the file-system watchers currently active.
- `Registrations` — per-`x:Class` metadata and tracked instance counts.
- `ActiveViews` — the set of live `IHotReloadableView` instances currently tracked.
- `ReplacementStats` — a map recording how many times each view type has been replaced during the current session.

These structured diagnostics make it easier for IDEs or custom tooling to surface hot reload health, active views, and recent replacements without scraping logs.

## Tooling Integration

### dotnet-watch

The default configuration is optimized for the runtime hot reload loop:

- `.axaml` edits are applied by the runtime without forcing `dotnet watch` to rebuild.
- If you need rebuild-triggered behaviour (for example to regenerate resources or run custom tooling) set the `AvaloniaHotReloadEnableDotNetWatchRebuild` property as shown above.
- `dotnet watch list` should complete in seconds; long runs usually indicate that a project opted into watching large asset trees.

### IDE support

IDE tooling that already listens for hot reload deltas (Visual Studio, Rider via dotnet-watch, etc.) does not require additional configuration. Extensions that want to monitor Avalonia-specific resources can rely on the manifest JSON format (`AvaloniaHotReloadManifestEntry`) and the runtime service APIs.

## Troubleshooting

- **No updates when editing XAML:** Ensure the assembly ships with its `.axaml.hotreload.json`. When trimming or single-file publishing, rely on the embedded-manifest registration (`AvaloniaXamlLoader` handles this automatically).
- **dotnet watch rebuild loops / slow startup:** Leave `AvaloniaHotReloadEnableDotNetWatchRebuild` at its default (`false`). If previously enabled, remove the property or set it explicitly to `false`.
- **Need to hot reload non-XAML assets:** Set `AvaloniaHotReloadWatchAssets=true` in the specific project; be aware this re-enables watching all assets.

## Reference

- `build/Avalonia.HotReload.targets` – MSBuild entry point for manifests and watch configuration.
- `src/Avalonia.Build.Tasks/HotReloadManifestWriter.cs` – Manifest generator.
- `src/Markup/Avalonia.Markup.Xaml/HotReload` – Runtime service, manager, metadata definitions, metadata update handler.
- `src/Markup/Avalonia.Markup.Xaml/AvaloniaXamlLoader.cs` – Runtime manifest registration.

## Critical Assessment

### Manifest Registration Fails When Source Paths Are Missing

- Manifest watchers throw when the original source directory no longer exists (for example after publishing, running on another machine, or referencing a NuGet-dropped library). `EnsureDirectoryWatcher` constructs a `FileSystemWatcher` without checking `Directory.Exists`, so an `ArgumentException` escapes and the manifest registration is swallowed by the parent `catch` (src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:232, src/Markup/Avalonia.Markup.Xaml/AvaloniaXamlLoader.cs:133). The result is that the manifest never loads and hot reload silently stops working for every view in that assembly.
- Because the registration failure is caught and discarded, users receive no diagnostics and cannot discover why hot reload never activated. The runtime only surfaces a blank experience, reinforcing the perception that hot reload is unreliable.

### Machine-Absolute Source Paths Break Remote or Container Scenarios

- The manifest writer persists `SourcePath` as a fully qualified machine path (src/Avalonia.Build.Tasks/HotReloadManifestWriter.cs:79). When the app runs from a published output, container image, or under continuous integration the original source tree is absent, so the runtime cannot reopen the XAML file.
- The runtime has no fallback transport for content updates. Once the local file read fails, the feature is effectively disabled for class libraries, remote developers, or cloud-hosted preview targets.

### File-Watcher Loop Performs Excessive Work on the UI Thread

- Every change triggers `ApplyHotReloadFromFile`, which re-opens the XAML, rebuilds a runtime dynamic assembly, and replays `Populate` against every tracked instance (src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:342). On complex views with dozens of live instances (e.g., templated controls or designer previews) this causes frame hitches and, in practice, encourages teams to disable hot reload.
- Back-to-back save bursts are throttled by a hard-coded 150 ms gate (src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:328). This is insufficient for large files: saves triggered by IDE formatters or git operations still enqueue duplicate rebuilds that stall the UI thread.

### Tooling Integration Is Limited to Opportunistic File Watching

- The MSBuild targets intentionally disable `dotnet watch` rebuilds and only watch `.axaml` files unless you opt-in. When projects span multiple repositories or when assets are generated, the runtime never receives updated manifests, so hot reload remains stale.
- IDE integrations receive no structured protocol. They must poll the filesystem and guess the manifests, duplicating logic and making consistent UX (for example between Visual Studio, Rider, Live Previewer) hard to achieve.

### Diagnostics and Observability Are Lacking

- All critical failures—including manifest registration, watcher creation, and runtime reload exceptions—are swallowed (src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:217). Developers cannot tell whether the system is idle or broken.
- There is no way to enumerate the registered manifests or tracked instances at runtime, so tooling cannot present status or guidance to the user.

## Architecture & Fix Proposals

### Immediate Hardening

**Findings**
- Critical — `CreateDelegates` never finds builder types from the shipped manifests, because it only searches `AppDomain` for dynamic assemblies. Build-time manifests (for example `ControlCatalog.axaml.hotreload.json:2`) point to real assemblies like `ControlCatalog.dll`, so `ResolveDynamicAssembly` in `src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadDelegateProvider.cs:113` short-circuits with “Unable to locate dynamic assembly …”, and `RuntimeHotReloadService.RegisterRange` fails to register any entry. Hot reload can’t start for precompiled XAML.
- Critical — even after fixing the lookup, `BuildDelegate` in `RuntimeHotReloadDelegateProvider.cs:62-80` throws whenever a manifest entry has `BuildMethodName = null`. That’s the default for every compiled `x:Class` (see how `HotReloadManifestWriter` serialises it in `src/Avalonia.Build.Tasks/HotReload/HotReloadManifestWriter.cs:36-45` and the generated data in `samples/ControlCatalog/bin/Debug/net8.0/ControlCatalog.axaml.hotreload.json:2`). The exception bubbles out of `RuntimeHotReloadService.RegisterRange`, so nothing gets registered/tracked. We at least need a populate-only path (e.g. fall back to `Activator.CreateInstance` or skip `Build` when it’s missing).
- Major — embedded manifests are re-registered on every call to `AvaloniaXamlLoader.Load`. The loader adds a new provider each time (`src/Markup/Avalonia.Markup.Xaml/AvaloniaXamlLoader.cs:166-167`), and `RuntimeHotReloadService.RegisterManifestProvider` just appends it to `s_manifestProviders` (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs:146-152`). After a few loads you end up with dozens of identical providers; every `ReloadRegisteredManifests` call replays the same manifest over and over, repeatedly repopulating tracked controls and driving up CPU/allocations. The provider list needs deduping (e.g. keyed by assembly/resource) or a guard to register once per assembly.

**Open questions / next steps:**
1. After unblocking registration, can we add a regression test that loads a real manifest (with null `BuildMethodName`) to catch both failures?
2. Once providers are deduped, it’d be good to measure that we only repopulate once per reload.

### Hot Reload Transport & Tooling Contract

- Introduce an `IHotReloadTransport` abstraction inside the runtime that can accept content updates via local named pipes, Unix sockets, or HTTP/WebSocket endpoints so tooling has a reliable push channel.
- Ship a lightweight CLI (`avalonia-hot`) that wraps `dotnet watch` and streams file diffs into the transport, removing the dependence on fragile filesystem mirroring.
- Update MSBuild targets to emit the manifest plus a deterministic `ContentId` per XAML file so tooling can push `{ContentId, xaml}` payloads without re-parsing the manifest every time.

### Runtime Pipeline Improvements

- Cache the parsed XAML-to-IL graph between reloads. Instead of recompiling the entire document for every instance, compile once per change, cache the dynamic builder, and only invoke `Populate` on tracked instances.
- Move the UI-thread work onto a background dispatcher queue. Parse and compile on a thread pool, then marshal the minimal diff (new delegates) back to the UI thread, drastically reducing UI hitches.
- Replace the fixed 150 ms debounce with an adaptive scheduler that coalesces rapid edits per file and supports cancellation when newer content arrives.
- Expose optional view-model safe hooks so that reloads can preserve state or run user-provided migrations between XAML schema changes.

### Tooling & UX Enhancements

- Surface hot reload status in the developer tools overlay: show per-file success/failure, last update time, and the active transport.
- Provide opt-in validation that warns when the source path in the manifest does not exist at startup, guiding developers to enable the new transport or copy sources.
- Document a contract for custom IDEs: how to discover `ContentId`, negotiate transport, push deltas, and query runtime status.

## Implementation Roadmap

- Measure and fix the watcher crash bug, add diagnostics, and release as a patch to unblock existing users.
- Implement the transport abstraction and CLI tooling alongside updated MSBuild targets so `dotnet watch` and IDE integrations can migrate without breaking older workflows.
- Optimize the runtime pipeline (compile caching, async reload, adaptive throttling) and add public status APIs so tooling can provide feedback.
