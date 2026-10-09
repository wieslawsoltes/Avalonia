# Remaining regression follow-up

Baseline: `a9429a328057befa287ffb5e981f58b86a86eda0`. Previous runtime: `fa8bb8c926cf6d1271b23d6e89a06633051f56cf`.

## Single-selector dispatch

The single-selector branch previously shared a method with compound-plan evaluation and the AND activator builder. Split the compound/container-query path out of the small inlinable dispatch method. The fast branch still evaluates the selector and returns the original match/activator. Previous links are read exactly once by the dispatcher; a matching combinator is still evaluated by `Match`, with the existing result-category conversion. Container queries cannot take the fast branch. No result, type, class or parent state is memoized.

New tests exercise both subscribing and immediate matches, live StyleKey changes, null container queries, compound property constraints and reparenting after the evaluation plan has been created. Existing selector/counter tests remain required.

Performance acceptance requires new measurements; this source change is not itself proof of speedup. Existing scenarios, operation counts, warm-up and screen thresholds are unchanged.

## Correction required in the preceding validation report

The AOT numbers transcribed in `validation-regression-repairs.md` and PR #8 do not match the cited job `113828663614` in run `37933178593`. Its actual log reports software list-thumb **-8.7%**, software tree-thumb **+0.6%**, default/WebGL tree-wheel **-11.3%**, default/WebGL tree-thumb **-8.0%**, and default/WebGL list-thumb **+2.4%**. Runtime bytes are **39,434,843 -> 39,620,575**, not the previously transcribed values. The incorrect table is not evidence of those large regressions and must not be used to claim a later code change fixed them. The small positive observations and interpreter tree/idle cases still need measurement. Source: https://github.com/wieslawsoltes/Avalonia/actions/runs/37933178593/job/113828663614
