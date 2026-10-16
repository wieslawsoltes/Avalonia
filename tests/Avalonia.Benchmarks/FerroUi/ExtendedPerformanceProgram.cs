#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Harfbuzz;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.UnitTests;

namespace Avalonia.Benchmarks.FerroUi;

// Compiled unchanged against both revisions. These cases exercise remaining-design tradeoffs.
internal static class ExtendedPerformanceProgram
{
    public static int Main(string[] args)
    {
        var exit = PerformanceProgram.Main(args);
        if (exit != 0) return exit;
        try
        {
            var payload = JsonNode.Parse(File.ReadAllText(args[0]))!;
            var results = payload["results"]!.AsArray();
            var shaper = new HarfBuzzTextShaper();
            using var app = UnitTestApplication.Start(TestServices.StyledWindow.With(
                renderInterface: new PlatformRenderInterface(), textShaperImpl: shaper,
                fontManagerImpl: new FontManagerImpl()));

            var resources = new ResourceDictionary();
            var tail = resources;
            for (var depth = 0; depth < 10; ++depth)
            {
                var next = new ResourceDictionary();
                tail.MergedDictionaries.Add(next);
                for (var i = 0; i < 8; ++i)
                    tail.MergedDictionaries.Add(new ResourceDictionary { ["unrelated" + i] = i });
                tail = next;
            }
            tail["key"] = 1;
            Measure("resource-deep-hit", 10000, () =>
            {
                if (!resources.TryGetResource("key", null, out var value)) throw new InvalidOperationException("Missing resource");
                return (int)value!;
            });
            Measure("resource-deep-miss", 10000, () =>
                resources.TryGetResource("absent", null, out _) ? throw new InvalidOperationException("Unexpected resource") : 1);
            var version = 0;
            Measure("resource-deep-mutation", 1000, () =>
            {
                tail["key"] = ++version;
                if (!resources.TryGetResource("key", null, out var value) || (int)value! != version)
                    throw new InvalidOperationException("Stale resource cache");
                return version;
            });
            var local = new ResourceDictionary { ["key"] = 0 };
            var localVersion = 0;
            Measure("resource-local-replacement", 10000, () =>
            {
                local["key"] = ++localVersion;
                return (int)local["key"]!;
            });
            Measure("resource-deep-insert-remove", 1000, () =>
            {
                tail["temporary"] = 1;
                if (!resources.TryGetResource("temporary", null, out var value) || (int)value! != 1)
                    throw new InvalidOperationException("Inserted resource not found");
                if (!tail.Remove("temporary") || resources.TryGetResource("temporary", null, out _))
                    throw new InvalidOperationException("Removed resource still found");
                return 1;
            });

            var selector = default(Selector).Is<Control>().PropertyEquals(Control.TagProperty, "match").Class("active");
            var control = new Button { Tag = "match", Classes = { "active" } };
            Measure("selector-compound-live", 100000, () => selector.Match(control, subscribe: false).IsMatch ? 1 : 0);

            var font = Typeface.Default.GlyphTypeface;
            using var shaped = shaper.ShapeText("Recreated native glyph geometry".AsMemory(), new TextShaperOptions(font, 15));
            Measure("native-glyph-recreated", 6000, () =>
            {
                using var run = new GlyphRunImpl(font, 15, shaped, default);
                return (long)(run.GetTextBlob(default, default).Bounds.Width * 100);
            });
            var unique = Enumerable.Range(0, 4096).Select(i => new[]
            {
                new GlyphInfo(42, 0, 12 + i / 100.0), new GlyphInfo(43, 1, 12), new GlyphInfo(44, 2, 12)
            }).ToArray();
            var index = 0;
            Measure("native-glyph-unique", 4096, () =>
            {
                using var run = new GlyphRunImpl(font, 15, unique[index++ % unique.Length], default);
                return (long)(run.GetTextBlob(default, default).Bounds.Width * 100);
            });
            Measure("text-layout-wrap-fallback", 2000, () =>
            {
                using var layout = new TextLayout("A paragraph that wraps across several lines.", Typeface.Default, 15,
                    Brushes.Black, textWrapping: TextWrapping.Wrap, maxWidth: 90);
                return layout.TextLines.Count;
            });
            File.WriteAllText(args[0], payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return 0;

            void Measure(string name, int operations, Func<long> action)
            {
                for (var pass = 0; pass < 3; ++pass)
                    for (var i = 0; i < Math.Min(operations, 2048); ++i) action();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var allocation = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                long checksum = 0;
                for (var i = 0; i < operations; ++i) checksum += action();
                var elapsed = Stopwatch.GetTimestamp() - start;
                allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                results.Add(JsonSerializer.SerializeToNode(new
                {
                    Scenario = name, Operations = operations, Nanoseconds = elapsed * (1e9 / Stopwatch.Frequency),
                    AllocatedBytes = allocation, Checksum = checksum, PreparedRows = 0, ClearedRows = 0
                }));
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
