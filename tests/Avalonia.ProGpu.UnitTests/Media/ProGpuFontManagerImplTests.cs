using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using ProGPU.Text;
using Xunit;

namespace Avalonia.ProGpu.UnitTests.Media
{
    public class ProGpuFontManagerImplTests
    {
        [Fact]
        public void StreamTypefaceUsesSfntMetadata()
        {
            byte[] data = ReadFont("DejaVuSans.ttf");
            var expected = new TtfFont(data);
            var manager = new FontManagerImpl(() => Array.Empty<FontInfo>());

            using var stream = new MemoryStream(data);
            Assert.True(manager.TryCreateGlyphTypeface(stream, FontSimulations.None, out var platformTypeface));

            var typeface = Assert.IsType<ProGpuTypeface>(platformTypeface);
            Assert.Equal(expected.FamilyName, typeface.FamilyName);
            Assert.Equal((FontWeight)expected.WeightClass, typeface.Weight);
            Assert.Equal(expected.IsItalic ? FontStyle.Italic : FontStyle.Normal, typeface.Style);
            Assert.True(typeface.TryGetTable(OpenTypeTag.Parse("head"), out var head));
            Assert.NotEmpty(head.ToArray());
        }

        [Fact]
        public void MatchCharacterFallsBackByCoverageAndCachesResult()
        {
            byte[] digitsData = ReadFont("DF7segHMI.ttf");
            byte[] fallbackData = ReadFont("DejaVuSans.ttf");
            var digitsFont = new TtfFont(digitsData);
            var fallbackFont = new TtfFont(fallbackData);
            Assert.False(digitsFont.HasGlyph('Ω'));
            Assert.True(fallbackFont.HasGlyph('Ω'));

            string digitsPath = WriteTemporaryFont(digitsData, ".ttf");
            string fallbackPath = WriteTemporaryFont(fallbackData, ".ttf");
            try
            {
                var fonts = new List<FontInfo>
                {
                    CreateFontInfo(digitsFont, digitsPath),
                    CreateFontInfo(fallbackFont, fallbackPath)
                };
                var providerCalls = 0;
                var manager = new FontManagerImpl(() =>
                {
                    providerCalls++;
                    return fonts;
                });

                Assert.True(manager.TryMatchCharacter(
                    'Ω',
                    FontStyle.Normal,
                    FontWeight.Normal,
                    FontStretch.Normal,
                    digitsFont.FamilyName,
                    null,
                    out var first));
                Assert.True(manager.TryMatchCharacter(
                    'Ω',
                    FontStyle.Normal,
                    FontWeight.Normal,
                    FontStretch.Normal,
                    digitsFont.FamilyName,
                    null,
                    out var second));

                Assert.Same(first, second);
                Assert.Equal(fallbackFont.FamilyName, first.FamilyName);
                Assert.Equal(1, providerCalls);
            }
            finally
            {
                File.Delete(digitsPath);
                File.Delete(fallbackPath);
            }
        }

        [Fact]
        public void SystemFontCatalogCanBePreloadedOnTheThreadPool()
        {
            using var providerCalled = new ManualResetEventSlim();
            var manager = new FontManagerImpl(
                () =>
                {
                    providerCalled.Set();
                    return Array.Empty<FontInfo>();
                },
                preloadSystemFonts: true);

            Assert.True(providerCalled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Empty(manager.GetInstalledFontFamilyNames());
        }

        private static FontInfo CreateFontInfo(TtfFont font, string filePath)
        {
            return new FontInfo
            {
                Name = font.FullName,
                FamilyName = font.FamilyName,
                FilePath = filePath,
                FaceIndex = font.FaceIndex
            };
        }

        private static byte[] ReadFont(string fileName)
        {
            string resourceName = $"Avalonia.ProGpu.UnitTests.Fonts.{fileName}";
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing test font resource '{resourceName}'.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string WriteTemporaryFont(byte[] data, string extension)
        {
            string path = Path.Combine(Path.GetTempPath(), $"avalonia-progpu-font-{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(path, data);
            return path;
        }
    }
}
