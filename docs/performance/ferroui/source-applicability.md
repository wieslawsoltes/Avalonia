# Applying the source study to Avalonia rather than to a verbatim port

## Reviewed source

The study's most recent performance-directory revision at the 2026-10-09 review is [FerroUI `cb16102d4ca77a7af46d938697c918a5117c54b7`](https://github.com/wieslawsoltes/FerroUI/tree/cb16102d4ca77a7af46d938697c918a5117c54b7/docs/porting/performance). Its [overview](https://github.com/wieslawsoltes/FerroUI/blob/cb16102d4ca77a7af46d938697c918a5117c54b7/docs/porting/performance/README.md) and nine designs distinguish measured observations, estimates and hypotheses. Those figures describe the Rust port and its workloads, not this managed implementation.

The source requires FerroUI to follow upstream statement for statement. Consequently it rejects several proposed resource, typed-binding and text caches because Avalonia did not already have those implementations. That is a port-maintenance constraint, not evidence that every such cache is semantically invalid or slower in Avalonia.

This PR implements the requested managed-framework optimizations. Its contract is to preserve effective values, priorities, observable notifications and their order, extension callbacks, mutable inputs, ownership and rendered output. New internal algorithms must be justified by their own tests and measurements. It would be inaccurate to describe the whole PR as merely copying optimizations already implemented in FerroUI or already present upstream.

## Substitutions and explicit exclusions

**Dispatch and inheritance.** C# does not have the Rust macro-generated forwarding-closure chain. Adding a closed-world registry would threaten external overrides rather than remove that port-specific cost. Object-major inheritance batching is excluded because changing property-major cross-object notification order is observable. The branch instead improves rentals and allocation while keeping those callbacks and ordering.

**Styles, resources and bindings.** Immutable selector plans are reusable; live class/property/name/StyleKey/parent/container decisions are not cached away. Resource caches store weak resolution locations rather than values, so non-shared factories, found-null and replacement remain live. Custom providers retain their probes and disable incompatible location reuse. A same-type styled template-binding specialization is new; converters, different types, direct properties and other guarded cases keep the general implementation. Reflection syntax is shared, not binding observers or source lifetimes.

**Text and rendering.** The shaping cache does not accept arbitrary slices, memory managers, mutable buffers or explicit features. Returned buffers are independent. Default single-run metrics are generation-checked and guarded against complex paragraphs and custom renderers. Cross-layout native geometry and blobs have explicit leases; cache eviction cannot free a blob used by another wrapper. The raw native run-buffer view removes a managed wrapper but does not pretend native font/blob allocation is gone. Existing dirty-region rendering remains intact rather than being replaced without evidence.

**Builds and measurement.** Cargo optimization levels and Emscripten linking switches are not .NET library defaults. Interpreter, AOT, symbol-retaining and JIT modes are measured as explicit deployment experiments. The 26 native scenarios and browser list/tree fixtures are independent Avalonia workloads, not the source's exact TableView benchmark. Counter scope and global atomic snapshots are documented in `counters-and-calibration.md`; they are not a byte-for-byte port of Rust's thread-local virtual-dispatch counter map. Symbol metadata is inspected on actual built modules; arbitrary cross-build function-index equivalence and hardware GPU presentation are not assumed.

## Meaning of completion

Implementation coverage means that each design has a concrete managed implementation, an existing mechanism, or a justified incompatibility/port-only boundary in the coverage matrix. It does not mean every proposed algorithm is enabled for all public extension points, nor that every measured workload is faster. A passing correctness run, a report-only benchmark run and acceptance of a performance tradeoff are distinct facts. Exact revisions and positive timing/allocation deltas must remain visible in reports.
