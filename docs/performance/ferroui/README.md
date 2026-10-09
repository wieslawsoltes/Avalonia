# FerroUI performance designs applied to Avalonia

Branch: `perf/ferroui-runtime-optimizations`. [Pull request #8](https://github.com/wieslawsoltes/Avalonia/pull/8) targets **`wieslawsoltes/Avalonia:master`**, not upstream. Original Avalonia baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`.

The source is [FerroUI's performance study and nine designs](https://github.com/wieslawsoltes/FerroUI/tree/main/docs/porting/performance). Its Rust/WebAssembly profile percentages are not Avalonia measurements. The designs mix transferable work, machinery already present in Avalonia, Rust-only mechanisms and hypotheses that must not override observable framework behavior.

## Implemented changes and design disposition

| Design | Implementation or existing mechanism | Remaining scope / deliberate boundary |
| --- | --- | --- |
| 01 Virtual dispatch | Audited C# dispatch; the Rust macro forwarding chain does not exist here. | Do not skip extensible virtual callbacks or add a closed-world override registry. |
| 02 Notifications | Cache immutable name-only INPC event arguments; return and clear larger observer snapshots in `finally`. Typed value arguments and small-listener paths were already present. | No callback suppression, changed notification order or speculative dense-ID rewrite of all overrides. |
| 03 Inheritance | Rent the comparison dictionary only after the unchanged-ancestor check; always return it on exceptions. Indexed matching, identity checks and local-value pruning already existed. | Object-major batching was rejected because property-major cross-object order is observable. |
| 04 Tree attachment/styling | Cache the last assignability result for a selector's own immutable type constraint; preserve dynamic StyleKey reads and class/name activation. | A full per-host selector index and implicit-theme/resource cache with complete invalidation are not implemented. |
| 05 Recycling bindings | Avoid redundant same-type template conversion; reuse owned parsed reflection-binding syntax per description and in a bounded shared cache. Fresh observer state, type resolution and name-scope selection remain per instance. Compiled bindings already share their immutable path. | The object-valued sink remains; this is not a completely unboxed binding pipeline. Dynamic-resource host caching depends on design 04. |
| 06 Text layout | Bounded immutable short-run glyph snapshots, complete conservative keys, independent returned buffers, font-disposal checks and two-touch admission to avoid one-off snapshot allocation. | Context-sensitive slices and explicit features bypass caching. No cross-layout native glyph sharing or whole-line metrics shortcut. |
| 07 Composed-frame cost | Stateful static Skia cache factory removes captured allocations from retained text-blob hits; exception-safe builder rental. Blob retention and composition dirty-region mechanisms already existed. | No changes to dirty-subtree rendering, diagnostics defaults or browser idle scheduling; those need render/browser evidence. |
| 08 Build settings | Added separate native non-tiered-JIT and runtime-default measurement modes without changing library/application runtime defaults. | Cargo/Emscripten flags do not apply. Browser AOT/SIMD/size/startup deployment choices remain unmeasured. |
| 09 Measurement | Identical-harness, alternating exact-base/head runs; 17 native scenarios; Linux/macOS and two runtime modes; managed allocation and recycling checks; strict report validation with Python tests. | Browser wheel/drag traces, screenshot comparisons, instrumented feature counters and calibrated timing gates remain. |

## Documentation

[Implementation notes](implementation.md) describe notification, inheritance, selector, template-binding, text and Skia changes with their regression tests. [Binding syntax sharing](reflection-binding-path.md) explains per-description/global bounds, reentrant parser ownership and the first-use tradeoff. [Shaping admission](shaping-admission.md) records the unique-text regression that motivated the two-touch policy. [Measurement and reproduction](measurement.md) explains scenarios, platform/runtime modes and report validation. [Validation and measured results](validation.md) preserves the exact tested revision, successful checks, all four comparison tables and remaining regressions.

## Compatibility contract

Keep effective values, binding priorities, property-major notification order and reentrancy, source/subscription lifetimes, clear/detach/prepare/attach lifecycle, resource precedence, text metrics and rendered output. Never share mutable shaped buffers, observers, live binding expressions, resolved name scopes or native font ownership through syntax/data caches. Changes are internal; no public application API or diagnostics default is intentionally changed.

## Validation policy

The upstream build workflow skips forks. The fork-local `FerroUI performance` workflow runs six Release suites (Base, Controls, Markup, XAML, Skia and Themes) and affected .NET 8 library builds. `FerroUI native measurements` runs the independent baseline/head matrix and uploads raw data and provenance. Invalid output or logical differences fail; noisy elapsed-time changes are reported, not hidden or used as an uncalibrated hard gate.

A passing native test does not establish browser/GPU correctness or performance. Earlier commits' successful runs do not validate later code. Read the checks for the exact commit being evaluated. The PR remains draft while the broader requested scope and target-specific validation are incomplete.
