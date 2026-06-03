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
        public FontSimulations FontSimulations { get; }
        public string FamilyName { get; }
        public FontWeight Weight { get; }
        public FontStyle Style { get; }
        public FontStretch Stretch { get; }

        public ProGpuTypeface(TtfFont font, byte[] fontData, string familyName, FontWeight weight, FontStyle style, FontStretch stretch, FontSimulations fontSimulations = FontSimulations.None)
        {
            Font = font ?? throw new ArgumentNullException(nameof(font));
            _fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            FamilyName = familyName;
            Weight = weight;
            Style = style;
            Stretch = stretch;
            FontSimulations = fontSimulations;
        }

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        {
            table = default;
            try
            {
                uint tagVal = (uint)tag;
                byte b1 = (byte)((tagVal >> 24) & 0xFF);
                byte b2 = (byte)((tagVal >> 16) & 0xFF);
                byte b3 = (byte)((tagVal >> 8) & 0xFF);
                byte b4 = (byte)(tagVal & 0xFF);
                string searchTag = new string(new[] { (char)b1, (char)b2, (char)b3, (char)b4 });
                string searchTagRev = new string(new[] { (char)b4, (char)b3, (char)b2, (char)b1 });

                if (_fontData.Length < 12) return false;
                ushort numTables = (ushort)((_fontData[4] << 8) | _fontData[5]);
                
                for (int i = 0; i < numTables; i++)
                {
                    int entryOffset = 12 + i * 16;
                    if (entryOffset + 16 > _fontData.Length) break;
                    
                    char c1 = (char)_fontData[entryOffset];
                    char c2 = (char)_fontData[entryOffset + 1];
                    char c3 = (char)_fontData[entryOffset + 2];
                    char c4 = (char)_fontData[entryOffset + 3];
                    string entryTag = new string(new[] { c1, c2, c3, c4 });
                    
                    if (entryTag == searchTag || entryTag == searchTagRev)
                    {
                        uint offset = (uint)((_fontData[entryOffset + 8] << 24) |
                                             (_fontData[entryOffset + 9] << 16) |
                                             (_fontData[entryOffset + 10] << 8) |
                                             _fontData[entryOffset + 11]);
                        uint length = (uint)((_fontData[entryOffset + 12] << 24) |
                                             (_fontData[entryOffset + 13] << 16) |
                                             (_fontData[entryOffset + 14] << 8) |
                                             _fontData[entryOffset + 15]);
                        
                        if (offset + length <= _fontData.Length)
                        {
                            table = new ReadOnlyMemory<byte>(_fontData, (int)offset, (int)length);
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
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
