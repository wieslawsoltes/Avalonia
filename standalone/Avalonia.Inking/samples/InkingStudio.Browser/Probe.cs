using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Inking;
using Avalonia.Inking.Core;
using Avalonia.Inking.Specs;
using Avalonia.VisualTree;
namespace InkingStudio.Browser;

/// <summary>Diagnostics and deterministic setup for the sample, not part of any production library.</summary>
public static partial class Probe
{
    private static StudioView View => App.Studio ?? throw new InvalidOperationException("Sample is not initialized.");
    [JSExport] public static bool Ready() => App.Studio is { IsReady: true, InkSurface.Presenter: not null, InkSurface.Adorner: not null };
    [JSExport] public static string RunCoreTests() => InkSpecs.ToJson(InkSpecs.Run());
    [JSExport] public static void Reset() => View.ResetForValidation();
    [JSExport] public static string DocumentJson() => InkSerializer.Serialize(View.Session.Document);
    [JSExport] public static string TryLoadJson(string json)
    {
        try { var document = InkSerializer.Deserialize(json); View.LoadDocument(document); return "ok"; }
        catch (Exception error) { return error.GetType().Name + ": " + error.Message; }
    }
    [JSExport] public static void Fixture(string name)
    {
        View.ResetForValidation();
        var document = name switch
        {
            "showcase" => DemoDocuments.Showcase(),
            "large" => DemoDocuments.Large(10_000),
            "highlighter" => new InkDocument("Alpha fixture", [new InkLayer(strokes: [new InkStroke(Enumerable.Range(0, 80).Select(i => new InkPoint(70 + i * 4, 130)), InkPresets.Highlighter with { Width = 36 })])]),
            _ => throw new ArgumentException("Unknown fixture.")
        };
        View.LoadDocument(document);
    }
    [JSExport] public static void ScrollTo(string id)
    {
        var control = View.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);
        control?.BringIntoView();
    }
    [JSExport] public static string Bounds(string id) => Json(w => { if (View.FindAutomationBounds(id) is { } rect) WriteRect(w, rect); else w.WriteNullValue(); });
    [JSExport] public static string Snapshot() => Json(w =>
    {
        var c = View.InkSurface; var s = c.Session;
        w.WriteStartObject(); w.WriteBoolean("ready", Ready()); w.WriteBoolean("browser", OperatingSystem.IsBrowser());
        w.WriteString("avalonia", typeof(Control).Assembly.GetName().Version?.ToString());
        w.WriteString("title", s.Document.Title); w.WriteNumber("strokes", s.Document.StrokeCount); w.WriteNumber("points", s.Document.PointCount);
        w.WriteNumber("layers", s.Document.Layers.Length); w.WriteNumber("selected", s.Selection.Count); w.WriteNumber("undo", s.UndoCount); w.WriteNumber("redo", s.RedoCount);
        w.WriteBoolean("dirty", s.IsDirty); w.WriteBoolean("editing", s.IsEditing); w.WriteBoolean("readOnly", c.EffectiveReadOnly);
        w.WriteBoolean("activeLocked", s.ActiveLayer.IsLocked); w.WriteBoolean("activeVisible", s.ActiveLayer.IsVisible);
        w.WriteString("tool", c.Tool.ToString()); w.WriteNumber("zoom", c.Zoom); w.WriteNumber("panX", c.PanOffset.X); w.WriteNumber("panY", c.PanOffset.Y);
        w.WriteString("pointer", c.LastPointerKind); w.WriteNumber("pressure", c.LastPressure); w.WriteNumber("collected", c.CollectedStrokeCount); w.WriteNumber("cancelled", c.CancelledGestureCount);
        w.WriteNumber("inputSamples", c.AcceptedInputSamples); w.WriteNumber("failures", c.OperationFailureCount); w.WriteString("status", c.StatusText);
        w.WriteBoolean("presenter", c.Presenter is not null); w.WriteBoolean("adorner", c.Adorner is not null);
        w.WriteNumber("cachedStrokes", c.Presenter?.CachedStrokeCount ?? 0); w.WriteNumber("cachedPoints", c.Presenter?.CachedPointCount ?? 0);
        w.WriteNumber("visibleStrokes", c.Presenter?.LastVisibleStrokeCount ?? 0); w.WriteNumber("cacheHits", c.Presenter?.CacheHits ?? 0); w.WriteNumber("cacheMisses", c.Presenter?.CacheMisses ?? 0);
        w.WriteString("recovery", View.RecoveryState);
        w.WriteStartArray("handles");
        var origin = c.TranslatePoint(default, View) ?? default;
        foreach (var p in c.GetSelectionHandles()) { w.WriteStartArray(); w.WriteNumberValue(p.X + origin.X); w.WriteNumberValue(p.Y + origin.Y); w.WriteEndArray(); }
        w.WriteEndArray();
        w.WritePropertyName("selectionBounds");
        var r = s.SelectionBounds;
        if (r.IsEmpty) w.WriteNullValue(); else WriteRect(w, new(r.X, r.Y, r.Width, r.Height));
        var last = s.Document.AllStrokes.LastOrDefault();
        w.WritePropertyName("last");
        if (last is null) w.WriteNullValue();
        else
        {
            w.WriteStartObject(); w.WriteString("id", last.Id); w.WriteNumber("samples", last.Points.Length);
            w.WriteNumber("pressureMin", last.Points.Min(p => p.Pressure)); w.WriteNumber("pressureMax", last.Points.Max(p => p.Pressure));
            w.WriteNumber("tiltMax", last.Points.Max(p => Math.Abs(p.TiltX))); w.WriteNumber("twistMax", last.Points.Max(p => p.Twist));
            w.WriteNumber("width", last.Brush.Width); w.WriteNumber("opacity", last.Brush.Opacity); w.WriteString("kind", last.Brush.Kind.ToString());
            w.WritePropertyName("bounds"); WriteRect(w, new(last.Bounds.X, last.Bounds.Y, last.Bounds.Width, last.Bounds.Height));
            w.WriteStartArray("transform"); foreach (var value in new[] { last.Transform.A, last.Transform.B, last.Transform.C, last.Transform.D, last.Transform.Tx, last.Transform.Ty }) w.WriteNumberValue(value); w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndObject();
    });
    [JSExport] public static string ControlContracts() => Json(w =>
    {
        var a = new InkCanvas(); var b = new InkCanvas(); var data = new object(); a.DataContext = data;
        a.Session.AddStroke(new InkStroke([new InkPoint(1, 2)]));
        w.WriteStartObject();
        w.WriteBoolean("uniqueDefaultSession", !ReferenceEquals(a.Session, b.Session) && b.Session.Document.StrokeCount == 0);
        w.WriteBoolean("dataContextPreserved", ReferenceEquals(a.DataContext, data));
        w.WriteBoolean("templateParts", View.InkSurface.Presenter is not null && View.InkSurface.Adorner is not null);
        w.WriteBoolean("canvasIsTemplated", View.InkSurface is Avalonia.Controls.Primitives.TemplatedControl);
        w.WriteBoolean("toolbarIsTemplated", new InkToolbar() is Avalonia.Controls.Primitives.TemplatedControl);
        w.WriteBoolean("layerPanelIsTemplated", new InkLayerPanel() is Avalonia.Controls.Primitives.TemplatedControl);
        w.WriteEndObject();
    });
    private static void WriteRect(Utf8JsonWriter w, Rect r)
    {
        w.WriteStartObject(); w.WriteNumber("x", r.X); w.WriteNumber("y", r.Y); w.WriteNumber("width", r.Width); w.WriteNumber("height", r.Height); w.WriteEndObject();
    }
    private static string Json(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) action(writer); return Encoding.UTF8.GetString(stream.ToArray());
    }
}
