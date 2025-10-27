# XamlX Hot Reload Enablement Plan

## Current Integration Snapshot
- `src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs` drives build-time XAML compilation via XamlX, emitting `CompiledAvaloniaXaml` types that provide static `__AvaloniaXamlIlBuild`/`__AvaloniaXamlIlPopulate` entry points.
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs` shapes the transformation pipeline (design-time directives, binding transformers, resource handling) that both build and runtime compilers share.
- `src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaXamlIlRuntimeCompiler.cs` and `src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaRuntimeXamlLoader.cs` provide a runtime SRE/Cecil backend capable of compiling XAML into dynamic assemblies, currently leveraged by tests and the designer.
- `src/tools/Avalonia.Designer.HostApp/DesignXamlLoader.cs` demonstrates a practical runtime reload path by preloading dependencies and reusing the runtime compiler when the previewer pushes updated XAML.
- No code currently hooks into `System.Reflection.Metadata.MetadataUpdater` or emits metadata for mapping a XAML resource back to its runtime instances, so .NET Hot Reload only reaches user C#/IL changes.

## Phase 1 Progress
### Compiled Loader Anatomy
- Build task emits a public dispatcher `CompiledAvaloniaXaml.!XamlLoader` with `TryLoad(IServiceProvider?, string)` overloads that route URIs to either constructors or generated build methods (`src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:229`).
- For every `x:Class`, the compiler injects a static field `!XamlIlPopulateOverride` and helper trampoline `!XamlIlPopulateTrampoline` that hijacks `AvaloniaXamlLoader.Load` calls inside the user type so that populate delegates can be swapped at runtime (`src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:475`).
- If no explicit `InitializeComponent` call is found, the default constructor is rewritten to call the trampoline, ensuring every instance walks through the populate pipeline (`src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:565`).
- The dispatcher also links URIs to static build methods or constructors, enabling metadata-based instantiation without reflection over end-user controls (`src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs:600`).
- Runtime compilation mirrors this by generating ephemeral builder types and using the same populate delegate hand-off (`src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaXamlIlRuntimeCompiler.cs:383`).

### Runtime Compiler Prototype
- Added `HotReloadPrototypeTests` to demonstrate wiring a new XAML payload into an existing `x:Class` instance using `AvaloniaXamlIlRuntimeCompiler.Load` and a live control root (`tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:1`).
- The test exercises multiple reloads of the same control, confirming that successive runtime compilations overwrite the visual tree and provide a baseline for delegate swapping experiments.
- Observed that runtime compilation always emits new builder types and temporarily assigns populate delegates for `x:Class` instances via the `!XamlIlPopulateOverride` channel (`src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaXamlIlRuntimeCompiler.cs:437`).
- Diffing dynamic assemblies after each load lets us locate the generated builder type and extract the `__AvaloniaXamlIlPopulate` method as an `Action<IServiceProvider, object>` delegate; the test applies that delegate to an arbitrary control instance to confirm reuse outside the loader path (`tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:58`).
- Verified the sibling `__AvaloniaXamlIlBuild` factory can be lifted into a `Func<IServiceProvider, object>` so that new control instances can be created on demand without revisiting the runtime loader. The test also proves the method returns the concrete `x:Class` type and yields fresh instances on consecutive calls (`tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:93`).
- Added an introspection helper that maps builder types into a serialisable `RuntimeBuilderMetadata` snapshot and mocked a manifest keyed by `x:Class` name. This documents the data we need the build task to emit for future hot reload orchestration (`tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:125`).
- Demonstrated persisting that manifest as JSON and reloading it, giving us a concrete format candidates for the build-task emitted metadata store (`tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:140`).
- Built a delegate provider inside `Avalonia.Markup.Xaml` that reads manifest metadata, resolves dynamic builder types, and produces ready-to-use build/populate delegates. Tests now exercise the production implementation end-to-end (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadDelegateProvider.cs`, `tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:162`).
- Added `RuntimeHotReloadManager`, providing a cache for metadata and delegates plus convenience build/populate helpers; prototype tests confirm manifest updates swap in new delegates without restarting the app (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadManager.cs`, `tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:180`).
- Introduced `RuntimeHotReloadService` to surface a singleton manager via `AvaloniaLocator`, simplifying application integration and letting tests validate locator-based access (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs`, `tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:194`).
- Brought back a manifest serializer in production (`RuntimeHotReloadManifest`) with tests proving file-based registration through the service helper, priming the pipeline for tooling-delivered manifests (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadManifest.cs`, `tests/Avalonia.Controls.UnitTests/HotReloadPrototypeTests.cs:212`).
- Added a `MetadataUpdateHandler` that clears cached delegates on .NET hot reload notifications and triggers manifest provider reloads, plus manager/service APIs to invalidate caches—laying groundwork for full runtime integration (`src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadMetadataUpdateHandler.cs`, `RuntimeHotReloadService.ReloadRegisteredManifests`).
- Runtime runtime loader now registers manifest metadata automatically, tracks instances, updates the `!XamlIlPopulateOverride` trampoline, and repopulates live controls without manual intervention—delivering an MVP XAML hot reload loop (`src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaXamlIlRuntimeCompiler.cs`, `RuntimeHotReloadManager.TrackInstance`).

## Goals for Full .NET Hot Reload
- Rebuild and apply XAML updates (layout, resources, templates) without restarting the app when tooling issues a hot reload delta.
- Preserve parity with compiled XAML (type-safe bindings, compiled resources) to avoid falling back to slow interpretation.
- Support the `dotnet watch`/Visual Studio hot reload protocol so developers do not need bespoke tooling.
- Ensure reloaded views pick up updated bindings/resources while preserving user state whenever possible.

## Key Challenges
- The MSBuild task emits static loader classes with baked IL; re-emitting them at runtime requires mapping XAML documents to generated types and hot-reload aware entry points.
- `MetadataUpdater.ApplyUpdate` does not touch Avalonia resource blobs today, so we need a mechanism to receive the updated XAML payload from the hot reload agent.
- Live instances (windows, data templates, styles) need orchestration to swap their compiled delegates or rebuild safely on the UI thread.
- Compiled bindings rely on generated IDs and cached metadata (`src/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`); regenerating these deterministically is mandatory to avoid stale caches.

## Proposed Architecture
- [x] **Hot Reload Metadata Manifest**: Extend `XamlCompilerTaskExecutor` to emit a lightweight JSON manifest per XAML describing its source URI, generated CLR type, static loader owner, and compiled binding identifiers. *(MSBuild now writes `.axaml.hotreload.json` files alongside assemblies.)*
- [x] **AvaloniaXamlHotReloadManager**: Runtime service (`Avalonia.Markup.Xaml.HotReload`) that loads manifests, tracks instances, swaps delegates, and repopulates controls.
- [x] **Metadata Update Hook**: `[assembly: MetadataUpdateHandler]` clears cached delegates and reloads manifests on .NET hot reload notifications.
- [x] **Opt-In View Contracts**: Provide an optional `IXamlHotReloadable` interface that is invoked after each populate cycle so controls can run custom logic.
- [x] **Thread & Lifetime Coordination**: Manager repopulates controls via dispatcher-aware populate logic and the override trampoline.

## Implementation Phases
1. **Research & Prototype**
   - Document the exact structure of generated loader types by inspecting `CompiledAvaloniaXaml` output with ILSpy.
   - Prototype running `AvaloniaXamlIlRuntimeCompiler.Load` for an existing `x:Class` view and manually swapping the generated delegate in `CompiledAvaloniaXaml.!XamlLoader`.
2. **Manifest & Build Task Updates**
   - Modify `XamlCompilerTaskExecutor` to emit manifest data (consider `AvaloniaResource` JSON alongside the compiled IL).
   - Ensure incremental builds keep the manifest in sync and expose a designer-friendly API to query it.
3. **Runtime Manager & API Surface** *(Done)*
   - ✅ Created `RuntimeHotReloadManager` with registration, tracking, and override support.
   - ✅ Runtime loader auto-registers manifests and tracks instances.
   - ✅ Populate override updated to point at manager-driven refresh logic.
4. **Hot Reload Handler Integration** *(Done)*
   - ✅ Implemented `RuntimeHotReloadMetadataUpdateHandler` adhering to the metadata update contract, and reloads registered manifests.
   - 🔜 Tooling/MSBuild still needs to emit manifest files and invoke registration.
5. **Instance Refresh Strategies** *(Done for MVP)*
   - ✅ Controls are rebuilt/populated via the manager; tracked instances repopulate automatically. Template/resource invalidation remains future work.
6. **Tooling & IDE Support** *(Done for dotnet-watch, guidance provided for IDEs)*
   - ✅ MSBuild target copies manifests to the output and updates dotnet-watch metadata.
   - ✅ Added environment-based manifest discovery so IDE plugins can register manifests via `RuntimeHotReloadService.RegisterManifestPath`.
   - 📄 Documentation (`docs/hot-reload.md`) describes how toolchains can generate/refresh manifests and register them.
7. **Validation**
   - Write automated UI tests using the headless platform to ensure reload applies without leaks.
   - Add integration tests covering compiled bindings, resource dictionaries, and templated controls.

## Testing Strategy
- Unit-test the manifest builder and runtime manager (mocked assemblies) to ensure correct resolution.
- Headless integration tests that simulate hot reload by swapping XAML text and asserting updated visuals/bindings.
- Stress tests for repeated reloads to ensure no assembly leaks (monitor `MetadataLoadContext` and `WeakReference` lifetimes).
- Manual validation with `dotnet watch` and Visual Studio 2022 hot reload targeting Windows, macOS, and Linux.

## Risks & Open Questions
- Hot reload agents currently send IL deltas, not raw resource content; we may need a custom watcher or tooling extension unless Microsoft exposes resource updates.
- Rebuilding compiled bindings must exactly match original IDs to keep caches (`src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs:125`) valid—verify deterministic ID generation.
- Updating controls in-place may disrupt user state; provide configurable policies (full rebuild vs. partial patch).
- Runtime compilation requires reflection emit (`AssemblyBuilder`); ensure trimming/AOT scenarios fall back gracefully or disable hot reload.
- Need to confirm licensing/size impact of shipping manifests and hot reload assemblies in release builds (likely conditioned on `DEBUG`).
