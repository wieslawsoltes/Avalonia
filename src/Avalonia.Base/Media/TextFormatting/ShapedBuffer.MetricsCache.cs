namespace Avalonia.Media.TextFormatting;

public sealed partial class ShapedBuffer
{
    private DefaultTextLineMetricsCache? _sharedMetrics;
    private int _sharedMetricsGeneration;

    internal DefaultTextLineMetricsCache? SharedMetrics =>
        !_disposed && _glyphRef is not null &&
        _glyphRef.Item.Generation == _sharedMetricsGeneration ? _sharedMetrics : null;

    internal void AttachMetricsCache(DefaultTextLineMetricsCache cache)
    {
        _sharedMetrics = cache;
        _sharedMetricsGeneration = _glyphRef?.Item.Generation ?? -1;
    }
}
