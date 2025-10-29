using Avalonia.Controls;

namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Represents a visual element that supports runtime hot reload.
/// </summary>
public interface IHotReloadableView
{
    /// <summary>
    /// Gets or sets the handler responsible for coordinating reload lifecycle callbacks.
    /// </summary>
    IHotReloadViewHandler? ReloadHandler { get; set; }

    /// <summary>
    /// Transfers the mutable state from the current view to the provided replacement view.
    /// </summary>
    /// <param name="newView">The view that will replace the current instance.</param>
    void TransferState(Control newView);

    /// <summary>
    /// Re-applies bindings and layout once the view has been reloaded.
    /// </summary>
    void Reload();
}
