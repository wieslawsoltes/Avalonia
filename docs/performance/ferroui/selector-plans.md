# Design 04: shared immutable selector evaluation plans

A whole-host negative type index cannot safely skip selectors in all Avalonia cases: StyleKey is virtual and may change, Or alternatives can come from mutable lists, container queries depend on current parent state, and nested child styles must still be visited even when an outer selector misses. Caching a complete match or reading a custom StyleKey once instead of at its original callbacks would change observable behavior.

The applicable reusable work is the selector's immutable predecessor chain. `Selector` now computes its left-to-right evaluation plan once and reuses it across every host and target. Compound selectors avoid recursively reversing that chain on each match. Single-node selectors do not allocate a plan. Plans contain selector references and their combinator, never match results, targets, classes, resource values or resolved parents.

Each node is evaluated in the original order, including per-node container-query evaluation and activator creation. Matching still performs current StyleKey reads, type-prefilter checks, name/class/property tests and mutable Or evaluation. Combinators remain live and reparenting is not hidden. This complements the immutable type-constraint assignability cache rather than introducing an unsound closed-world host index.

`FerroUiSelectorPlanTests` verifies plan identity reuse, changing properties/classes, mutable alternatives, changed logical parents, reactive activation and allocation-free plan omission for simple selectors. Existing selector, nested-style, container-query and control-theme suites remain acceptance tests. Runtime timing must be read from measurements, not inferred from removal of recursion.
