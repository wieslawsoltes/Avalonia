# Local resource writes: remove dispatch overhead, not safety checks

## Evidence

The unchanged Linux default-runtime scenario at `1daa96863f71eacbd50d8c5712a9062595e2b031` reported local replacement at 101.5 ns/op versus baseline 94.5, with paired median +9.6%. The same source's non-tiered comparison was -1.6%, and the preceding default-runtime observation was +2.9%. These differences motivate reducing actual call/branch work; they are not proof that every percentage is a source effect. The original benchmark, A/A screen and workload are unchanged.

## Implementation and compatibility

The guarded string indexer combines its two plain-field flags with boolean OR, eliminating one short-circuit branch. Reading both flags has no callbacks or side effects. The stable/untracked eligibility contract is identical; arbitrary keys, unstable stored keys and cache dependencies still use the callback-sensitive path.

The fast path accesses `_inner ??= new Dictionary<object, object?>()` directly rather than calling the separate lazy `Inner` getter. First-use construction, assignment and notification order are unchanged. Other paths continue using the existing getter.

`ResourceProvider.RaiseResourcesChanged` is a small inlining candidate and reads its private owner field directly instead of calling the non-virtual field-only `Owner` getter. It still snapshots the current owner once, constructs event arguments only for a non-null owner, calls every required notification and propagates exceptions. Owner changes and virtual owner lifecycle callbacks are untouched. No notification is batched, suppressed or moved before the assignment.

## Regression coverage

Eight cases cover allocation-free ownerless publication, reentrant owner replacement, throwing owner callbacks, first string assignment before a throwing host callback, and all combinations of tracked/untracked and stable/opaque keys through insert, null replacement and remove. Existing arbitrary equality/type/theme callback and dependency-establishment tests remain required.

This patch adds no per-instance field or retention and changes no public API. Its own configured suites and unchanged native/browser runs must complete before claiming a measured improvement. Default-runtime transitions and shared-runner variance remain visible, not normalized away.
