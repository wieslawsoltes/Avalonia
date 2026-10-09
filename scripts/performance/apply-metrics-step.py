#!/usr/bin/env python3
"""One-shot, exact-source integration for the remaining metrics work; removed by its commit."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]

def edit(path, expected, replacements):
    file = ROOT / path
    sha = subprocess.check_output(['git', 'hash-object', str(file)], text=True).strip()
    if sha != expected:
        raise RuntimeError(f'Source changed: {path}: {sha} != {expected}')
    content = file.read_text(encoding='utf-8')
    for old, new in replacements:
        if content.count(old) != 1:
            raise RuntimeError(f'Expected one integration point in {path}: {old[:100]}')
        content = content.replace(old, new)
    file.write_text(content, encoding='utf-8')

edit('src/Avalonia.Base/Media/TextFormatting/ShapedBuffer.cs', '8e17bc6d7ccf2e1ebd1d58b9f0a291ac1111be63', [
    ('public sealed class ShapedBuffer :', 'public sealed partial class ShapedBuffer :')])
edit('src/Avalonia.Base/Media/TextFormatting/ShapedTextRun.cs', 'e8f9e377000a383be53e7a9722718fe0297a74fc', [
    ('public sealed class ShapedTextRun :', 'public sealed partial class ShapedTextRun :')])
edit('src/Avalonia.Base/Media/TextFormatting/TextLineImpl.cs', '29fc730e64af556a88a9379c7e5ff48f51e67f04', [
    ('internal class TextLineImpl :', 'internal partial class TextLineImpl :'),
    ('_textLineMetrics = CreateLineMetrics();\n\n            if (_textLineBreak is null',
     '_textLineMetrics = GetOrCreateLineMetrics();\n\n            if (_textLineBreak is null')])
edit('src/Avalonia.Base/Avalonia.Base.csproj', '45f792bb967faf4a9121be7895fa87460b3f2b4c', [
    ('<InternalsVisibleTo Include="Avalonia.Skia, PublicKey=$(AvaloniaPublicKey)" />',
     '<InternalsVisibleTo Include="Avalonia.Skia, PublicKey=$(AvaloniaPublicKey)" />\n    <InternalsVisibleTo Include="Avalonia.HarfBuzz, PublicKey=$(AvaloniaPublicKey)" />')])
edit('src/Skia/Avalonia.Skia/PlatformRenderInterface.cs', '0376f1844b093603783c9ca91c4c610a47778b16', [
    ('internal class PlatformRenderInterface : IPlatformRenderInterface',
     'internal class PlatformRenderInterface : IPlatformRenderInterface, IDefaultLineMetricsCacheBackend')])
edit('tests/Avalonia.Skia.UnitTests/Avalonia.Skia.UnitTests.csproj', '80731a076fd71a72fff79c68e4c4bf60ae714991', [
    ('<ProjectReference Include="..\\..\\src\\Avalonia.Base\\Avalonia.Base.csproj" />',
     '<ProjectReference Include="..\\..\\src\\Avalonia.Base\\Avalonia.Base.csproj" />\n    <ProjectReference Include="..\\..\\src\\HarfBuzz\\Avalonia.HarfBuzz\\Avalonia.HarfBuzz.csproj" />')])

path = ROOT / 'src/HarfBuzz/Avalonia.HarfBuzz/HarfBuzzTextShaper.cs'
source = path.read_text(encoding='utf-8')
old = 'var cacheable = ShapedRunCache.TryCreateKey(text, options, usedCulture, harfBuzzTypeface.CacheId, out cacheKey);'
assert source.count(old) == 1
source = source.replace(old, '''var probe = _shapedRunCache.ShouldProbe();
            var cacheable = probe && ShapedRunCache.TryCreateKey(text, options, usedCulture, harfBuzzTypeface.CacheId, out cacheKey);
#if AVALONIA_PERF_COUNTERS
            Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeRequests);
            if (!probe)
                Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.ShapeProbeSkips);
#endif''')
path.write_text(source, encoding='utf-8')

path = ROOT / 'src/Skia/Avalonia.Skia/SharedGlyphRunData.cs'
source = path.read_text(encoding='utf-8')
for old, new in [
    ('private SKTextBlob CreateTextBlob(TextOptions options)\n    {', '''private SKTextBlob CreateTextBlob(TextOptions options)
    {
#if AVALONIA_PERF_COUNTERS
        Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeTextBlobsCreated);
#endif'''),
    ('if (face.IsDisposed)\n            throw new ObjectDisposedException(nameof(GlyphTypeface));', '''#if AVALONIA_PERF_COUNTERS
        Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeGlyphRequests);
#endif
        if (face.IsDisposed)
            throw new ObjectDisposedException(nameof(GlyphTypeface));'''),
    ('found.Value.Data.AddReference();\n                    return found.Value.Data;', '''found.Value.Data.AddReference();
#if AVALONIA_PERF_COUNTERS
                    Avalonia.Diagnostics.PerformanceCounters.Increment(Avalonia.Diagnostics.PerformanceCounter.NativeGlyphHits);
#endif
                    return found.Value.Data;''')]:
    assert source.count(old) == 1, old
    source = source.replace(old, new)
path.write_text(source, encoding='utf-8')

path = ROOT / 'src/Avalonia.Base/Diagnostics/PerformanceCounters.cs'
source = path.read_text(encoding='utf-8')
assert source.count('    ValidationProbe,') == 1
source = source.replace('    ValidationProbe,', '    TextLinesFinalized,\n    DefaultLineMetricHits,\n    ValidationProbe,')
path.write_text(source, encoding='utf-8')

for pending in ROOT.glob('**/*.ferroui-pending'):
    destination = pending.with_suffix('')
    if destination.exists() and destination.name != 'ShapedRunCache.cs':
        raise RuntimeError(f'Unexpected existing target: {destination}')
    pending.replace(destination)

path = ROOT / 'samples/FerroUi.Browser.Performance/Program.cs'
source = path.read_text(encoding='utf-8')
old = 'text.Bind(TextBlock.TextProperty, new ReflectionBinding(path));'
assert source.count(old) == 1
source = source.replace(old, old + '\n                        text.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FerroUiAccent"));')
path.write_text(source, encoding='utf-8')

(ROOT / '.github/workflows/ferroui-integrate-metrics.yml').unlink()
Path(__file__).unlink()
