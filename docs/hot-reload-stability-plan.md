# Hot Reload Stability & MAUI Parity Plan

## Context

- Avalonia's runtime hot reload currently stalls under repeated edit cycles; stress tests hang because reload delegates run synchronously under `Dispatcher.DisableProcessing()`.
- The MAUI codebase (see `external/maui/src/Core/src/HotReload`) provides a more battle-tested approach built around:
  - `IHotReloadableView` abstractions with state transfer hooks.
  - `MauiHotReloadHelper` that tracks active views, replaces instances, and re-registers handlers.
  - An `OnHotReloadAttribute` pipeline that runs static hooks after reload.
  - Resource dictionary helpers that centralise re-loading of XAML resources.

## Goals

1. Eliminate dispatcher deadlocks during long edit sessions.
2. Provide explicit hot-reload entry points for tests/tools (no file-system dependency).
3. Introduce structured state transfer and view replacement akin to MAUI.
4. Improve documentation and observability for reload cycles.

## Milestones & Tasks

1. **Stabilise Reload Execution Path**
   - [x] Replace synchronous `Dispatcher.DisableProcessing` invocations with queued UI-thread work that respects the dispatcher pump.
   - [x] Expose `RuntimeHotReloadService.ApplyHotReloadForTests` (or equivalent) for deterministic test harnesses, bypassing `FileSystemWatcher`.
   - [x] Add throttling/backoff similar to MAUI's metadata update trigger to avoid rapid-fire reloads.
   - [ ] Land a passing stress test that performs `N` in-memory reload cycles without hanging.

2. **Adopt MAUI-Style View Contracts**
   - [x] Introduce an Avalonia `IHotReloadableView` interface (TransferState, Reload, ReloadHandler) inspired by MAUI.
   - [x] Implement a helper (analogous to `MauiHotReloadHelper`) that tracks live views via weak references and orchestrates state transfer.
   - [x] Ensure tracker integrates with existing `TrackInstance` logic and supports manual registration for custom controls.
   - [x] Provide an `OnHotReload` attribute to run static initialisers/post-reload hooks.

3. **Resource & Handler Synchronisation**
   - [x] Port MAUI's resource dictionary reload pattern into Avalonia so shared resources rehydrate cleanly.
   - [x] Extend helper to refresh control templates/handlers (Avalonia equivalents) when reload swaps types.
   - [x] Document a pattern for controls to re-register platform handlers after reload.

4. **Diagnostics & Tooling Hooks**
   - [x] Emit structured diagnostics (start/end, duration, failures) for each reload cycle.
   - [x] Surface status APIs mirroring MAUI's `ActiveViews`/`ReplacedViews` for tooling.
   - [x] Update CLI/docs to describe new interfaces, testing entry points, and stability improvements.
   - [x] Provide step-by-step guidance for enabling the new helper in sample apps (ControlCatalog etc.).

## Next Steps

- Analyse `MauiHotReloadHelper` internals to design Avalonia equivalents (state cache, handler re-registration).
- Prototype the queued dispatcher reload path and validate with the new stress test harness.
- Iterate on milestones, ticking tasks as patches land.
