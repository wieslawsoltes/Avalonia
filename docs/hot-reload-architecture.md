# Avalonia XAML Hot Reload – Architecture RFC

**Status:** Active  
**Editors:** Avalonia Hot Reload Working Group  
**Scope:** Runtime XAML hot reload inside the `Avalonia.Markup.Xaml` package  
**Last Updated:** 2023-XX-XX <!-- replace with actual date if needed -->

---

## 1. Motivation

Avalonia’s first iterations of runtime XAML hot reload relied on synchronous dispatcher suspension and ad-hoc state capture. Long-lived edit sessions frequently stalled the UI thread, resource dictionaries did not refresh reliably, and tooling lacked any visibility into the runtime state. This RFC documents the redesigned hot reload architecture that:

- Queues work without blocking the dispatcher pump.
- Provides explicit lifecycle hooks for view/template refresh.
- Emits structured diagnostics and exposes a status snapshot API for tooling.
- Supports deterministic unit testing via in-memory reload entry points.

This document doubles as the authoritative reference for contributors and downstream integrators.

---

## 2. Goals

1. **Deterministic UI updates** – apply reload work on the dispatcher thread without `Dispatcher.DisableProcessing`.
2. **State preservation** – keep track of control instances, resource dictionaries, and opt-in state providers.
3. **Lifecycle extensibility** – offer handler hooks (`IHotReloadableView`/`IHotReloadViewHandler`) mirroring MAUI’s pattern.
4. **Observability** – surface structured diagnostics (start/end/failure, duration) and a status snapshot consumable by tooling.
5. **Testability** – provide async APIs that tests can await instead of relying on file-system side effects.

Non-goals include designer-time metadata transport and cross-process reload protocols (see §8 for future work).

---

## 3. Architecture Overview

### 3.1 High-Level Flow

```
┌────────────────────┐      file change      ┌────────────────────────┐
│ dotnet watch / IDE │ ────────────────────▶ │ RuntimeHotReloadService │
│ (hot reload agent) │                       │  (file system watcher) ├────┐
└─────────┬──────────┘                       └────────────┬───────────┘    │
          │                                            queue               │
          │ structured status / logs                   ▼                   │
          │                                      ┌───────────────┐         │
          └───────────────────────────────────── │ Dispatcher UI │ ◀───────┘
                                                 │  (InvokeAsync)│
                                                 └──────┬────────┘
                                                        │
                            ┌───────────────────────────┼────────────────────────┐
                            ▼                           ▼                        ▼
                 RuntimeHotReloadManager     HotReloadViewRegistry   ResourceDictionaryRegistry
                        (delegates)              (views/handlers)         (resource refresh)
```

### 3.2 Internal Execution Stages

1. **Watchers & throttling**
   - `RuntimeHotReloadService.HandleSourceChange` enforces a 150 ms debounce per file and queues work via `EnqueueHotReloadWork`.
   - Queued tasks run on the thread pool; actual UI operations hop onto the dispatcher via `Dispatcher.UIThread.InvokeAsync`.

2. **UI-thread execution**
   - `ApplyHotReloadOnUiThread` (file-based) / `ApplyHotReloadFromXamlOnUiThread` (in-memory) handle synchronous steps.
   - Structured diagnostics surround every stage (`HotReloadDiagnostics.ReportReloadStart|Success|Failure`).

3. **Delegate/instance refresh**
   - `RuntimeHotReloadManager` resolves metadata, rebuilds delegates, and re-populates tracked instances.
   - `CaptureInstanceSnapshot` / `RestoreInstanceSnapshot` preserve property bags, read-only lists/dictionaries, and `IXamlHotReloadStateProvider` payloads.

4. **Lifecycle hooks**
   - `HotReloadViewRegistry` tracks `IHotReloadableView` instances, invoking `ReloadHandler.OnBeforeReload` and `OnAfterReload`. The built-in `DefaultHotReloadViewHandler` reapplies templates, invalidates styles, and refreshes layout.
   - `HotReloadResourceDictionaryRegistry` emits `ResourcesChangedToken`s so bindings and styles pick up new resources.
   - `HotReloadStaticHookInvoker` locates `[OnHotReload]`-annotated static methods and executes them.

5. **Tooling visibility**
   - `RuntimeHotReloadService.GetStatusSnapshot()` returns `RuntimeHotReloadStatus`, exposing manifest/watch paths, registrations, active views, and replacement statistics.

---

## 4. Component Inventory

| Component | Responsibility | Key References |
|-----------|----------------|----------------|
| `RuntimeHotReloadService` | Entry point, file watchers, work queue, diagnostics, public APIs (`Track`, `RegisterHotReloadView`, `ApplyHotReloadAsync`, `GetStatusSnapshot`) | `src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadService.cs` |
| `RuntimeHotReloadManager` | Metadata cache, delegate creation, tracked-instance refresh, populate override | `src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadManager.cs` |
| `RuntimeHotReloadDelegateProvider` | Reflection-based creation of `Build`/`Populate` delegates | `src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadDelegateProvider.cs` |
| `IHotReloadableView` / `IHotReloadViewHandler` | Hot reload lifecycle hooks | `src/Markup/Avalonia.Markup.Xaml/HotReload/IHotReloadableView.cs`, `IHotReloadViewHandler.cs` |
| `HotReloadViewRegistry` | Tracks active views, coordinates handlers, records replacement stats | `src/Markup/Avalonia.Markup.Xaml/HotReload/HotReloadViewRegistry.cs` |
| `DefaultHotReloadViewHandler` | Reapply templates, invalidate layout/visuals post reload | `src/Markup/Avalonia.Markup.Xaml/HotReload/DefaultHotReloadViewHandler.cs` |
| `HotReloadResourceDictionaryRegistry` | Notifies owning trees when resources change | `src/Markup/Avalonia.Markup.Xaml/HotReload/HotReloadResourceDictionaryRegistry.cs` |
| `HotReloadStaticHookInvoker` | Discovers `[OnHotReload]` hooks via reflection | `src/Markup/Avalonia.Markup.Xaml/HotReload/HotReloadStaticHookInvoker.cs` |
| `HotReloadDiagnostics` | Opt-in structured logging with reload start/success/failure events | `src/Markup/Avalonia.Markup.Xaml/HotReload/HotReloadDiagnostics.cs` |
| `RuntimeHotReloadStatus` | Tooling-facing snapshot DTO (manifests, watchers, registrations, active views) | `src/Markup/Avalonia.Markup.Xaml/HotReload/RuntimeHotReloadStatus.cs` |

---

## 5. Public APIs & Usage Patterns

### 5.1 Tracking controls

```csharp
public sealed partial class TextBoxPage : UserControl, IHotReloadableView
{
    public TextBoxPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RuntimeHotReloadService.RegisterHotReloadView(this);
        Unloaded += (_, _) => RuntimeHotReloadService.UnregisterHotReloadView(this);
    }

    public IHotReloadViewHandler? ReloadHandler { get; set; } = DefaultHotReloadViewHandler.Instance;

    public void TransferState(Control newView) =>
        ((TextBoxPage)newView).MyTextBox.Text = MyTextBox.Text;

    public void Reload() { /* optional re-binding */ }
}
```

### 5.2 Unit-testing entry point

```csharp
await RuntimeHotReloadService.ApplyHotReloadAsync(
    typeName: "ControlCatalog.Pages.TextBoxPage",
    xaml: File.ReadAllText("TextBoxPage.xaml"));
```

### 5.3 Tooling snapshot

```csharp
var status = RuntimeHotReloadService.GetStatusSnapshot();
Console.WriteLine($"Active views: {string.Join(", ", status.ActiveViews)}");
Console.WriteLine($"Replacements: {string.Join(", ", status.ReplacementStats)}");
```

Structured reload diagnostics can be enabled via:

```bash
AVALONIA_HOTRELOAD_DIAGNOSTICS=1 \
AVALONIA_LOGGING_CONSOLE=1 \
dotnet watch --project samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj run -- --hotreload
```

---

## 6. ControlCatalog Integration Checklist

The sample application (`samples/ControlCatalog.NetCore`) demonstrates the full pipeline:

1. Launch with `dotnet watch --project samples/ControlCatalog.NetCore/ControlCatalog.NetCore.csproj run -- --hotreload`.
2. `App.Initialize` calls `RuntimeHotReloadService.GetOrCreate()` to ensure manifests register before any XAML is loaded.
3. Individual pages register via `RuntimeHotReloadService.RegisterHotReloadView(this)` inside their `Loaded` handlers.
4. TreeView, TabControl, and DataGrid implement `IXamlHotReloadStateProvider` to persist selection/expansion state.
5. Diagnostics and status snapshots can be queried while editing to confirm active views and replacements.

Refer to `docs/control-catalog-hot-reload.md` for the full walkthrough.

---

## 7. Granular Watchers & Partial Diff Reload

### 7.1 Objectives

- Detect XAML edits at the granularity of templates, resources, and inline controls.
- Compile and ship only the delta for touched parts, leaving unaffected controls alive.
- Maintain view and resource state while still guaranteeing consistency when diffs fail.

### 7.2 Incremental Watch Pipeline

- **Syntax-aware watchers:** Extend the XAML project graph so each file is represented as a tree of addressable parts (root control, `Style`, `DataTemplate`, `ResourceDictionary` entries). File system events are mapped to tree nodes using a cached parse with stable node IDs (path + spine index).
- **Incremental parsing cache:** Cache the parsed syntax tree, semantic bindings, and resource metadata for every node. When a change lands, reparse only the affected span and invalidate dependent nodes rather than the entire file.
- **Unified subscriptions:** IDE hot reload, `dotnet watch`, and the ControlCatalog harness subscribe through a shared watcher service. Events are emitted with the list of changed node IDs and a summary of affected bindings, enabling tooling to preview the impact before reloading.

### 7.3 Partial Diff Emission

- **Addressable IR:** Introduce a part-addressable intermediate representation in the XamlX compiler so every node carries its deterministic ID and source span metadata.
- **Tree differ:** Compare the cached IR for a node with the newly parsed version (e.g., GumTree-style diff) and emit high-level operations such as `ReplaceNode`, `UpdateProperty`, `AddChild`, and `RemoveChild`. The differ collapses cosmetic changes (whitespace, attribute reordering) so runtime patches stay minimal.
- **Payload serializer:** Package diff operations as a transport payload (JSON or binary) that also includes dependency hints (resource keys, referenced types). Version the payload so older runtimes fall back to full reloads.

### 7.4 Runtime State Preservation

- **Selective patching:** The runtime patcher resolves live controls via `NameScope`, templated parents, and resource keys. Only nodes marked in the diff are recreated; untouched controls keep their `DataContext`, bindings, and registered handlers.
- **State providers:** Existing `IXamlHotReloadStateProvider` implementations continue to run, but only for the instances that are actually replaced. Controls that survive the diff skip snapshot/restore, avoiding unnecessary churn.
- **Failure fallback:** If applying a diff throws or produces an inconsistent tree, the runtime rolls back to a full reload of the file, preserving the current stability guarantees.

### 7.5 Compiler & Runtime Workstreams

1. **Infrastructure:** Establish syntax tree caching, stable node IDs, and hash-based change detection that plugs into the XamlX front-end.
2. **Diff Engine:** Build the structural differ and IR serializer, including unit tests covering complex templates and merged dictionaries.
3. **Emitter & Transport:** Extend the existing hot reload transport to ship diff payloads, add schema versioning, and surface diagnostics when the payload is discarded.
4. **Runtime Application:** Implement the diff applier service, ensure dispatcher-thread guarantees, and validate template/resource refresh scenarios.

### 7.6 Milestone Plan

- **M1 Discovery:** Audit the current XamlX pipeline, document change points, and spike syntax tree caching with node IDs.
- **M2 Incremental Parsing:** Implement part-level watchers, validate them inside ControlCatalog, and measure latency under rapid edits.
- **M3 Diff Prototype:** Produce partial diff payloads for a single file, create a minimal runtime applier, and integrate with tooling.
- **M4 Robustness:** Expand coverage to merged dictionaries, global resources, and templated controls; add telemetry and error recovery.
- **M5 Tooling & Docs:** Expose CLI/IDE toggles, update developer documentation, and provide a stability playbook for the new pipeline.

## 8. Testing & Observability

- **Async reload API:** `ApplyHotReloadAsync` ensures tests can `await` dispatcher work without blocking.
- **Stress tests:** `tests/Avalonia.Markup.Xaml.UnitTests/HotReload/RuntimeHotReloadServiceStressTests.cs` validates rapid reload cycles.
- **Diagnostics:** `HotReloadDiagnostics` emits structured records (`ReloadStart`, `ReloadSuccess`, `ReloadFailure`) with scope and duration.
- **Status snapshot:** `RuntimeHotReloadStatus` mirrors MAUI’s `ActiveViews`/`ReplacedViews` counters for IDE integration.

---

## 9. Future Work

- **Transport abstraction:** decouple from file-system watchers to support remote tooling or IDE push protocols.
- **Template diffing:** explore minimal-template updates instead of full reapply for large control trees.
- **Designer integration:** expose hooks for design-time metadata and previewer synchronization.
- **Extended metrics:** include per-file reload counts, last failure metadata, and custom telemetry sinks.

---

## 10. Appendix – Code Reference Map

```
src/
 └── Markup/Avalonia.Markup.Xaml/
     └── HotReload/
         ├── RuntimeHotReloadService.cs         (dispatcher orchestration, public APIs)
         ├── RuntimeHotReloadManager.cs         (metadata + delegate lifecycle)
         ├── RuntimeHotReloadDelegateProvider.cs
         ├── HotReloadDiagnostics.cs            (structured logging)
         ├── HotReloadViewRegistry.cs           (view tracking, handlers, replacement stats)
         ├── DefaultHotReloadViewHandler.cs     (template/layout refresh)
         ├── HotReloadResourceDictionaryRegistry.cs
         ├── HotReloadStaticHookInvoker.cs      (OnHotReload attribute runner)
         ├── IHotReloadableView.cs / IHotReloadViewHandler.cs
         ├── OnHotReloadAttribute.cs
         ├── IXamlHotReloadStateProvider.cs
         └── ControlStateProviders/             (TreeView, TabControl state preservation)
```

---

*End of document.*
