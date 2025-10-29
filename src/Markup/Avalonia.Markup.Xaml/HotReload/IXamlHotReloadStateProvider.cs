namespace Avalonia.Markup.Xaml.HotReload;

/// <summary>
/// Provides an opt-in extension point for controls that need to capture and restore custom state
/// across a hot reload populate cycle.
/// </summary>
public interface IXamlHotReloadStateProvider
{
    /// <summary>
    /// Captures state before hot reload repopulates the control. Implementations can return any value,
    /// including complex graphs or lightweight tokens.
    /// </summary>
    /// <returns>An opaque state object to be supplied back to <see cref="RestoreHotReloadState"/>.</returns>
    object? CaptureHotReloadState();

    /// <summary>
    /// Restores state after hot reload has repopulated the control.
    /// </summary>
    /// <param name="state">The state object returned from <see cref="CaptureHotReloadState"/>.</param>
    void RestoreHotReloadState(object? state);
}
