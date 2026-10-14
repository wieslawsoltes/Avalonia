# Validation and reproducibility

The control is validated at three levels. The supplied build report records the exact source commit and the actual results; the presence of a test file is not evidence that it passed.

## Core specifications

`dotnet run --project tests/Kanban.Core.Tests -c Release`

Exercises stable indexing, original-model identity, insertion-anchor semantics, multi-card transactions, WIP modes, locked/read-only rules, swimlane rules, cancellation, stale revisions, policy reentrancy, reversible adapter failures, bounded history, external edits, source batching, lifecycle and a 10,000-card source. The console runner exits unsuccessfully on the first failed specification.

## Avalonia headless tests

`dotnet run --project tests/Kanban.Headless.Tests -c Release`

Instantiates the actual XAML themes and controls, measures and arranges the visual tree, and verifies selection, projection, filtering, sorting, column/lane layout, theme switching, virtualization, JSON round trips and detach/reattach behavior. It also replaces templates with ones missing optional parts to verify that the engine remains usable.

## Published-browser input tests

Publish `samples/Kanban.Sample.Browser`, serve its `wwwroot` using `scripts/serve.py`, then run:

```sh
python tests/browser/validate.py --url http://127.0.0.1:8080 --output artifacts/validation
```

The test uses Playwright Chromium against the real .NET/Avalonia WebAssembly application. It sends browser mouse and keyboard events. A sample-only managed diagnostics bridge reports original source state and realized Avalonia control coordinates; no replacement HTML card interface is used.

Checks include keyboard moves and history, single/multi-card drag-and-drop, insertion order, WIP rejection, locks/read-only behavior, cancellation, stale-data rejection, custom workflow policy, typing into search and the editor, creation, collapse/expand, swimlanes, theme switching, JSON round trips and virtualized 10,000-card scrolling. Screenshots include light, dark, compact, swimlane, stress and narrow layouts. Failures are recorded in `report.json` and the process exits nonzero even when later independent checks continue.

A GitHub Actions workflow compiles the desktop host separately, publishes the browser host, executes the three test layers and creates the two NuGet packages. Intermediate stages collect diagnostics independently, but the final gate requires every stage's actual outcome to be successful. A continued-error step's displayed conclusion alone is not a pass result.

## Evidence bundle

The delivered validation bundle contains the browser JSON report, screenshots, core/headless output and browser build log. Its accompanying validation summary identifies the tested commit, CI run and artifact checksum. Source archives are checked against the CI source capture before delivery.

Performance measurements are scoped to the particular machine and data set. `LastLoadMilliseconds` measures source creation and projection, not total time to the first rendered frame. Realized-container counts demonstrate card virtualization; they do not certify unlimited column/swimlane scalability.

Desktop interaction on all three operating systems, physical touch hardware, screen readers, Safari/Firefox, Native AOT and aggressive trimming require additional platform-specific qualification. A browser success is not a claim that these untested environments have passed.
