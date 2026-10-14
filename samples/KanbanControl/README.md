# Avalonia Kanban Control

A reusable, fully templated Kanban board with a UI-independent move engine and a shared desktop/WebAssembly showcase. The browser host renders the **same Avalonia controls and XAML** as the desktop host; it is not an HTML replacement.

**Version:** 0.1.0-preview.1 · **License:** MIT · **Avalonia:** 12.1.3 · **SDK:** .NET 10

The libraries target .NET 8. The samples and executable test projects target .NET 10. The browser sample uses `net10.0-browser` and the `wasm-tools` workload.

## Projects

| Project | Responsibility |
| --- | --- |
| `Kanban.Core` | Model adapters, indexed cells, workflow validation, WIP policies, revision checks, reversible move transactions and bounded history. No Avalonia dependency. |
| `Kanban.Avalonia` | `KanbanBoard`, `KanbanColumn`, `KanbanSwimlane`, `KanbanCard`, virtualized card lists, pointer/keyboard input and replaceable control themes. |
| `Kanban.Sample` | Shared Atlas workspace: rich cards, filters, sorting, card editor, context menus, light/dark and compact views, JSON import/export and 10,000-card data set. |
| `Kanban.Sample.Desktop` | Native Avalonia desktop entry point. |
| `Kanban.Sample.Browser` | Actual Avalonia WebAssembly entry point and sample-only diagnostics bridge. |
| `Kanban.Core.Tests` | Dependency-light executable behavioral specifications. |
| `Kanban.Headless.Tests` | Actual Avalonia template, projection, selection, lifecycle and virtualization checks. |
| `tests/browser` | Chromium input scenarios against the published WebAssembly application. |

## Run

Run these commands **from this directory**, not the parent Avalonia repository.

```sh
dotnet run --project samples/Kanban.Sample.Desktop

dotnet workload install wasm-tools
dotnet run --project samples/Kanban.Sample.Browser
```

To publish and serve a static browser build:

```sh
dotnet publish samples/Kanban.Sample.Browser -c Release -o artifacts/browser
# Locate the directory containing the published index.html (normally wwwroot).
python scripts/serve.py artifacts/browser/wwwroot --port 8080
```

Open `http://127.0.0.1:8080`. A WebAssembly app must be served over HTTP(S), not opened using `file://`. The supplied server sets the WebAssembly MIME type and cross-origin isolation headers. No backend, login, API key or external service is required by the demo.

## Use the control

Reference `src/Kanban.Avalonia/Kanban.Avalonia.csproj`, or install the supplied local NuGet package and its `Kanban.Core` dependency. Include the theme after the application's base theme:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:k="using:Kanban.Avalonia">
  <Application.Styles>
    <FluentTheme />
    <k:KanbanTheme />
  </Application.Styles>
</Application>
```

Declare columns and bind the original application collection:

```xml
<k:KanbanBoard xmlns:k="using:Kanban.Avalonia"
               xmlns:core="using:Kanban.Core"
               ItemsSource="{Binding Cards}"
               SelectedItem="{Binding SelectedCard, Mode=TwoWay}"
               LaneHeight="480">
  <k:KanbanBoard.Columns>
    <core:KanbanColumnDefinition Key="todo" Header="To do" />
    <core:KanbanColumnDefinition Key="doing" Header="In progress"
                                 MaximumWorkInProgress="4" />
    <core:KanbanColumnDefinition Key="done" Header="Done" />
  </k:KanbanBoard.Columns>
  <k:KanbanBoard.CardTemplate>
    <DataTemplate>
      <TextBlock Text="{Binding Title}" TextWrapping="Wrap" Margin="12" />
    </DataTemplate>
  </k:KanbanBoard.CardTemplate>
</k:KanbanBoard>
```

The default adapter accepts `KanbanItem`:

```csharp
public ObservableCollection<KanbanItem> Cards { get; } = new()
{
    new() { Id = "TASK-1", Title = "Review the design", ColumnKey = "todo", Order = 0 },
    new() { Id = "TASK-2", Title = "Ship the feature", ColumnKey = "doing", Order = 0 }
};
```

Implement `IKanbanAdapter` to use your own model without deriving from `KanbanItem`. The source objects are used directly as card template data contexts; no reflection-based property naming convention is required by the move engine.

## Implemented behavior

Cards support mouse/pen drag-and-drop, multi-card moves, same-cell reordering, swimlane transfers, insertion indicators, an overlay drag label, continuous edge auto-scroll and cancellation. A deliberate long press is required before a touch drag; physical touch-device behavior still needs device-matrix testing.

The same coordinator handles pointer, keyboard and programmatic moves. It validates stable IDs, target cells, locked cards, source/destination read-only state, lane-change rules, WIP limits, insertion anchors, preview cancellation, custom workflow policy and expected revisions. WIP can be enforced, warned about or ignored. Counts use the full source across all swimlanes and are not reduced by filtering.

Selection supports modifier-key multi-selection, ListBox range selection, selected-item binding, an observable selected-items collection and selection notifications. Text search, a custom predicate and custom sorting project the source without reordering its collection. Manual ordering is disabled while a custom sort is active.

A single-lane board fills its available viewport by default (`FitSingleLaneToViewport`); explicit swimlanes use `LaneHeight`. Columns resize with a thumb and collapse consistently across swimlanes. Swimlanes also collapse. Reordering the `Columns` or `Swimlanes` observable collections changes their visual order without changing card keys. Column-header drag reordering and cross-board drag-and-drop are not included in this preview.

The default theme provides light/dark resources, pointer-over, selected, focus-visible, dragging, insertion and disabled visual states. Card, column, lane, empty, header and footer presentation are replaceable. Card containers retain Avalonia's list-item automation peer; the board and groups have named automation peers. A complete assistive-technology audit is not claimed.

## Keyboard

| Input | Action |
| --- | --- |
| Click / Ctrl or Command + click | Select / toggle a card. |
| Shift + click | Extend the range within a cell. |
| Ctrl or Command + A | Select matching cards in expanded columns and lanes. |
| Alt + Left / Right | Move selected cards to the adjacent column. |
| Alt + Up / Down | Reorder the selected group within one cell. |
| Alt + Shift + Up / Down | Move selected cards to the adjacent swimlane. |
| Ctrl or Command + Z | Undo a move. |
| Ctrl or Command + Y; Ctrl or Command + Shift + Z | Redo a move. |
| Enter / double-click | Invoke the application's card-open command. |
| Delete | Invoke the application's delete command when permitted. |
| Escape | Cancel an active or pending drag. |

Text editors and embedded buttons are excluded from board shortcut handling.

## Validation

```sh
dotnet run --project tests/Kanban.Core.Tests -c Release
dotnet run --project tests/Kanban.Headless.Tests -c Release
dotnet build samples/Kanban.Sample.Desktop -c Release
dotnet publish samples/Kanban.Sample.Browser -c Release -o artifacts/browser
python -m pip install playwright==1.58.0
python -m playwright install chromium
# In a second terminal, serve the published wwwroot with scripts/serve.py.
python tests/browser/validate.py --url http://127.0.0.1:8080
```

The browser suite obtains coordinates from realized Avalonia controls, sends actual Chromium pointer/key events, and then checks the resulting managed models. The diagnostics bridge is part of the **sample only**, not the reusable control package. It is not a production remote-control API.

See `docs/validation.md` for the validation methodology and evidence layout. The delivered validation summary identifies the exact tested source and run results. The 10,000-card scenario checks that card-container realization remains bounded by the viewports. It does not claim that columns or swimlane rows are virtualized, or that a synthetic load time is a universal performance guarantee.

## Integration boundaries

This is a preview library, not a finished project-management backend. Authentication, server-side permissions, persistence, collaboration, conflict resolution across clients, asynchronous approvals, attachments and application-specific editing belong to the host application. UI read-only flags are not a security boundary.

Moves are synchronous and rollback is in-memory. An adapter must implement deterministic, synchronous, reversible placement setters; do not put network calls inside `SetPlacement`. Use the cancellable preview and application orchestration around the board for remote approval/persistence. Keep mutations on one owning UI thread and use `Controller.DeferRefresh()` for bulk updates.

The browser publish currently disables trimming to keep template/reflection bindings predictable. Native AOT, aggressive trimming, screen readers, Safari/Firefox and physical mobile-device coverage are not certified by this preview. The supplied desktop sample compiles separately; browser validation is not a substitute for a Windows/macOS/Linux desktop interaction matrix.

Further API, templating and history details: [`docs/api.md`](docs/api.md).
