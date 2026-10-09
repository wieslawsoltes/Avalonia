namespace Avalonia.PropertyStore;

internal partial class ValueStore
{
    /// <summary>
    /// Recognizes a complete inheritance source containing at most one inherited value.
    /// A deeper ancestor or several inherited properties always uses the general snapshot.
    /// </summary>
    private static bool TryGetSingleInheritedValue(ValueStore? ancestor, out EffectiveValue? value)
    {
        value = null;
        if (ancestor is null)
            return true;

        // Do not partially traverse a compound chain and then repeat all that work in the
        // general path. These fields are internal state, not user-overridable callbacks.
        if (ancestor.InheritanceAncestor is not null || ancestor._inheritedValueCount != 1)
            return false;

        var count = ancestor._effectiveValues.Count;
        for (var i = 0; i < count; ++i)
        {
            var candidate = ancestor._effectiveValues.GetValue(i);
            if (candidate.Property.Inherits)
            {
                value = candidate;
                return true;
            }
        }

        // Be conservative if the count invariant ever changes during framework evolution.
        return false;
    }
}
