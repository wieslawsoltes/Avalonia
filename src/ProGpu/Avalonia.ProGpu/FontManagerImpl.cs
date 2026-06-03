#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Avalonia.Platform;
using ProGPU.Text;

namespace Avalonia.ProGpu
{
    internal class FontManagerImpl : IFontManagerImpl
    {
        private static readonly string[] FontDirectories = new[]
        {
            "/System/Library/Fonts/Supplemental",
            "/System/Library/Fonts",
            "/Library/Fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts")
        };

        private class CachedFont
        {
            public TtfFont Font { get; }
            public byte[] Data { get; }

            public CachedFont(TtfFont font, byte[] data)
            {
                Font = font;
                Data = data;
            }
        }

        private readonly Dictionary<string, CachedFont> _fontCache = new(StringComparer.OrdinalIgnoreCase);

        public string GetDefaultFontFamilyName()
        {
            return "Arial";
        }

        public string[] GetInstalledFontFamilyNames(bool checkForUpdates = false)
        {
            return new[] { "Arial", "Georgia", "Courier New", "Times New Roman" };
        }

        public bool TryMatchCharacter(
            int codepoint,
            FontStyle fontStyle,
            FontWeight fontWeight,
            FontStretch fontStretch,
            string? familyName,
            CultureInfo? culture,
            [NotNullWhen(returnValue: true)] out IPlatformTypeface? platformTypeface)
        {
            return TryCreateGlyphTypeface(familyName ?? GetDefaultFontFamilyName(), fontStyle, fontWeight, fontStretch, out platformTypeface);
        }

        public bool TryCreateGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
            FontStretch stretch, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        {
            platformTypeface = null;
            if (string.IsNullOrEmpty(familyName))
            {
                familyName = GetDefaultFontFamilyName();
            }

            // Map familyName to typical font filename
            string fontFileName = familyName.ToLowerInvariant() switch
            {
                "georgia" => "Georgia.ttf",
                "courier new" => "Courier New.ttf",
                "times new roman" => "Times New Roman.ttf",
                _ => "Arial.ttf"
            };

            // Attempt to load and cache the TtfFont
            if (!_fontCache.TryGetValue(fontFileName, out var cachedFont))
            {
                foreach (var dir in FontDirectories)
                {
                    string path = Path.Combine(dir, fontFileName);
                    if (File.Exists(path))
                    {
                        try
                        {
                            var data = File.ReadAllBytes(path);
                            var ttfFont = new TtfFont(data);
                            cachedFont = new CachedFont(ttfFont, data);
                            _fontCache[fontFileName] = cachedFont;
                            break;
                        }
                        catch
                        {
                            // Ignore and check next directory
                        }
                    }
                }
            }

            if (cachedFont == null)
            {
                // Fallback to searching any .ttf matching familyName in standard dirs
                foreach (var dir in FontDirectories)
                {
                    if (Directory.Exists(dir))
                    {
                        var files = Directory.GetFiles(dir, "*.ttf");
                        foreach (var file in files)
                        {
                            if (Path.GetFileNameWithoutExtension(file).Contains(familyName, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var data = File.ReadAllBytes(file);
                                    var ttfFont = new TtfFont(data);
                                    cachedFont = new CachedFont(ttfFont, data);
                                    _fontCache[fontFileName] = cachedFont;
                                    break;
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    if (cachedFont != null) break;
                }
            }

            // Absolute fallback to system Arial
            if (cachedFont == null)
            {
                string fallbackPath = "/System/Library/Fonts/Supplemental/Arial.ttf";
                if (File.Exists(fallbackPath))
                {
                    try
                    {
                        var data = File.ReadAllBytes(fallbackPath);
                        var ttfFont = new TtfFont(data);
                        cachedFont = new CachedFont(ttfFont, data);
                        _fontCache[fontFileName] = cachedFont;
                    }
                    catch
                    {
                    }
                }
            }

            if (cachedFont != null)
            {
                platformTypeface = new ProGpuTypeface(cachedFont.Font, cachedFont.Data, familyName, weight, style, stretch);
                return true;
            }

            return false;
        }

        public bool TryCreateGlyphTypeface(Stream stream, FontSimulations fontSimulations, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        {
            platformTypeface = null;
            try
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var data = ms.ToArray();
                var ttfFont = new TtfFont(data);
                platformTypeface = new ProGpuTypeface(ttfFont, data, "CustomFont", FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, fontSimulations);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetFamilyTypefaces(string familyName, [NotNullWhen(true)] out IReadOnlyList<Typeface>? familyTypefaces)
        {
            familyTypefaces = new[] { new Typeface(familyName) };
            return true;
        }
    }
}
