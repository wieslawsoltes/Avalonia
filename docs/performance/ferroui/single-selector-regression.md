# Single-selector dispatch and owned type constraints

The default-runtime measurements exposed cost in simple type misses, despite compound plans helping longer selector chains. The single-node path still constructed an AND builder and called a helper just to reconstruct the one match returned by Evaluate.

For one node without a parent ContainerQuery, MatchUntilCombinator now returns that original SelectorMatch/activator directly. The combinator is still evaluated by Match, with the original result-category conversion. ContainerQuery and compound selectors retain their AND and activation paths. Instrumented builds still count exactly one evaluation for the one node; normal builds contain no counter calls.

TypeNameAndClassSelector reads its own immutable constraint directly rather than retrieving it through TargetType, then uses a reference-only warmed type check. StyleKey is still read at the same point on every call. Inherited constraints retain live repeated resolution because Or/nesting callbacks can change them. Cache installation is restricted to actual runtime types: arbitrary Type subclasses can change IsAssignableFrom or equality behavior without a framework event and are therefore evaluated each time.

Tests preserve single-class live activation, parent/combinator changes, exact StyleKey read counts, mutable TypeDelegator assignability, and type-versus-instance result categories. Existing container-query, dynamic-selector and compound-plan tests remain required. The original simple-miss and compound benchmarks are unchanged and must both be considered before assigning a measured improvement.
