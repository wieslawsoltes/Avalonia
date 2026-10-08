# FerroUI performance designs: Avalonia implementation

This work maps the nine designs in [FerroUI's performance study](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance) to Avalonia. All work is on `perf/ferroui-runtime-optimizations` in `wieslawsoltes/Avalonia`, based on `a9429a328057befa287ffb5e981f58b86a86eda0`. The pull request targets this fork, not AvaloniaUI/Avalonia.

## Compatibility contract

Preserve effective values, binding priorities, notification order (including reentrancy), resource precedence and invalidation, text metrics, rendering output, and detach/attach lifecycle. Do not reuse mutable shaped buffers or skip virtual callbacks implemented by third-party subclasses. FerroUI's measured percentages describe its Rust/WebAssembly implementation, not Avalonia; they are not forecasts or measured gains for this branch.

## Design disposition (implementation in progress)

| Design | Avalonia treatment | Status |
| --- | --- | --- |
| 01 Virtual dispatch | C# virtual dispatch does not contain FerroUI's generated Rust forwarding chains. Do not rewrite extensible virtual callbacks. | Rust-specific mechanism does not apply |
| 02 Property notifications | Audit typed event arguments, listener snapshots, and repeated publication; retain callbacks and reentrancy. | Under implementation |
| 03 Inheritance parent changes | Preserve property-major notification order. Inspect indexed old/new matching and eliminate unnecessary pooled work with exception-safe ownership. | Under implementation |
| 04 Tree attachment and styling | Audit existing theme/style caches and invalidation before adding caches; preserve lookup precedence and selector order. | Under implementation |
| 05 Recycling bindings | Preserve clear/dispose/recreate sequence. Audit compiled immutable paths and template-binding conversion overhead. | Under implementation |
| 06 Text layout | Cache only immutable results with complete keys and bounded lifetime; validate text, culture, direction, features, spacing and font identity. | Under implementation |
| 07 Compositor frame cost | Audit retained Skia blobs and dirty-region culling. Preserve diagnostics defaults, animations and custom rendering. | Under implementation |
| 08 Build settings | Rust/Cargo/emscripten flags are not .NET settings. Measure .NET-specific alternatives without globally changing app deployment policy. | Assessment pending |
| 09 Measurement | Add fork-local validation and reproducible benchmark evidence; noisy timing differences are reports, not uncalibrated hard gates. | Infrastructure in progress |

## Validation

The existing upstream build workflow deliberately excludes forks. The companion `FerroUI performance` workflow is fork-local, read-only, publishes the exact source snapshot for reproduction, and runs the affected managed unit-test suites. Implementation details, tests and measurements are added to this directory as each optimization lands. A source audit or a benchmark definition is not a passing test run; pending results remain explicitly pending.
