using System;

namespace Avalonia;

/// <summary>Controls how explicit property operations affect bindings and current values.</summary>
/// <remarks>These behaviors are opt-in through property metadata. The default preserves Avalonia's existing value lifetime.</remarks>
[Flags]
public enum BindingValueBehavior
{
    /// <summary>Preserves existing bindings when assigning or clearing a value.</summary>
    None = 0,
    /// <summary>Replaces local bindings which cannot write to their source when an explicit local value is assigned.</summary>
    /// <remarks>Two-way and one-way-to-source expressions, including style expressions, handle explicit writes and remain attached. Binding publications and SetCurrentValue do not replace bindings.</remarks>
    ReplaceNonWritableBindingOnSetValue = 1,
    /// <summary>Removes the local binding before an explicit ClearValue operation resets the property.</summary>
    RemoveBindingOnClearValue = 2,
    /// <summary>Releases a current-value override when its originating style or template value entry is removed or deactivated.</summary>
    ResetCurrentValueOnSourceChange = 4,
    /// <summary>Prevents a binding from writing values contributed by a higher-priority source back to its own source.</summary>
    /// <remarks>Explicit source updates use the binding's underlying value when an animation overrides it.</remarks>
    IgnoreInactiveBindingSourceUpdates = 8,
}
