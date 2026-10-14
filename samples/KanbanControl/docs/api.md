# API and templating contract

## Data and ownership

`KanbanController.Bind(items, columns, lanes, adapter)` builds an index keyed by ordinal, case-sensitive stable IDs. The source may be any `IEnumerable`. Observable source collections and `INotifyPropertyChanged` items/definitions are subscribed automatically. Call `Refresh()` explicitly for non-notifying models. Use `DeferRefresh()` to coalesce a batch into one rebuild; scopes are nested and disposal is idempotent.

Each card placement is `(ColumnKey, LaneKey, Order)`. An empty swimlane collection defines exactly one unnamed lane, with key `""`. Declared lane keys must be unique; column keys must be nonempty and unique. IDs must be nonempty and unique. Unmapped cards, duplicate IDs and invalid ranks produce diagnostics and reject moves rather than silently losing data.

`KanbanItem` is a convenience model with notifying title, description, keys, order, assignee, tags, priority, locked state, due date and checklist progress. Its immutable `Id` has an application-overridable initialization value.

The board passes original source objects to `CardTemplate`. `IKanbanAdapter` supplies identity, title, search text, placement, movement permission and a placement setter. The engine neither reorders nor replaces the application's source collection.

## Move transaction

```csharp
var result = board.Move(new KanbanMoveRequest(
    new[] { "TASK-1", "TASK-2" },
    ColumnKey: "doing",
    LaneKey: "platform",
    BeforeId: "TASK-5",
    ExpectedRevision: board.Controller.Revision));
```

`BeforeId` names the destination anchor **after excluding every moved card**. `null` appends. Explicit IDs avoid filtered-index ambiguity. A batch is inserted in request order. Destination ranks are normalized to finite integer-valued doubles; source rank gaps do not affect ordering. Equal input ranks use stable source-index ordering.

Validation checks source integrity, board/column/lane read-only state, `AllowDrop`, locked cards, `AllowLaneChange`, the anchor, WIP and the optional `MovePolicy`. `CardMoving` / `Controller.Moving` may cancel with a reason. Reentrant operations and move/history requests made inside an unfinished `DeferRefresh()` scope return `Busy`. Mutating the board in preview or policy invalidates the request instead of applying against a changed revision.

The adapter receives synchronous placement writes. If a write throws, earlier writes are rolled back in reverse order, including a partially written item. If rollback also fails, an `AggregateException` explicitly requests authoritative data reload. This is not database isolation: external observers may see intermediate property notifications. Adapter setters and their observers must not cause unrelated source mutations or perform asynchronous work during a transaction.

`KanbanMoveResult` distinguishes rejection causes; `IsWarning` reports a successful WIP warning. The board displays the message in `StatusText`. `CardsMoved` and `CardsMovedCommand` fire for successful new move transactions. Failed requests do not enter undo history.

## WIP semantics

A column's `MaximumWorkInProgress` applies across every swimlane and to the unfiltered source. Moving already-counted cards between lanes or reordering them does not increase WIP and is allowed even when the column is already over its limit. Incoming batches are accepted or rejected as a unit.

`WipLimitMode.Enforce` rejects an over-limit incoming batch. `Warn` accepts it and flags the result. `Ignore` does neither. Host-owned card creation/import is separate from moves; the sample checks limits when adding but permits imported historical data to be over-limit and visibly reports that state.

## History and external changes

The default `HistoryLimit` is 100 move transactions. Setting it to zero disables new history recording. Undo/redo restores the exact stored placements, not newly normalized approximations. A no-op does not create a history record. A new move clears redo.

Source membership, object-identity or placement changes outside the controller invalidate history. Metadata-only changes preserve it. History replay checks identity/current placement and refuses to overwrite externally changed data. Read-only state or newly locked explicitly moved cards block replay. Existing locked neighbors whose numeric ranks were normalized do not prevent restoring another card; their lane, column and locked state remain unchanged.

Replay is historical restoration, not a fresh workflow transition: current WIP, `AllowDrop`, lane-change policy and custom transition policy are not re-run. Hosts requiring remote approval or fresh business-rule validation for undo should disable built-in history and own that workflow. The history does not cover application-owned card creation, deletion, content edits or column layout changes.

Detaching a board unsubscribes its controller and clears history. Reattaching creates a fresh controller and binds the current source. Configure application policies again when restoring a detached board; keep persistent business policy outside the control.

## Template contexts

| Property | Data context |
| --- | --- |
| `CardTemplate` | Original source item. |
| `ColumnHeaderTemplate` | `KanbanColumnView`: `Definition`, `Lane`, `Board`, `Items`, counts and WIP state. |
| `ColumnFooterTemplate` | The same `KanbanColumnView`. |
| `EmptyTemplate` | The same cell view, displayed when the filtered cell is empty. |
| `LaneHeaderTemplate` | `KanbanLaneView`: `Definition`, `Board`, `Columns`, `Count`, `Label`, expanded state. |
| `Header`, `Footer` | Arbitrary host content in the board template. |

`CardTheme`, `ColumnTheme` and `SwimlaneTheme` accept replacement `ControlTheme` objects. Replace the board's ordinary `Template` for a completely different board visual tree. Use resource overrides for smaller changes instead of copying the whole theme.

### Named parts

| Control | Part | Expected type / purpose |
| --- | --- | --- |
| Board | `PART_ScrollViewer` | `ScrollViewer`; horizontal/vertical board auto-scroll. |
| Board | `PART_Rows` | `ItemsControl`; conventional row presentation, owned by the template. |
| Board | `PART_DragGhost` | `Control` positioned in a `Canvas`; optional drag overlay. |
| Column | `PART_Items` | `KanbanListBox`; selection, realized-card hit testing and vertical auto-scroll. |
| Column | `PART_Collapse` | Optional `Button`; toggles column collapse. |
| Column | `PART_Add` | Optional `Button`; invokes the host's add command. |
| Column | `PART_Resize` | Optional `Thumb`; resizes the shared column definition. |
| Swimlane | `PART_Collapse` | Optional `Button`; toggles lane collapse. |
| Card (default theme) | `PART_ContentPresenter`, `PART_Surface`, `PART_Insertion` | Theme-owned presentation and insertion indicator. |

Retemplating detaches old part handlers before wiring new ones. Missing optional parts must not crash the engine; omitted presentation/input parts naturally omit that UI functionality.

### State and resources

Board pseudo-classes: `:dragging`, `:readonly`, `:empty`, `:invalid`.
Card pseudo-classes: Avalonia's `:selected`, `:pointerover`, `:focus-visible`, `:disabled`, plus `:dragging` and `:drop-before`.

Theme resources: `KanbanBoardBrush`, `KanbanColumnBrush`, `KanbanCardBrush`, `KanbanBorderBrush`, `KanbanTextBrush`, `KanbanMutedBrush`, `KanbanHoverBrush`, `KanbanSelectedBrush`, `KanbanAccentBrush`, `KanbanDangerBrush`. Both light and dark dictionaries provide these keys.

## Virtualization and threading

Each column cell has a finite height and a `VirtualizingStackPanel` inside its own `ListBox`. `FitSingleLaneToViewport` defaults to `true`: a board without explicit swimlanes fills its available height. Set it to `false` to use `LaneHeight` for that board. Explicit swimlanes use `LaneHeight`. Do not replace that panel with a nonvirtualizing stack or give the card list unbounded vertical space when retaining virtualization is important. Card templates may have variable heights.

Large projection replacements issue one collection reset; small changes preserve containers where possible. Search and custom filtering run on the owning UI thread, using the current indexed source. The implementation indexes all cards in memory. It does not provide server-side paging or row/column virtualization; boards with thousands of swimlane cells need a different row host.

`LastLoadMilliseconds` in the sample measures model creation and projection work, not total time to the first fully rendered frame. Browser tests separately count realized card containers after layout.

## Host commands

`AddCardCommand` receives `KanbanAddRequest(ColumnKey, LaneKey)`.
`CardInvokedCommand` receives the original item.
`DeleteCardsCommand` receives the selected original objects as an array.
`CardsMovedCommand` receives `KanbanMovedEventArgs`.

The control checks `ICommand.CanExecute` before invocation. The host remains responsible for authorization, content validation, confirmations and durable changes. Sample context-menu commands are examples, not a server-side permission system.

The sample JSON format is versioned and uses a source-generated serialization context. Imports are size-limited, validate all IDs and placements and construct all incoming models before replacing the collection. Import does not execute card contents. The JSON editor is a sample convenience rather than a general interchange standard.
