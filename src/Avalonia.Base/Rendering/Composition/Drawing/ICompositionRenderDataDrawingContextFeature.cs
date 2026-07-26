namespace Avalonia.Rendering.Composition.Drawing;

/// <summary>
/// Optional typed bridge for platform drawing contexts that can retain and
/// replay an immutable server composition draw list without invoking every
/// render-data node on each frame.
/// </summary>
internal interface ICompositionRenderDataDrawingContextFeature
{
    bool TryRender(ServerCompositionRenderData renderData);
}
