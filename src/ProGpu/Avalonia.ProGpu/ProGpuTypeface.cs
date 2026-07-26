using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using ProGPU.Text;

namespace Avalonia.ProGpu
{
    internal class ProGpuTypeface : IPlatformTypeface
    {
        public TtfFont Font { get; }
        private readonly byte[] _fontData;
        internal TtfShapingFontFace ShapingFace { get; }
        public FontSimulations FontSimulations { get; }
        public string FamilyName { get; }
        public FontWeight Weight { get; }
        public FontStyle Style { get; }
        public FontStretch Stretch { get; }

        public ProGpuTypeface(TtfFont font, byte[] fontData, string familyName, FontWeight weight, FontStyle style, FontStretch stretch, FontSimulations fontSimulations = FontSimulations.None)
        {
            Font = font ?? throw new ArgumentNullException(nameof(font));
            ShapingFace = new TtfShapingFontFace(Font);
            _fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            FamilyName = familyName;
            Weight = weight;
            Style = style;
            Stretch = stretch;
            FontSimulations = fontSimulations;
        }

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        {
            var value = (uint)tag;
            var tableTag = new string(new[]
            {
                (char)((value >> 24) & 0xFF),
                (char)((value >> 16) & 0xFF),
                (char)((value >> 8) & 0xFF),
                (char)(value & 0xFF)
            });
            if (Font.TryGetTable(tableTag, out table))
            {
                return true;
            }

            var reversedTag = new string(new[]
            {
                tableTag[3],
                tableTag[2],
                tableTag[1],
                tableTag[0]
            });
            return Font.TryGetTable(reversedTag, out table);
        }

        public bool TryGetStream([NotNullWhen(true)] out Stream? stream)
        {
            try
            {
                stream = new MemoryStream(_fontData);
                return true;
            }
            catch
            {
                stream = null;
                return false;
            }
        }

        public void Dispose()
        {
        }
    }
}
