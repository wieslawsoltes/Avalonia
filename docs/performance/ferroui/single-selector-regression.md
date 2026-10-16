# Single-selector dispatch and owned type constraints

Default-runtime measurements exposed cost in simple type misses, despite compound plans helping longer selector chains. The single-node path still constructed an AND builder and called a helper just to reconstruct the one match returned by Evaluate.

For one node without a parent ContainerQuery, MatchUntilCombinator returns that original SelectorMatch/activator directly. The combinator is still evaluated by Match, with the original result-category conversion. ContainerQuery and compound selectors retain their AND and activation paths. Instrumented builds still count exactly one evaluation for the one node; normal builds contain no counter calls.

TypeNameAndClassSelector reads its own immutable constraint directly rather than retrieving it through TargetType. StyleKey is still read at the same point on every call. Inherited constraints retain live repeated resolution because Or/nesting callbacks can change them.

## Follow-up after the default-runtime repeat

At `bb80607`, the first four native jobs had no calibrated flags, but the Linux default-runtime repeat flagged simple type misses and context-sensitive shaping. Those repeated results are retained, not discarded in favor of the first run. The extra managed last-assignable-type/result cache is now removed. An owned constraint delegates directly to Type.IsAssignableFrom, as the original did, while retaining the reduced owned-constraint and single-node dispatch. This removes the per-selector memo fields, runtime-type eligibility checks and a redundant managed cache over a runtime operation; arbitrary Type implementations are naturally evaluated on every call.

The shaping follow-up reads each native glyph position once and computes the same advance/offset arithmetic in place, instead of two helper calls and repeated span indexing. Its backing-memory resolver is explicitly inlinable. It retains surrounding context, the original negation/multiplication order, tab handling, features, glyph clusters, public mutation invalidation and optional metadata ownership. No runtime tiering setting, warm-up count, benchmark scenario or threshold changes.

Tests preserve single-class live activation, parent/combinator changes, exact StyleKey read counts, mutable TypeDelegator assignability, and type-versus-instance result categories. Existing container-query, dynamic-selector, compound-plan, exact shaping/context and raster tests remain required. The original simple-miss and compound benchmarks must both be considered; this source change is not itself evidence that the repeated timing flags have disappeared.
