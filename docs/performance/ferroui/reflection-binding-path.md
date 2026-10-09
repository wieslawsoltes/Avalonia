# Design 05 follow-up: reuse parsed reflection-binding paths

## Original cost and ownership problem

`ReflectionBinding.CreateInstance` reparsed `Path` for every target. A column/template can reuse one binding description while creating many live expressions; the property-name strings and grammar nodes are identical across those parses. The old path also passed a shared static parser scratch list through user-supplied type-resolution callbacks. A reentrant parse could overwrite the list being enumerated.

## Implementation

Each `ReflectionBinding` now owns at most one parsed-path snapshot, keyed by the current path's string value. A changed path replaces it on the next instantiation; invalid expressions still throw and are not installed. A local reference keeps the snapshot stable across callbacks. No global cache or target reference is introduced.

Only grammar syntax is reused. `ExpressionNodeFactory` still constructs fresh expression nodes, resolves types with the current resolver, reads the current name scope, creates source nodes and selects source/anchor for every instantiation. Indexer argument syntax is read-only in the factory; converted indexes, accessors, observers and subscriptions remain in each expression. Priority, validation, converter, fallback, null, delay and update-trigger settings still follow the original path.

This complements the existing compiled-binding path sharing and the branch's template-binding conversion fast path; it does not share mutable observer nodes or suppress recycling notifications.

## Tradeoff and tests

A one-use description now retains a small parsed-list/snapshot allocation rather than borrowing the global scratch list. Reused descriptions avoid all subsequent path parsing and AST allocation. Measurements must include both one-use and reused descriptions; a microbenchmark of only reuse would hide this tradeoff.

`FerroUiReflectionBindingTests` covers independent sources and two-way writes, disposal/subscription ownership, value-equal and changed paths, invalid-path recovery, a changed type resolver, a reentrant parser call during type resolution, name-scope changes and indexers whose per-target argument conversions differ.

The fork-local Base, Controls, Markup, XAML and Themes suites validate integration. Paired benchmarks are required before assigning an elapsed-time improvement to this optimization. No claim of a fully typed/unboxed binding pipeline is made.
