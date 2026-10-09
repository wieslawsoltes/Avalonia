using System;
using System.Collections.Generic;
using Avalonia.Styling.Activators;
using Avalonia.Utilities;

#nullable enable

namespace Avalonia.Styling
{
    /// <summary>
    /// A selector that matches the common case of a type and/or name followed by a collection of
    /// style classes and pseudoclasses.
    /// </summary>
    internal sealed class TypeNameAndClassSelector : Selector
    {
        private readonly Selector? _previous;
        private List<string>? _classes;
        private Type? _targetType;
        private string? _selectorString;

        public static TypeNameAndClassSelector OfType(Selector? previous, Type targetType)
        {
            var result = new TypeNameAndClassSelector(previous);
            result._targetType = targetType;
            result.IsConcreteType = true;
            return result;
        }
        public static TypeNameAndClassSelector Is(Selector? previous, Type targetType)
        {
            var result = new TypeNameAndClassSelector(previous);
            result._targetType = targetType;
            result.IsConcreteType = false;
            return result;
        }
        public static TypeNameAndClassSelector ForName(Selector? previous, string name)
        {
            var result = new TypeNameAndClassSelector(previous);
            result.Name = name;
            return result;
        }
        public static TypeNameAndClassSelector ForClass(Selector? previous, string className)
        {
            var result = new TypeNameAndClassSelector(previous);
            result.Classes.Add(className);
            return result;
        }
        TypeNameAndClassSelector(Selector? previous) { _previous = previous; }

        internal override bool InTemplate => _previous?.InTemplate ?? false;
        /// <summary>Gets the name of the control to match.</summary>
        public string? Name { get; set; }
        internal override Type? TargetType => _targetType ?? _previous?.TargetType;
        internal override bool IsCombinator => false;
        /// <summary>Whether this selector matches the concrete type or assignable types.</summary>
        public bool IsConcreteType { get; private set; }
        /// <summary>The style classes which the selector matches.</summary>
        public IList<string> Classes => _classes ??= new();
        public override string ToString(Style? owner) => _selectorString ??= BuildSelectorString(owner);

        private protected override SelectorMatch Evaluate(StyledElement control, IStyle? parent, bool subscribe)
        {
            if (_targetType is { } ownType)
            {
                // Resolve an owned constraint directly and let the runtime perform its
                // assignability operation. A second managed last-type cache added cold/tiered
                // overhead and required excluding arbitrary mutable Type implementations.
                var controlType = control.StyleKey ?? control.GetType();
                if (IsConcreteType ? controlType != ownType : !ownType.IsAssignableFrom(controlType))
                    return SelectorMatch.NeverThisType;
            }
            else if (TargetType != null)
            {
                // Inherited constraints can change through Or/nesting callbacks, including
                // during StyleKey access. Preserve the original repeated resolution here.
                var controlType = control.StyleKey ?? control.GetType();
                if (IsConcreteType)
                {
                    if (controlType != TargetType) return SelectorMatch.NeverThisType;
                }
                else if (!TargetType.IsAssignableFrom(controlType))
                    return SelectorMatch.NeverThisType;
            }
            if (Name != null && control.Name != Name) return SelectorMatch.NeverThisInstance;
            if (_classes is { Count: > 0 })
            {
                if (subscribe)
                    return new SelectorMatch(new StyleClassActivator(control.Classes, _classes));
                if (!StyleClassActivator.AreClassesMatching(control.Classes, _classes))
                    return SelectorMatch.NeverThisInstance;
            }
            return Name == null ? SelectorMatch.AlwaysThisType : SelectorMatch.AlwaysThisInstance;
        }

        private protected override Selector? MovePrevious() => _previous;
        private protected override Selector? MovePreviousOrParent() => _previous;

        private string BuildSelectorString(Style? owner)
        {
            var builder = StringBuilderCache.Acquire();
            if (_previous != null) builder.Append(_previous.ToString(owner, true));
            if (TargetType != null)
            {
                if (IsConcreteType) builder.Append(TargetType.Name);
                else
                {
                    builder.Append(":is(");
                    builder.Append(TargetType.Name);
                    builder.Append(")");
                }
            }
            if (Name != null) { builder.Append('#'); builder.Append(Name); }
            if (_classes is { Count: > 0 })
            {
                foreach (var c in _classes)
                {
                    if (!c.StartsWith(":")) builder.Append('.');
                    builder.Append(c);
                }
            }
            return StringBuilderCache.GetStringAndRelease(builder);
        }
    }
}
