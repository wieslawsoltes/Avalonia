using System.Collections.Generic;

namespace Avalonia.LogicalTree;

// Public compatibility projections must not disconnect the framework-owned
// template tree from attachment, inherited values, resources or styles.
internal readonly struct LogicalChildrenTraversal(
    ILogical owner, IReadOnlyList<ILogical>? owned, IReadOnlyList<ILogical>? projected)
{
    public Enumerator GetEnumerator() => new(owner, owned, projected);

    internal struct Enumerator(ILogical owner, IReadOnlyList<ILogical>? owned, IReadOnlyList<ILogical>? projected)
    {
        private readonly int _ownedCount = owned?.Count ?? 0;
        private readonly int _projectedCount = ReferenceEquals(owned, projected) ? 0 : projected?.Count ?? 0;
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

            while (_projectedIndex < _projectedCount)
            {
                var index = _projectedIndex++;
                var child = projected![index];
                if (ReferenceEquals(child, owner) || !ReferenceEquals(child.LogicalParent, owner) ||
                    Contains(owned, _ownedCount, child) || Contains(projected, index, child))
                    continue;
                Current = child;
                return true;
            }

            return false;
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
