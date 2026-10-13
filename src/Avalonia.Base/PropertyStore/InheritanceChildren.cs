using System;
using System.Collections.Generic;
using Avalonia.Collections.Pooled;
using Avalonia.Utilities;

namespace Avalonia.PropertyStore;

// Keep weak children alive only for the duration of one propagation operation.
// The normal ownership path borrows its existing list without allocating.
internal readonly struct InheritanceChildren(
    IReadOnlyList<AvaloniaObject>? strong, PooledList<AvaloniaObject>? weak) : IDisposable
{
    public int Count => strong?.Count ?? weak?.Count ?? 0;
    public AvaloniaObject this[int index] => strong is not null ? strong[index] : weak![index];

    public void Dispose()
    {
        if (weak is not null)
            WeakHashList<AvaloniaObject>.ReturnToSharedPool(weak);
    }
}
