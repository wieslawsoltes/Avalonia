using System;
using System.Collections.Generic;
using Avalonia.Collections.Pooled;
using Avalonia.Utilities;

namespace Avalonia.PropertyStore;

// Keep weak children alive only for the duration of one propagation operation.
// The normal ownership path borrows its existing list without allocating.
internal readonly struct InheritanceChildren(
    AvaloniaObject owner, IReadOnlyList<AvaloniaObject>? strong, PooledList<AvaloniaObject>? weak) : IDisposable
{
    public int Count => strong?.Count ?? weak?.Count ?? 0;
    public AvaloniaObject? this[int index]
    {
        get
        {
            if (strong is not null)
                return strong[index];
            var child = weak![index];
            // A preceding child's callback may have detached or reparented this
            // child after the live snapshot was captured.
            return ReferenceEquals(child.InheritanceParent, owner) ? child : null;
        }
    }

    public void Dispose()
    {
        if (weak is not null)
            WeakHashList<AvaloniaObject>.ReturnToSharedPool(weak);
    }
}
