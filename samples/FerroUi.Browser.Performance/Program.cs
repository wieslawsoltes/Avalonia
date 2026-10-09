using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Avalonia.VisualTree;

[assembly: SupportedOSPlatform("browser")]
namespace FerroUi.Browser.Performance;

public static partial class Program
{
    public static async Task Main(string[] args)
    {
        var software = args.Length > 0 && args[0].Contains("software=1", StringComparison.Ordinal);
        var options = new BrowserPlatformOptions();
        if (software) options.RenderingMode = new[] { BrowserRenderingMode.Software2D };
        await AppBuilder.Configure<PerfApplication>().StartBrowserAppAsync("out", options);
    }

    [JSExport]
    public static string Snapshot() => JsonSerializer.Serialize(PerfApplication.Capture(), PerfJsonContext.Default.PerfSnapshot);

    [JSExport]
    public static void SetOffset(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (PerfApplication.Scroll is { } scroll) scroll.Offset = new Vector(0, value);
    }

    [JSExport]
    public static void SetScenario(string scenario) => PerfApplication.SelectScenario(scenario);

    [JSExport]
    public static void SetTheme(bool dark)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    [JSExport]
    public static void ChangeResource(bool alternate)
    {
        // Multiple presenters consult the same nested graph via dynamic resources.
        PerfApplication.Accents["FerroUiAccent"] = alternate ? Brushes.DarkOrange : Brushes.SteelBlue;
    }

    [JSExport]
    public static void SetOverlay(bool enabled)
    {
        if (TopLevel.GetTopLevel(PerfApplication.View) is { } top)
            top.RendererDiagnostics.DebugOverlays = enabled ? RendererDebugOverlays.Fps : default;
    }
}

public sealed class PerfApplication : Application
{
    internal static Grid View { get; private set; } = null!;
    internal static ResourceDictionary Accents { get; } = new();
    private static Control? s_items;
    private static string s_scenario = "list";
    private static CountingListBox? s_list;
    internal static ScrollViewer? Scroll => s_items?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new SimpleTheme());
        var graph = new ResourceDictionary();
        for (var i = 0; i < 12; ++i)
            graph.MergedDictionaries.Add(new ResourceDictionary { ["unrelated" + i] = i });
        Accents["FerroUiAccent"] = Brushes.SteelBlue;
        graph.MergedDictionaries.Insert(0, Accents);
        Resources.MergedDictionaries.Add(graph);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        View = new Grid { RowDefinitions = new RowDefinitions("32,*") };
        View.Children.Add(new TextBlock
        {
            Text = "Avalonia performance parity — five-column rows",
            Margin = new Thickness(8, 4), FontSize = 16
        });
        SelectScenario("list");
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = View;
        base.OnFrameworkInitializationCompleted();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Row))]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The benchmark explicitly preserves Row properties for reflection binding.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Avalonia reflection binding uses its browser-compatible reflection accessors for these preserved properties.")]
    internal static void SelectScenario(string scenario)
    {
        if (scenario is not ("list" or "tree")) throw new ArgumentException("Unknown scenario.", nameof(scenario));
        if (s_items is not null) View.Children.Remove(s_items);
        s_scenario = scenario;
        s_list = null;
        if (scenario == "list")
        {
            s_list = new CountingListBox
            {
                ItemsSource = Enumerable.Range(0, 5000).Select(i => new Row(i)).ToArray(),
                ItemTemplate = new FuncDataTemplate<Row>((_, _) =>
                {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var path in new[] { nameof(Row.Name), nameof(Row.Status), nameof(Row.Owner), nameof(Row.Category), nameof(Row.Id) })
                    {
                        var text = new TextBlock { Width = 112, Height = 24, FontSize = 14 };
                        text.Bind(TextBlock.TextProperty, new ReflectionBinding(path));
                        panel.Children.Add(text);
                    }
                    return panel;
                }, supportsRecycling: true)
            };
            s_items = s_list;
        }
        else
        {
            var tree = new TreeView
            {
                ItemsSource = Enumerable.Range(0, 100).Select(i => new Row(i, true)).ToArray(),
                ItemTemplate = new FuncTreeDataTemplate(typeof(Row),
                    (value, _) => new TextBlock { Text = ((Row)value!).Name, Height = 24 },
                    value => ((Row)value!).Children)
            };
            tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
            {
                Setters = { new Setter(TreeViewItem.IsExpandedProperty, true) }
            });
            s_items = tree;
        }
        ScrollViewer.SetVerticalScrollBarVisibility(s_items, ScrollBarVisibility.Visible);
        Grid.SetRow(s_items, 1);
        View.Children.Add(s_items);
    }

    internal static PerfSnapshot Capture()
    {
        var scroll = Scroll;
        var containers = s_list?.GetRealizedContainers().ToArray() ?? Array.Empty<Control>();
        var thumbs = View.GetVisualDescendants().OfType<ScrollBar>()
            .Where(x => x.Orientation == Orientation.Vertical && x.IsVisible)
            .SelectMany(x => x.GetVisualDescendants().OfType<Thumb>()).ToArray();
        var thumb = thumbs.FirstOrDefault();
        var point = thumb?.TranslatePoint(default, View) ?? default;
        var texts = s_items?.GetVisualDescendants().OfType<TextBlock>()
            .Select(x => x.Text ?? "").ToArray() ?? Array.Empty<string>();
        // Stable integer checksum; never use randomized string.GetHashCode across processes.
        long checksum = 0;
        foreach (var text in texts)
            foreach (var character in text) checksum = unchecked((checksum * 31 + character) & 0x7FFFFFFF);
        return new PerfSnapshot
        {
            Ready = scroll is not null && scroll.Viewport.Height > 0,
            Scenario = s_scenario,
            Offset = scroll?.Offset.Y ?? 0,
            Extent = scroll?.Extent.Height ?? 0,
            Viewport = scroll?.Viewport.Height ?? 0,
            Prepared = s_list?.Prepared ?? 0,
            Cleared = s_list?.Cleared ?? 0,
            Realized = containers.Length,
            TextChecksum = checksum,
            ThumbX = point.X,
            ThumbY = point.Y,
            ThumbWidth = thumb?.Bounds.Width ?? 0,
            ThumbHeight = thumb?.Bounds.Height ?? 0
        };
    }

    public sealed class Row(int id, bool children = false)
    {
        public int Id => id;
        public string Name => $"Item {id % 64}";
        public string Status => id % 2 == 0 ? "Ready" : "Running";
        public string Owner => $"Owner {id % 8}";
        public string Category => $"Group {id % 4}";
        public Row[] Children { get; } = children ? Enumerable.Range(0, 4).Select(i => new Row(id * 4 + i)).ToArray() : Array.Empty<Row>();
    }

    private sealed class CountingListBox : ListBox
    {
        internal int Prepared;
        internal int Cleared;
        protected override Type StyleKeyOverride => typeof(ListBox);
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        { ++Prepared; base.PrepareContainerForItemOverride(container, item, index); }
        protected override void ClearContainerForItemOverride(Control container)
        { ++Cleared; base.ClearContainerForItemOverride(container); }
    }
}

public sealed class PerfSnapshot
{
    public bool Ready { get; init; }
    public string Scenario { get; init; } = "";
    public double Offset { get; init; }
    public double Extent { get; init; }
    public double Viewport { get; init; }
    public int Prepared { get; init; }
    public int Cleared { get; init; }
    public int Realized { get; init; }
    public long TextChecksum { get; init; }
    public double ThumbX { get; init; }
    public double ThumbY { get; init; }
    public double ThumbWidth { get; init; }
    public double ThumbHeight { get; init; }
}

[JsonSerializable(typeof(PerfSnapshot))]
internal partial class PerfJsonContext : JsonSerializerContext { }
