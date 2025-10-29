namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Defines lifecycle hooks for hot reload operations against <see cref="IHotReloadableView"/> instances.
/// </summary>
public interface IHotReloadViewHandler
{
    /// <summary>
    /// Invoked before a view instance transfers its state to a replacement.
    /// </summary>
    /// <param name="view">The view that is about to be replaced.</param>
    void OnBeforeReload(IHotReloadableView view);

    /// <summary>
    /// Invoked after a view has completed its reload and state transfer.
    /// </summary>
    /// <param name="view">The view that has been reloaded.</param>
    void OnAfterReload(IHotReloadableView view);
}
