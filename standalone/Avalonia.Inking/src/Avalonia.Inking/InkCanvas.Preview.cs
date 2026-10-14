using Avalonia.Inking.Core;
namespace Avalonia.Inking;
public partial class InkCanvas
{
    /// <summary>Immutable live ink snapshot for custom presenters. May be null outside collection.</summary>
    public InkStroke? CurrentStrokePreview => LiveStroke;
    public bool IsTransformPreviewActive => HasTransformPreview;
    public InkTransform SelectionPreviewTransform => PreviewTransform;
    public InkRect SelectionRectanglePreview => BoxPreview;
    /// <summary>Copied lasso coordinates; callers cannot mutate the active input gesture.</summary>
    public IReadOnlyList<InkVector> GetLassoPreview() => LassoPreview.ToArray();
    public Point? EraserPreviewPosition => EraserCursor;
}
