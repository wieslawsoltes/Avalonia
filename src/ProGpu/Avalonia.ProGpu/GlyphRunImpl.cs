using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal class GlyphRunImpl : IGlyphRunImpl
    {
        public ProGpuTypeface Typeface { get; }
        public double FontRenderingEmSize { get; }
        public Point BaselineOrigin { get; }
        public Rect Bounds { get; }

        private readonly ushort[] _glyphIndices;
        private readonly Point[] _glyphPositions;
        private readonly Vector2[] _proGpuGlyphPositions;

        public ushort[] GlyphIndices => _glyphIndices;
        public Point[] GlyphPositions => _glyphPositions;
        public Vector2[] ProGpuGlyphPositions => _proGpuGlyphPositions;

        public GlyphRunImpl(GlyphTypeface glyphTypeface, double fontRenderingEmSize,
            IReadOnlyList<GlyphInfo> glyphInfos, Point baselineOrigin)
        {
            if (glyphTypeface == null)
            {
                throw new ArgumentNullException(nameof(glyphTypeface));
            }

            if (glyphInfos == null)
            {
                throw new ArgumentNullException(nameof(glyphInfos));
            }

            Typeface = (ProGpuTypeface)glyphTypeface.PlatformTypeface;
            FontRenderingEmSize = fontRenderingEmSize;

            var count = glyphInfos.Count;
            _glyphIndices = new ushort[count];
            _glyphPositions = new Point[count];
            _proGpuGlyphPositions = new Vector2[count];

            var currentX = 0.0;

            for (int i = 0; i < count; i++)
            {
                var glyphInfo = glyphInfos[i];
                var offset = glyphInfo.GlyphOffset;

                _glyphIndices[i] = glyphInfo.GlyphIndex;
                _glyphPositions[i] = new Point(currentX + offset.X, offset.Y);
                _proGpuGlyphPositions[i] = new Vector2(
                    (float)(currentX + offset.X),
                    (float)offset.Y);

                currentX += glyphInfo.GlyphAdvance;
            }

            var runBounds = new Rect();
            double scale = fontRenderingEmSize / Typeface.Font.UnitsPerEm;
            currentX = 0.0;

            for (var i = 0; i < count; i++)
            {
                var glyphIndex = _glyphIndices[i];
                var advance = glyphInfos[i].GlyphAdvance;
                var offset = glyphInfos[i].GlyphOffset;

                if (BitmapGlyphCache.TryGetMetrics(
                        Typeface.Font,
                        glyphIndex,
                        fontRenderingEmSize,
                        out var bitmapMetrics))
                {
                    var bitmapBounds = bitmapMetrics.GetBounds(
                        new Point(currentX + offset.X, offset.Y),
                        fontRenderingEmSize);
                    runBounds = runBounds.Union(bitmapBounds);
                }
                else
                {
                    var outline = Typeface.Font.GetGlyphOutline(glyphIndex);
                    if (outline != null)
                    {
                        var gBounds = GeometryImpl.CalculateBounds(outline);
                        if (gBounds != new Rect())
                        {
                            var scaledBounds = new Rect(
                                currentX + offset.X + gBounds.Left * scale,
                                offset.Y + gBounds.Top * scale,
                                gBounds.Width * scale,
                                gBounds.Height * scale
                            );
                            runBounds = runBounds.Union(scaledBounds);
                        }
                    }
                }
                currentX += advance;
            }

            BaselineOrigin = baselineOrigin;
            Bounds = runBounds.Translate(new Vector(baselineOrigin.X, baselineOrigin.Y));
        }

        public void Dispose()
        {
        }

        public IReadOnlyList<float> GetIntersections(float lowerLimit, float upperLimit)
        {
            return Array.Empty<float>();
        }
    }
}
