# Design 05 follow-up: reuse parsed reflection-binding paths

## Original cost and ownership problem

`ReflectionBinding.CreateInstance` reparsed `Path` for every target. A column/template can reuse a description, or recreate descriptions with the same path, while creating many live expressions. The property-name strings and grammar nodes are identical across those parses. The original code also passed a shared static parser scratch list through user-supplied type-resolution callbacks. A reentrant parse could overwrite the list being enumerated.

## Implementation

Each `ReflectionBinding` retains one parsed-path snapshot for its current path. A changed path selects another snapshot on the next instantiation; invalid expressions still throw and are not cached. A local reference keeps the snapshot stable across callbacks.

A second, bounded cache shares pure syntax between descriptions: at most 128 paths, each at most 256 characters and 32 grammar nodes. FIFO eviction drops the cache reference; existing descriptions safely retain their snapshots. Oversized expressions are parsed and owned by their description but never globally retained. The cache stores no resolved types, source/target objects, converters, name scopes or observer state. A lock protects shared lookup/publication; parsing uses an independent list outside the lock. Common re-instantiation on an existing description does not take that lock.

`ExpressionNodeFactory` still constructs fresh expression nodes, resolves types with the current resolver, reads the current name scope, creates source nodes and selects source/anchor for every instantiation. Indexer argument syntax is only read; converted indexes, accessors, observers and subscriptions remain in each expression. Priority, validation, converter, fallback, null, delay and update-trigger settings follow the original path.

## Measurement-driven refinement

The initial per-description implementation was measured in [run 37889587051](https://github.com/wieslawsoltes/Avalonia/actions/runs/37889587051): reused-description construction improved by a paired median 24.3% and allocated 128 fewer bytes per operation. Fresh descriptions instead allocated 128 more bytes per operation. The shared syntax cache addresses that first-use cost for recurring paths, including code that constructs a new description per cell. Truly unique paths still incur parsing and snapshot ownership; bounds prevent indefinite global retention. Results for this refinement must be taken from its own commit's run, not inferred from the earlier numbers.

## Regression coverage

`FerroUiReflectionBindingTests` covers independent sources and two-way writes, disposal/subscription ownership, value-equal and changed paths, invalid-path recovery, changed type resolution, reentrant parser calls, name-scope changes and target-specific indexer conversions. `FerroUiSharedBindingPathTests` adds separate descriptions sharing syntax without sharing source values, the capacity bound, and exclusion of oversized paths/trees.

The fork-local Base, Controls, Markup, XAML and Themes suites validate integration. The same native harness measures both one-use and reused descriptions against the original baseline. This complements compiled-binding path sharing and the branch's template-binding conversion fast path; it is not a fully typed/unboxed binding pipeline and does not suppress recycling notifications.
