#if !NETSTANDARD2_0
using Avalonia.Markup.Xaml.HotReload;

namespace Avalonia.Controls;

public partial class TabControl : IXamlHotReloadStateProvider
{
    private sealed record HotReloadSnapshot(int SelectedIndex, object? SelectedItem);

    /// <inheritdoc />
    public object? CaptureHotReloadState()
    {
        return new HotReloadSnapshot(SelectedIndex, SelectedItem);
    }

    /// <inheritdoc />
    public void RestoreHotReloadState(object? state)
    {
        if (state is not HotReloadSnapshot snapshot)
            return;

        if (snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < Items.Count)
        {
            SelectedIndex = snapshot.SelectedIndex;
        }
        else if (snapshot.SelectedItem is not null && Items.Contains(snapshot.SelectedItem))
        {
            SelectedItem = snapshot.SelectedItem;
        }
    }
}
#endif
