using Avalonia.Media;

namespace Avalonia.ProGpu;

internal sealed class ProGpuTileBrushCalculator
{
    public ProGpuTileBrushCalculator(ITileBrush brush, Size contentSize, Size targetSize)
    {
        SourceRect = brush.SourceRect.ToPixels(contentSize);
        DestinationRect = brush.DestinationRect.ToPixels(targetSize);

        var scale = brush.Stretch.CalculateScaling(DestinationRect.Size, SourceRect.Size);
        var translate = CalculateTranslate(
            brush.AlignmentX,
            brush.AlignmentY,
            SourceRect.Size * scale,
            DestinationRect.Size);

        IntermediateTransform = Matrix.CreateTranslation(-SourceRect.Position) *
                                Matrix.CreateScale(scale) *
                                Matrix.CreateTranslation(translate);

        if (brush.TileMode == TileMode.None)
        {
            IntermediateClip = DestinationRect;
            IntermediateTransform *= Matrix.CreateTranslation(DestinationRect.Position);
        }
        else
        {
            IntermediateClip = new Rect(DestinationRect.Size);
        }
    }

    public Rect DestinationRect { get; }
    public Rect IntermediateClip { get; }
    public Matrix IntermediateTransform { get; }
    public Rect SourceRect { get; }

    private static Vector CalculateTranslate(
        AlignmentX alignmentX,
        AlignmentY alignmentY,
        Size sourceSize,
        Size destinationSize)
    {
        var x = alignmentX switch
        {
            AlignmentX.Center => (destinationSize.Width - sourceSize.Width) / 2,
            AlignmentX.Right => destinationSize.Width - sourceSize.Width,
            _ => 0
        };
        var y = alignmentY switch
        {
            AlignmentY.Center => (destinationSize.Height - sourceSize.Height) / 2,
            AlignmentY.Bottom => destinationSize.Height - sourceSize.Height,
            _ => 0
        };

        return new Vector(x, y);
    }
}
