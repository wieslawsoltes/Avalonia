namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Optional hook that allows controls to run custom logic after a hot reload populate cycle.
/// </summary>
public interface IXamlHotReloadable
{
    /// <summary>
    /// Called after the control has been repopulated with hot reload content.
    /// </summary>
    void OnHotReload();
}
