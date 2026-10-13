using System;
using System.Collections.Generic;
using Avalonia.Collections.Pooled;

namespace Avalonia.LogicalTree;

// Public compatibility projections must not disconnect the framework-owned
// template tree from attachment, inherited values, resources or styles.
internal readonly struct LogicalChildrenTraversal(
    ILogical owner, IReadOnlyList<ILogical>? owned, IReadOnlyList<ILogical>? projected)
{
    public Enumerator GetEnumerator() => new(owner, owned, projected);

    internal struct Enumerator(ILogical owner, IReadOnlyList<ILogical>? owned, IReadOnlyList<ILogical>? projected) : IDisposable
    {
        private readonly int _ownedCount = owned?.Count ?? 0;
        private readonly PooledList<ILogical>? _projected = Snapshot(owner, owned, projected);
        private int _ownedIndex;
        private int _projectedIndex;

        public ILogical Current { get; private set; } = null!;

        public bool MoveNext()
        {
            if (_ownedIndex < _ownedCount)
            {
                Current = owned![_ownedIndex++];
                return true;
            }

            while (_projectedIndex < (_projected?.Count ?? 0))
            {
                var child = _projected![_projectedIndex++];
                if (!ReferenceEquals(child.LogicalParent, owner))
                    continue;
                Current = child;
                return true;
            }

            return false;
        }

        public void Dispose() => _projected?.Dispose();

        private static PooledList<ILogical>? Snapshot(ILogical owner,
            IReadOnlyList<ILogical>? owned, IReadOnlyList<ILogical>? projected)
        {
            if (projected is null || ReferenceEquals(owned, projected))
                return null;
            PooledList<ILogical>? result = null;
            try
            {
                // Weak projections can shrink during a child's callback or GC.
                // Hold their live children only until this traversal finishes.
                foreach (var child in projected)
                {
                    if (ReferenceEquals(child, owner) || !ReferenceEquals(child.LogicalParent, owner) ||
                        Contains(owned, owned?.Count ?? 0, child) || Contains(result, result?.Count ?? 0, child))
                        continue;
                    (result ??= new PooledList<ILogical>()).Add(child);
                }
                return result;
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }

        private static bool Contains(IReadOnlyList<ILogical>? children, int count, ILogical child)
        {
            for (var i = 0; i < count; ++i)
                if (ReferenceEquals(children![i], child))
                    return true;
            return false;
        }
    }
}
