#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Markup.Xaml.HotReload;
using Avalonia.Threading;

namespace Avalonia.Controls;

public partial class TreeView : IXamlHotReloadStateProvider
{
    private sealed record HotReloadSnapshot(object?[] SelectedItems, List<int[]> ExpandedPaths);

    /// <inheritdoc />
    public object? CaptureHotReloadState()
    {
        var selectedItems = SelectedItems.Count == 0
            ? Array.Empty<object?>()
            : SelectedItems.Cast<object?>().ToArray();

        var expandedPaths = new List<int[]>();
        foreach (var container in GetRealizedTreeContainers())
        {
            if (container is TreeViewItem treeViewItem && treeViewItem.IsExpanded &&
                TryCreateIndexPath(treeViewItem, out var path))
            {
                expandedPaths.Add(path);
            }
        }

        return new HotReloadSnapshot(selectedItems, expandedPaths);
    }

    /// <inheritdoc />
    public void RestoreHotReloadState(object? state)
    {
        if (state is not HotReloadSnapshot snapshot)
            return;

        if (snapshot.SelectedItems.Length > 0)
        {
            var desired = new List<object>(snapshot.SelectedItems.Length);
            foreach (var item in snapshot.SelectedItems)
            {
                if (item is not null)
                    desired.Add(item);
            }

            try
            {
                SynchronizeItems(SelectedItems, desired);
            }
            catch
            {
                // SelectedItems may reject values that no longer exist; best effort only.
            }
        }
        else
        {
            try
            {
                SelectedItems.Clear();
            }
            catch
            {
                // Ignore failures clearing read-only selections.
            }
        }

        if (snapshot.ExpandedPaths.Count > 0)
            RestoreExpandedPaths(snapshot.ExpandedPaths, attempt: 0);
    }

    private void RestoreExpandedPaths(IReadOnlyList<int[]> paths, int attempt)
    {
        const int MaxAttempts = 4;
        if (paths.Count == 0)
            return;

        var pending = new List<int[]>();

        foreach (var path in paths)
        {
            if (!TryExpandPath(path))
                pending.Add(path);
        }

        if (pending.Count > 0 && attempt < MaxAttempts)
        {
            Dispatcher.UIThread.Post(
                () => RestoreExpandedPaths(pending, attempt + 1),
                DispatcherPriority.Background);
        }
    }

    private bool TryExpandPath(int[] path)
    {
        ItemsControl current = this;

        for (var depth = 0; depth < path.Length; depth++)
        {
            var index = path[depth];
            if (current.ContainerFromIndex(index) is not TreeViewItem container)
                return false;

            if (!container.IsExpanded)
                container.IsExpanded = true;

            current = container;
        }

        return true;
    }

    private bool TryCreateIndexPath(TreeViewItem container, out int[] path)
    {
        var indices = new Stack<int>();
        Control current = container;

        while (true)
        {
            var parent = ItemsControl.ItemsControlFromItemContainer(current);
            if (parent is null)
            {
                path = Array.Empty<int>();
                return false;
            }

            var index = parent.IndexFromContainer(current);
            if (index < 0)
            {
                path = Array.Empty<int>();
                return false;
            }

            indices.Push(index);

            if (ReferenceEquals(parent, this))
            {
                path = indices.ToArray();
                return true;
            }

            current = parent;
        }
    }
}
#endif
