namespace Avalonia.Media.TextFormatting;

public sealed partial class ShapedTextRun
{
    // Public GlyphRun is mutable. Only a fresh, unexposed run can consume shared metrics.
    internal bool HasGlyphRun => _glyphRun is not null;
}
