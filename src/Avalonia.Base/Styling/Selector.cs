using System;
using System.Threading;
using Avalonia.Styling.Activators;

#nullable enable

namespace Avalonia.Styling
{
    /// <summary>
    /// A selector in a <see cref="Style"/>.
    /// </summary>
    public abstract class Selector
    {
        private EvaluationPlan? _evaluationPlan;

        /// <summary>
        /// Gets a value indicating whether either this selector or a previous selector has moved
        /// into a template.
        /// </summary>
        internal abstract bool InTemplate { get; }

        /// <summary>
        /// Gets a value indicating whether this selector is a combinator.
        /// </summary>
        /// <remarks>
        /// A combinator is a selector such as Child or Descendent which links simple selectors.
        /// </remarks>
        internal abstract bool IsCombinator { get; }

        /// <summary>
        /// Gets the target type of the selector, if available.
        /// </summary>
        internal abstract Type? TargetType { get; }

        /// <summary>
        /// Tries to match the selector with a control.
        /// </summary>
        /// <param name="control">The control.</param>
        /// <param name="parent">
        /// The parent style, if the style containing the selector is a nested style.
        /// </param>
        /// <param name="subscribe">
        /// Whether the match should subscribe to changes in order to track the match over time,
        /// or simply return an immediate result.
        /// </param>
        /// <returns>A <see cref="SelectorMatch"/>.</returns>
        internal SelectorMatch Match(StyledElement control, IStyle? parent = null, bool subscribe = true)
        {
#if AVALONIA_PERF_COUNTERS
            Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.SelectorMatches);
#endif
            var match = MatchUntilCombinator(control, this, parent, subscribe, out var combinator);
            if (match.IsMatch && combinator is object)
            {
                match = match.And(combinator.Match(control, parent, subscribe));
                match = match.Result switch
                {
                    SelectorMatchResult.AlwaysThisType => SelectorMatch.AlwaysThisInstance,
                    SelectorMatchResult.NeverThisType => SelectorMatch.NeverThisInstance,
                    _ => match
                };
            }
#if AVALONIA_PERF_COUNTERS
            if (match.IsMatch)
                Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.SelectorSuccessfulMatches);
#endif
            return match;
        }

        public override string ToString() => ToString(null);

        /// <summary>
        /// Returns a string representing the selector, with the nesting separator (`^`) replaced with
        /// the parent selector.
        /// </summary>
        /// <param name="owner">The owner style.</param>
        public abstract string ToString(Style? owner);

        /// <summary>
        /// Returns a string representing the selector, with the nesting separator (`^`) replaced with
        /// the parent selector.
        /// </summary>
        /// <param name="owner">The owner style.</param>
        /// <param name="hasNext">Whether there is a selector that comes after this one.</param>
        internal virtual string ToString(Style? owner, bool hasNext) => ToString(owner);

        /// <summary>
        /// Evaluates the selector for a match.
        /// </summary>
        /// <param name="control">The control.</param>
        /// <param name="parent">
        /// The parent style, if the style containing the selector is a nested style.
        /// </param>
        /// <param name="subscribe">
        /// Whether the match should subscribe to changes in order to track the match over time,
        /// or simply return an immediate result.
        /// </param>
        /// <returns>A <see cref="SelectorMatch"/>.</returns>
        private protected abstract SelectorMatch Evaluate(StyledElement control, IStyle? parent, bool subscribe);

        /// <summary>
        /// Moves to the previous selector.
        /// </summary>
        private protected abstract Selector? MovePrevious();

        /// <summary>
        /// Moves to the previous selector or the parent selector.
        /// </summary>
        private protected abstract Selector? MovePreviousOrParent();

        internal virtual void ValidateNestingSelector(bool inControlTheme, int templateCount = 0)
        {
            var s = this;
            if (inControlTheme)
            {
                if (!s.InTemplate && s.IsCombinator)
                    throw new InvalidOperationException("ControlTheme style may not directly contain a child or descendent selector.");
                if (s is TemplateSelector && templateCount++ > 0)
                    throw new InvalidOperationException("ControlTemplate styles cannot contain multiple template selectors.");
            }
            var previous = s.MovePreviousOrParent();
            if (previous is null)
            {
                if (s is not NestingSelector)
                    throw new InvalidOperationException("Child styles must have a nesting selector.");
            }
            else previous.ValidateNestingSelector(inControlTheme, templateCount);
        }

        private static SelectorMatch MatchUntilCombinator(StyledElement control, Selector start,
            IStyle? parent, bool subscribe, out Selector? combinator)
        {
            combinator = null;
            var previous = start.MovePrevious();
            var single = previous is null || previous.IsCombinator;
            if (single && parent is not ContainerQuery)
            {
                // There is no AND operation to build for one selector. Return its original
                // match/activator directly, without a helper call and struct reconstruction.
#if AVALONIA_PERF_COUNTERS
                Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.SelectorEvaluations);
#endif
                var match = start.Evaluate(control, parent, subscribe);
                if (match.IsMatch) combinator = previous;
                return match;
            }

            var activators = new AndActivatorBuilder();
            SelectorMatchResult result;
            if (single)
            {
                // Container queries still participate in the original AND and activation.
                result = EvaluateNode(control, start, parent, subscribe, ref activators);
                if (result >= SelectorMatchResult.Sometimes) combinator = previous;
            }
            else
            {
                var plan = start._evaluationPlan ?? start.CreateEvaluationPlan();
                result = SelectorMatchResult.NeverThisInstance;
                foreach (var selector in plan.Selectors)
                {
                    result = EvaluateNode(control, selector, parent, subscribe, ref activators);
                    if (result < SelectorMatchResult.Sometimes)
                        break;
                }
                if (result >= SelectorMatchResult.Sometimes) combinator = plan.Combinator;
            }
            return result == SelectorMatchResult.Sometimes ?
                new SelectorMatch(activators.Get()) : new SelectorMatch(result);
        }

        private EvaluationPlan CreateEvaluationPlan()
        {
            var count = 1;
            var current = this;
            while (current.MovePrevious() is { } previous && !previous.IsCombinator)
            {
                ++count;
                current = previous;
            }
            var combinator = current.MovePrevious();
            var selectors = new Selector[count];
            current = this;
            for (var i = count - 1; i >= 0; --i)
            {
                selectors[i] = current;
                if (i > 0) current = current.MovePrevious()!;
            }
            var plan = new EvaluationPlan(selectors, combinator);
            return Interlocked.CompareExchange(ref _evaluationPlan, plan, null) ?? plan;
        }

        private static SelectorMatchResult EvaluateNode(StyledElement control, Selector selector,
            IStyle? parent, bool subscribe, ref AndActivatorBuilder activators)
        {
#if AVALONIA_PERF_COUNTERS
            Diagnostics.PerformanceCounters.Increment(Diagnostics.PerformanceCounter.SelectorEvaluations);
#endif
            var containerMatchesSometimes = false;
            SelectorMatch match;
            if (parent is ContainerQuery container)
            {
                match = container.Query?.Evaluate(control, container.Parent, subscribe, container.Name) ?? SelectorMatch.NeverThisInstance;
                if (!match.IsMatch) return match.Result;
                containerMatchesSometimes = match.Result == SelectorMatchResult.Sometimes;
                if (containerMatchesSometimes) activators.Add(match.Activator!);
            }
            match = selector.Evaluate(control, parent, subscribe);
            if (!match.IsMatch) return match.Result;
            if (match.Activator is object) activators.Add(match.Activator);
            return containerMatchesSometimes ? SelectorMatchResult.Sometimes : match.Result;
        }

        // Only immutable predecessor links are cached. Evaluate still reads live class/name,
        // property, StyleKey, parent, container-query and mutable Or-alternative state in order.
        private sealed class EvaluationPlan(Selector[] selectors, Selector? combinator)
        {
            internal Selector[] Selectors { get; } = selectors;
            internal Selector? Combinator { get; } = combinator;
        }
    }
}
