using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;
using SkiaSharp;
using ProGPU.Text;
using Avalonia.ProGpu;

namespace Avalonia.ProGpu.UnitTests.Media
{
    public class CustomFontManagerImpl : IFontManagerImpl, IDisposable
    {
        private readonly string _defaultFamilyName;
        private readonly string[] _bcp47 = { CultureInfo.CurrentCulture.ThreeLetterISOLanguageName, CultureInfo.CurrentCulture.TwoLetterISOLanguageName };
        private IFontCollection? _systemFonts;

        public CustomFontManagerImpl()
        {
            _defaultFamilyName = FontManager.SystemFontsKey + "#Noto Mono";
        }

        public IFontCollection SystemFonts
        {
            get
            {
                if (_systemFonts is null)
                {
                    var source = new Uri("resm:Avalonia.ProGpu.UnitTests.Assets?assembly=Avalonia.ProGpu.UnitTests");

                    _systemFonts = new EmbeddedFontCollection(FontManager.SystemFontsKey, source);
                }

                return _systemFonts;
            }
        }
        public string GetDefaultFontFamilyName()
        {
            return _defaultFamilyName;
        }

        public string[] GetInstalledFontFamilyNames(bool checkForUpdates = false)
        {
            try
            {
                var key = new Uri("resm:Avalonia.ProGpu.UnitTests.Assets?assembly=Avalonia.ProGpu.UnitTests");

                var assetLoader = AvaloniaLocator.Current.GetRequiredService<IAssetLoader>();

                var fontAssets = FontFamilyLoader.LoadFontAssets(key);
                var names = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                foreach (var fontAsset in fontAssets)
                {
                    try
                    {
                        using var stream = assetLoader.Open(fontAsset);
                        using var sk = SKTypeface.FromStream(stream);

                        if (sk != null && !string.IsNullOrEmpty(sk.FamilyName))
                        {
                            names.Add(sk.FamilyName);
                        }
                    }
                    catch
                    {
                        // Ignore faulty assets
                    }
                }

                return names.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IPlatformTypeface? CreatePlatformTypeface(SKTypeface skTypeface, FontSimulations fontSimulations, FontStyle fontStyle, FontWeight fontWeight, FontStretch fontStretch)
        {
            if (skTypeface == null) return null;
            try
            {
                using (var asset = skTypeface.OpenStream())
                {
                    if (asset == null) return null;
                    var size = asset.Length;
                    var buffer = new byte[size];
                    asset.Read(buffer, (int)size);
                    var ttfFont = new TtfFont(buffer);
                    
                    var actualWeight = (FontWeight)skTypeface.FontStyle.Weight;
                    var actualStyle = ConvertSlant(skTypeface.FontStyle.Slant);
                    var actualStretch = (FontStretch)skTypeface.FontStyle.Width;

                    return new ProGpuTypeface(ttfFont, buffer, skTypeface.FamilyName, actualWeight, actualStyle, actualStretch, fontSimulations);
                }
            }
            catch
            {
                return null;
            }
        }

        private static FontStyle ConvertSlant(SKFontStyleSlant slant)
        {
            return slant switch
            {
                SKFontStyleSlant.Italic => FontStyle.Italic,
                SKFontStyleSlant.Oblique => FontStyle.Oblique,
                _ => FontStyle.Normal
            };
        }

        public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight, FontStretch fontStretch,
            string? familyName, CultureInfo? culture, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        {
            if (SystemFonts.TryMatchCharacter(codepoint, fontStyle, fontWeight, fontStretch, familyName, culture, out var glyphTypeface))
            {
                platformTypeface = glyphTypeface.GlyphTypeface.PlatformTypeface;

                return true;
            }

            var fallback = SKFontManager.Default.MatchCharacter(familyName, (SKFontStyleWeight)fontWeight,
                (SKFontStyleWidth)fontStretch, (SKFontStyleSlant)fontStyle, _bcp47, codepoint);

            if (fallback != null)
            {
                var pt = CreatePlatformTypeface(fallback, FontSimulations.None, fontStyle, fontWeight, fontStretch);
                if (pt != null)
                {
                    platformTypeface = pt;
                    return true;
                }
            }

            platformTypeface = null;
            return false;
        }

        public bool TryCreateGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
            FontStretch stretch, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        {
            if (SystemFonts.TryGetGlyphTypeface(familyName, style, weight, stretch, out var glyphTypeface))
            {
                platformTypeface = glyphTypeface.PlatformTypeface;

                return true;
            }

            var fontStyle = new SKFontStyle((SKFontStyleWeight)weight, (SKFontStyleWidth)stretch, (SKFontStyleSlant)style);
            var skTypeface = SKFontManager.Default.MatchFamily(familyName, fontStyle);

            if (skTypeface != null)
            {
                var pt = CreatePlatformTypeface(skTypeface, FontSimulations.None, style, weight, stretch);
                if (pt != null)
                {
                    platformTypeface = pt;
                    return true;
                }
            }

            platformTypeface = null;
            return false;
        }

        public bool TryCreateGlyphTypeface(Stream stream, FontSimulations fontSimulations, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        {
            try
            {
                var skTypeface = SKTypeface.FromStream(stream);
                if (skTypeface != null)
                {
                    var pt = CreatePlatformTypeface(skTypeface, fontSimulations, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
                    if (pt != null)
                    {
                        platformTypeface = pt;
                        return true;
                    }
                }
            }
            catch
            {
            }

            platformTypeface = null;
            return false;
        }

        public void Dispose()
        {
            _systemFonts?.Dispose();
        }

        public bool TryGetFamilyTypefaces(string familyName, [NotNullWhen(true)] out IReadOnlyList<Typeface>? familyTypefaces)
        {
            if (SystemFonts.TryGetFamilyTypefaces(familyName, out familyTypefaces))
            {
                return true;
            }

            var set = SKFontManager.Default.GetFontStyles(familyName);

            if (set.Count == 0)
            {
                return false;
            }

            var typefaces = new List<Typeface>(set.Count);

            foreach (var fontStyle in set)
            {
                typefaces.Add(new Typeface(familyName, ConvertSlant(fontStyle.Slant), (FontWeight)fontStyle.Weight, (FontStretch)fontStyle.Width));
            }

            familyTypefaces = typefaces;

            return true;
        }
    }
}
