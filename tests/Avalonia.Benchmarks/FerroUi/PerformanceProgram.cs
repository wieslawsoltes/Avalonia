#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Harfbuzz;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.UnitTests;

namespace Avalonia.Benchmarks.FerroUi;

// The same file is compiled against both revisions by scripts/performance/compare-native.py.
// It intentionally references no optimization-specific APIs or counters.
internal static class PerformanceProgram
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Specify an output JSON path.");
            return 2;
        }
        try
        {
            var shaper = new HarfBuzzTextShaper();
            using var app = UnitTestApplication.Start(TestServices.StyledWindow.With(
                renderInterface: new PlatformRenderInterface(), textShaperImpl: shaper,
                fontManagerImpl: new FontManagerImpl()));
            var results = new List<Result>();
            var node = new Node();
            var toggle = 0;
            Measure("property-no-listener", 100000, () => { node.Number = ++toggle & 1; return node.Number; });
            ((INotifyPropertyChanged)node).PropertyChanged += static (_, _) => { };
            Measure("property-inpc", 100000, () => { node.Number = ++toggle & 1; return node.Number; });

            var parentA = new Node { Number = 1 };
            var parentB = new Node { Number = 2 };
            var child = new Node { InheritanceOwner = parentA };
            Measure("inheritance-reparent", 30000, () =>
            {
                var parent = (++toggle & 1) == 0 ? parentA : parentB;
                child.InheritanceOwner = parent;
                child.GetValueStore().SetInheritanceParent(parent);
                return child.Number;
            });
            var templateTarget = new Node { TemplatedParent = node };
            using var binding = templateTarget.Bind(Node.NumberProperty, new TemplateBinding(Node.NumberProperty));
            Measure("template-identity", 60000, () => { node.Number = ++toggle & 1; return templateTarget.Number; });

            var reflectionSource = new Control { Tag = new Row(23) };
            var reflectionTarget = new TextBlock();
            var description = new ReflectionBinding("Tag.Name") { Source = reflectionSource };
            Measure("reflection-description-reused", 10000, () => BindDescription(description));
            Measure("reflection-description-once", 10000, () =>
                BindDescription(new ReflectionBinding("Tag.Name") { Source = reflectionSource }));

            var style = new Style(x => x.Is<Button>()) { Setters = { new Setter(Control.TagProperty, "hit") } };
            var nonMatch = new TextBlock();
            Measure("style-type-miss", 100000, () => { StyleHelpers.TryAttach(style, nonMatch); return nonMatch.Tag is null ? 1 : 0; });

            var font = Typeface.Default.GlyphTypeface;
            var options = new TextShaperOptions(font, 14);
            var repeated = Enumerable.Range(0, 64).Select(i => $"Row {i}: Ready").ToArray();
            var unique = Enumerable.Range(0, 4096).Select(i => $"Unique row {i}: never reused inside the cache window").ToArray();
            var index = 0;
            Measure("shape-repeated-short", 12000, () => Shape(repeated[index++ % repeated.Length]));
            index = 0;
            Measure("shape-unique-short", 8192, () => Shape(unique[index++ % unique.Length]));

            // Warm-up consumes 6,144 values, measurement 8,192: none of these 16,384 strings
            // repeats. String creation is outside the measured shaping operation.
            var onePass = Enumerable.Range(0, 16384).Select(i => $"One pass {i}: a previously unseen label").ToArray();
            var onePassIndex = 0;
            Measure("shape-one-pass-short", 8192, () => Shape(onePass[onePassIndex++]));
            var mixed = Enumerable.Range(0, 16384).Select(i => $"Mixed cold label {i}").ToArray();
            var mixedIndex = 0;
            Measure("shape-hot-with-one-off-scan", 8192, () =>
            {
                var i = mixedIndex++;
                return Shape((i & 3) == 0 ? mixed[i] : repeated[(i / 4) % repeated.Length]);
            });
            var longText = new string('x', 512);
            Measure("shape-long-uncacheable", 2048, () => Shape(longText));

            var context = "before office after".AsMemory(7, 6);
            Measure("shape-context-slice", 12000, () =>
            {
                using var run = shaper.ShapeText(context, options);
                return run.Length;
            });
            using var shaped = shaper.ShapeText("Retained glyph run".AsMemory(), options);
            using var glyphRun = new GlyphRunImpl(font, 14, shaped, default);
            Measure("skia-retained-text-blob", 100000, () =>
                (long)glyphRun.GetTextBlob(default, default).Bounds.Width);
            Measure("text-layout-short", 2000, () =>
            {
                using var layout = new TextLayout("Ready to render", Typeface.Default, 14, Brushes.Black, maxWidth: 160);
                return layout.TextLines.Count;
            });

            var list = new CountingListBox
            {
                ItemsSource = Enumerable.Range(0, 5000).Select(i => new Row(i)).ToArray(),
                ItemTemplate = new FuncDataTemplate<Row>((_, _) =>
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var path in new[] { nameof(Row.Name), nameof(Row.Status), nameof(Row.Owner), nameof(Row.Category), nameof(Row.Id) })
                    {
                        var text = new TextBlock { Width = 100, Height = 24 };
                        text.Bind(TextBlock.TextProperty, new Binding(path));
                        panel.Children.Add(text);
                    }
                    return panel;
                }, supportsRecycling: true)
            };
            var root = new TestRoot(true, list) { ClientSize = new Size(640, 480), Renderer = new NullRenderer() };
            root.LayoutManager.ExecuteInitialLayoutPass();
            if (list.Scroll is null || !list.GetRealizedContainers().Any())
                throw new InvalidOperationException("The recycling benchmark did not realize its viewport.");
            var position = 0;
            Measure("listbox-wheel-offset", 180, () => Scroll(24), list);
            Measure("listbox-viewport-jump", 100, () => Scroll(480), list);
            root.Child = null;

            var payload = new
            {
                schema = 1,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                backend = "Skia + HarfBuzz; headless layout; no GPU presentation",
                runtimeMode = Environment.GetEnvironmentVariable("AVALONIA_PERF_RUNTIME_MODE") ?? "unspecified",
                results
            };
            File.WriteAllText(args[0], JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return 0;

            long BindDescription(ReflectionBinding source)
            {
                using var expression = reflectionTarget.Bind(TextBlock.TextProperty, source);
                if (reflectionTarget.Text != "Item 23")
                    throw new InvalidOperationException("Reflection binding did not produce the expected value.");
                return reflectionTarget.Text.Length;
            }

            long Shape(string text)
            {
                using var run = shaper.ShapeText(text.AsMemory(), options);
                return run.Length;
            }

            long Scroll(int step)
            {
                position = (position + step) % 20000;
                list.Scroll!.Offset = new Vector(0, position);
                root.LayoutManager.ExecuteLayoutPass();
                var realized = list.GetRealizedContainers().Count();
                if (realized == 0 || realized > 150)
                    throw new InvalidOperationException($"Unexpected realized count: {realized}");
                return realized;
            }

            void Measure(string name, int operations, Func<long> action, CountingListBox? counter = null)
            {
                for (var w = 0; w < 3; ++w)
                    for (var i = 0; i < Math.Min(operations, 2048); ++i)
                        action();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var prepared = counter?.Prepared ?? 0;
                var cleared = counter?.Cleared ?? 0;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                long checksum = 0;
                for (var i = 0; i < operations; ++i)
                    checksum += action();
                var elapsed = Stopwatch.GetTimestamp() - started;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                results.Add(new Result(name, operations, elapsed * (1e9 / Stopwatch.Frequency), allocated,
                    checksum, (counter?.Prepared ?? 0) - prepared, (counter?.Cleared ?? 0) - cleared));
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private sealed record Result(string Scenario, int Operations, double Nanoseconds, long AllocatedBytes,
        long Checksum, int PreparedRows, int ClearedRows);

    private sealed class Node : Control
    {
        public static readonly StyledProperty<int> NumberProperty = AvaloniaProperty.Register<Node, int>("Number", inherits: true);
        public int Number { get => GetValue(NumberProperty); set => SetValue(NumberProperty, value); }
        public AvaloniaObject? InheritanceOwner { set => InheritanceParent = value; }
    }

    public sealed class Row(int id)
    {
        public int Id => id;
        public string Name => $"Item {id % 64}";
        public string Status => id % 2 == 0 ? "Ready" : "Running";
        public string Owner => $"Owner {id % 8}";
        public string Category => $"Group {id % 4}";
    }

    private sealed class CountingListBox : ListBox
    {
        public int Prepared { get; private set; }
        public int Cleared { get; private set; }
        protected override Type StyleKeyOverride => typeof(ListBox);
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            ++Prepared;
            base.PrepareContainerForItemOverride(container, item, index);
        }
        protected override void ClearContainerForItemOverride(Control container)
        {
            ++Cleared;
            base.ClearContainerForItemOverride(container);
        }
    }
}
