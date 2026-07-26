using Avalonia.Metadata;

namespace Avalonia.Platform;

/// <summary>
/// Describes native-window surface constraints imposed by a rendering backend.
/// </summary>
[Unstable, PrivateApi]
public interface IPlatformRenderInterfaceNativeSurfaceFeature
{
    /// <summary>
    /// Gets whether native window surfaces must use an opaque-capable visual.
    /// </summary>
    bool RequiresOpaqueSurface { get; }
}
