using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Inking.Core;

namespace Avalonia.Inking.Specs;

public sealed record SpecResult(string Name, bool Passed, string Detail, double Milliseconds);

/// <summary>The same regression suite executes as native .NET and inside the published browser application.</summary>
public static class InkSpecs
{
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Near(double a, double b, double tolerance = 1e-6) => Assert(Math.Abs(a - b) <= tolerance, $"Expected {b}, got {a}");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static InkStroke Line(double y = 0, double width = 2) => new([new InkPoint(0, y), new InkPoint(100, y)], InkPresets.Pen with { Width = width, UsePressure = false });
    private static InkSession Session(params InkStroke[] strokes) => new(new InkDocument(layers: [new InkLayer(strokes: strokes)]));
    public static List<SpecResult> Run()
    {
        var results = new List<SpecResult>();
        void Test(string name, Action action)
        {
            var watch = Stopwatch.StartNew();
            try { action(); results.Add(new(name, true, "", watch.Elapsed.TotalMilliseconds)); }
            catch (Exception e) { results.Add(new(name, false, e.GetType().Name + ": " + e.Message, watch.Elapsed.TotalMilliseconds)); }
        }
        Test("Point rejects NaN", () => Throws<ArgumentException>(() => new InkPoint(double.NaN, 0).Validate()));
        Test("Point rejects infinite coordinate", () => Throws<ArgumentException>(() => new InkPoint(0, double.PositiveInfinity).Validate()));
        Test("Point rejects invalid pressure", () => Throws<ArgumentException>(() => new InkPoint(0, 0, 1.1).Validate()));
        Test("Point rejects invalid tilt", () => Throws<ArgumentException>(() => new InkPoint(0, 0, 1, 91).Validate()));
        Test("Point rejects invalid twist", () => Throws<ArgumentException>(() => new InkPoint(0, 0, 1, 0, 0, -1).Validate()));
        Test("Point rejects negative time", () => Throws<ArgumentException>(() => new InkPoint(0, 0, Timestamp: -1).Validate()));
        Test("Twist interpolation crosses zero", () => Near(InkPoint.Lerp(new(0, 0, Twist: 350), new(10, 0, Twist: 10), .5).Twist, 0));
        Test("Pressure interpolation preserves channels", () => { var p = InkPoint.Lerp(new(0, 0, .2, 10, 20, 0, 10), new(100, 0, .8, 30, 40, 0, 30), .5); Near(p.Pressure, .5); Near(p.TiltX, 20); Near(p.Timestamp, 20); });
        Test("Brush rejects zero width", () => Throws<ArgumentException>(() => (InkPresets.Pen with { Width = 0 }).Validate()));
        Test("Brush rejects invalid opacity", () => Throws<ArgumentException>(() => (InkPresets.Pen with { Opacity = 2 }).Validate()));
        Test("Brush rejects invalid enum", () => Throws<ArgumentException>(() => (InkPresets.Pen with { Kind = (InkBrushKind)88 }).Validate()));
        Test("Pressure controls nib width", () => Assert(InkPresets.Pen.GetNib(new(0, 0, .1)).RadiusX < InkPresets.Pen.GetNib(new(0, 0, 1)).RadiusX));
        Test("Highlighter ignores pressure", () => Near(InkPresets.Highlighter.GetNib(new(0, 0, 0)).RadiusX, InkPresets.Highlighter.GetNib(new(0, 0, 1)).RadiusX));
        Test("Pencil responds to tilt", () => Assert(InkPresets.Pencil.GetNib(new(0, 0, 1, 70)).RadiusX > InkPresets.Pencil.GetNib(new(0, 0, 1)).RadiusX));
        Test("Immutable sample copy", () => { var points = new[] { new InkPoint(1, 2) }; var s = new InkStroke(points); points[0] = new(99, 99); Near(s.Points[0].X, 1); });
        Test("Empty stroke rejected", () => Throws<ArgumentException>(() => new InkStroke([])));
        Test("Duplicate stroke IDs rejected", () => { var s = Line(); Throws<ArgumentException>(() => new InkDocument(layers: [new InkLayer(strokes: [s, s])])); });
        Test("Duplicate layer IDs rejected", () => { var l = new InkLayer(); Throws<ArgumentException>(() => new InkDocument(layers: [l, l])); });
        Test("Empty document layer list rejected", () => Throws<ArgumentException>(() => new InkDocument(layers: [])));
        Test("Singular transform rejected", () => Throws<ArgumentException>(() => new InkTransform(0, 0, 0, 0, 0, 0).Validate()));
        Test("Affine composition and inversion", () => { var t = InkTransform.Scale(2, 3, new(5, 5)).Then(InkTransform.Rotation(.7, new(0, 0))).Then(InkTransform.Translation(12, -3)); var p = new InkVector(4, -8); var q = t.Inverse().Apply(t.Apply(p)); Near(q.X, p.X); Near(q.Y, p.Y); });
        Test("Affine edit retains raw pressure", () => { var s = new InkStroke([new InkPoint(10, 20, .3, 12, -8, 40, 100)]); var t = s.Transformed(InkTransform.Scale(2, 3, default)); Assert(t.Points == s.Points && t.Id == s.Id); Near(t.WorldPoint(0).X, 20); Near(t.Points[0].Pressure, .3); });
        Test("Maximum affine scale", () => Near(InkTransform.Scale(2, 4, default).MaximumScale, 4));
        Test("Clone allocates new identity", () => { var s = Line(); var c = s.Clone(); Assert(s.Id != c.Id && s.Points == c.Points); });
        Test("Line hit between sparse samples", () => Assert(InkGeometry.HitTest(Line(), new(50, .9))));
        Test("Line hit misses distant point", () => Assert(!InkGeometry.HitTest(Line(), new(50, 4))));
        Test("Fast eraser sweep crosses line", () => Assert(InkGeometry.HitSweep(Line(), new(50, -100), new(50, 100), 1)));
        Test("Polygon boundary included", () => Assert(InkGeometry.PointInPolygon(new(0, 5), [new(0, 0), new(10, 0), new(10, 10), new(0, 10)])));
        Test("Lasso finds crossing sparse stroke", () => Assert(InkGeometry.IntersectsPolygon(Line(), [new(45, -5), new(55, -5), new(55, 5), new(45, 5)])));
        Test("Lasso rejects outside stroke", () => Assert(!InkGeometry.IntersectsPolygon(Line(), [new(45, 20), new(55, 20), new(55, 30), new(45, 30)])));
        Test("Partial eraser produces two fragments", () => { var p = InkGeometry.Erase(Line(), new(50, -20), new(50, 20), 4); Assert(p.Count == 2); Near(p[0].Points[^1].X, 45); Near(p[1].Points[0].X, 55); });
        Test("Partial eraser interpolates pressure", () => { var s = new InkStroke([new InkPoint(0, 0, .2), new InkPoint(100, 0, .8)]); var pieces = InkGeometry.Erase(s, new(50, -10), new(50, 10), 2); Assert(pieces.Count == 2); var p = pieces[0].Points[^1]; Near(p.Pressure, .2 + .6 * p.X / 100); });
        Test("Partial eraser retains affine transform", () => { var s = Line().Transformed(InkTransform.Translation(100, 100)); var p = InkGeometry.Erase(s, new(150, 80), new(150, 120), 3); Assert(p.Count == 2 && p.All(x => x.Transform == s.Transform)); });
        Test("No-op eraser shares original stroke", () => { var s = Line(); var p = InkGeometry.Erase(s, new(150, 50), new(150, 80), 3); Assert(p.Count == 1 && ReferenceEquals(s, p[0])); });
        Test("Eraser removes tap", () => Assert(InkGeometry.Erase(new InkStroke([new InkPoint(10, 10)]), new(10, 0), new(10, 20), 2).Count == 0));
        Test("Line constraint snaps to 45 degrees", () => { var s = InkGeometry.Shape(InkTool.Line, default, new(80, 20), InkPresets.Pen, true); Near(s.Points[^1].Y, 0); });
        Test("Rectangle constraint produces square", () => { var s = InkGeometry.Shape(InkTool.Rectangle, default, new(80, 20), InkPresets.Pen, true); Near(s.Points[2].X, s.Points[2].Y); });
        Test("Ellipse closes geometry", () => { var s = InkGeometry.Shape(InkTool.Ellipse, new(10, 20), new(100, 60), InkPresets.Pen); Near(s.Points[0].X, s.Points[^1].X); Near(s.Points[0].Y, s.Points[^1].Y); });
        Test("Builder preserves tap", () => { var b = new InkStrokeBuilder(InkPresets.Pen); b.Add(new(3, 4)); Assert(b.Build().Points.Length == 1); });
        Test("Builder commits final endpoint", () => { var b = new InkStrokeBuilder(InkPresets.Pen, 1); b.Add(new(0, 0)); b.Add(new(100, 20, Timestamp: 10)); b.Add(new(101, 21, Timestamp: 12), true); Near(b.Build().Points[^1].X, 101); });
        Test("Builder preserves stationary pressure change", () => { var b = new InkStrokeBuilder(InkPresets.Pen); b.Add(new(0, 0, .1)); b.Add(new(0, 0, .9)); Assert(b.Count == 2); });
        Test("Undo and redo preserve snapshot identity", () => { var s = Session(); var before = s.Document; s.AddStroke(Line()); var after = s.Document; s.Undo(); Assert(ReferenceEquals(before, s.Document)); s.Redo(); Assert(ReferenceEquals(after, s.Document)); });
        Test("New edit clears redo", () => { var s = Session(); s.AddStroke(Line()); s.Undo(); s.AddStroke(Line(10)); Assert(s.RedoCount == 0); });
        Test("Transaction commits one undo item", () => { var s = Session(); using (var t = s.BeginTransaction("Gesture")) { s.AddStroke(Line()); s.AddStroke(Line(10)); t.Commit(); } Assert(s.UndoCount == 1 && s.Document.StrokeCount == 2); s.Undo(); Assert(s.Document.StrokeCount == 0); });
        Test("Transaction disposal rolls back", () => { var s = Session(Line()); var doc = s.Document; using (s.BeginTransaction("Erase")) s.Erase(new(50, -10), new(50, 10), 3, true); Assert(ReferenceEquals(doc, s.Document) && s.UndoCount == 0); });
        Test("Cancelled transaction preserves redo branch", () => { var s = Session(); s.AddStroke(Line()); s.Undo(); using (s.BeginTransaction("Cancelled")) s.AddStroke(Line(10)); Assert(s.CanRedo); s.Redo(); Assert(s.Document.StrokeCount == 1); });
        Test("Nested transaction rejected", () => { var s = Session(); using var t = s.BeginTransaction("One"); Throws<InvalidOperationException>(() => s.BeginTransaction("Two")); });
        Test("Dirty state follows save and undo", () => { var s = Session(); s.AddStroke(Line()); s.MarkSaved(); Assert(!s.IsDirty); s.AddStroke(Line(10)); Assert(s.IsDirty); s.Undo(); Assert(!s.IsDirty); });
        Test("Async saved snapshot does not mark later edit clean", () => { var s = Session(); s.AddStroke(Line()); var saved = s.Document; s.AddStroke(Line(10)); s.MarkSaved(saved); Assert(s.IsDirty); s.Undo(); Assert(!s.IsDirty); });
        Test("History capacity is bounded", () => { var s = Session(); s.HistoryCapacity = 2; for (var i = 0; i < 6; i++) s.AddStroke(Line(i)); Assert(s.UndoCount == 2); });
        Test("History point budget is bounded", () => { var s = Session(); s.HistoryPointBudget = 1; s.AddStroke(Line()); Assert(s.UndoCount == 0); });
        Test("Read-only blocks mutations", () => { var s = Session(); s.IsReadOnly = true; Throws<InvalidOperationException>(() => s.AddStroke(Line())); Assert(!s.CanUndo); });
        Test("Read-only cancels active transaction", () => { var s = Session(); var before = s.Document; using var t = s.BeginTransaction("One"); s.AddStroke(Line()); s.IsReadOnly = true; Assert(!s.IsEditing && ReferenceEquals(before, s.Document)); });
        Test("Locked layer blocks adding", () => { var s = Session(); s.UpdateLayer(s.ActiveLayerId, locked: true); Throws<InvalidOperationException>(() => s.AddStroke(Line())); });
        Test("Hidden layer blocks adding", () => { var s = Session(); s.UpdateLayer(s.ActiveLayerId, visible: false); Throws<InvalidOperationException>(() => s.AddStroke(Line())); });
        Test("Locked layer excluded from selection and erasure", () => { var s = Session(Line()); s.UpdateLayer(s.ActiveLayerId, locked: true); s.SelectAll(); s.Erase(new(50, -10), new(50, 10), 4, false); Assert(s.Selection.Count == 0 && s.Document.StrokeCount == 1); });
        Test("Last layer cannot be removed", () => { var s = Session(); Throws<InvalidOperationException>(() => s.RemoveLayer(s.ActiveLayerId)); });
        Test("Layer add activates and undo restores", () => { var s = Session(); var id = s.ActiveLayerId; s.AddLayer(); Assert(s.Document.Layers.Length == 2 && s.ActiveLayerId != id); s.Undo(); Assert(s.ActiveLayerId == id); });
        Test("Layer reorder preserves strokes", () => { var s = Session(Line()); var id = s.ActiveLayerId; s.AddLayer(); s.MoveLayer(id, 1); Assert(s.Document.Layers[^1].Id == id && s.Document.StrokeCount == 1); });
        Test("Selection transform is undoable", () => { var s = Session(Line()); s.SelectAll(); s.TransformSelection(InkTransform.Translation(10, 20)); Near(s.Document.AllStrokes.First().WorldPoint(0).Y, 20); s.Undo(); Near(s.Document.AllStrokes.First().WorldPoint(0).Y, 0); });
        Test("Delete selection does not delete unselected ink", () => { var a = Line(); var b = Line(20); var s = Session(a, b); s.SetSelection([a.Id]); s.DeleteSelection(); Assert(s.Document.StrokeCount == 1 && s.Document.AllStrokes.First().Id == b.Id); });
        Test("Selection rectangle uses actual geometry", () => { var s = Session(Line()); s.SelectRectangle(new(45, 20, 10, 10)); Assert(s.Selection.Count == 0); s.SelectRectangle(new(45, -2, 10, 4)); Assert(s.Selection.Count == 1); });
        Test("Stroke eraser deletes whole hit stroke", () => { var s = Session(Line(), Line(30)); s.Erase(new(50, -5), new(50, 5), 2, false); Assert(s.Document.StrokeCount == 1); });
        Test("Point eraser creates reversible fragments", () => { var s = Session(Line()); s.Erase(new(50, -5), new(50, 5), 2, true); Assert(s.Document.StrokeCount == 2); s.Undo(); Assert(s.Document.StrokeCount == 1); });
        Test("Portable JSON roundtrip is deterministic", () => { var s = new InkStroke([new InkPoint(1.25, -4.5, .32, 15, -21, 275, 1234)], InkPresets.Pencil).Transformed(InkTransform.Rotation(.3, new(7, 9))); var d = new InkDocument("Roundtrip", [new InkLayer("Layer", [s], opacity: .65)]); var json = InkSerializer.Serialize(d); Assert(json == InkSerializer.Serialize(InkSerializer.Deserialize(json))); });
        Test("Malformed import is atomic", () => { var s = Session(Line()); var before = s.Document; Throws<InkFormatException>(() => s.LoadJson("{invalid")); Assert(ReferenceEquals(before, s.Document)); });
        Test("Future schema version rejected", () => { var json = InkSerializer.Serialize(new InkDocument()).Replace("\"version\":1", "\"version\":9"); Throws<InkFormatException>(() => InkSerializer.Deserialize(json)); });
        Test("Unknown document schema rejected", () => { var json = InkSerializer.Serialize(new InkDocument()).Replace("avalonia-ink", "foreign"); Throws<InkFormatException>(() => InkSerializer.Deserialize(json)); });
        Test("Duplicate IDs in imported document rejected", () => { var a = Line(); var b = Line(20); var json = InkSerializer.Serialize(Session(a, b).Document).Replace(b.Id.ToString(), a.Id.ToString()); Throws<InkFormatException>(() => InkSerializer.Deserialize(json)); });
        Test("Invalid brush in imported document rejected", () => { var json = InkSerializer.Serialize(Session(Line()).Document).Replace("\"width\":2", "\"width\":-2"); Throws<InkFormatException>(() => InkSerializer.Deserialize(json)); });
        Test("Clipboard paste regenerates IDs", () => { var s = Session(Line()); s.SelectAll(); var id = s.SelectedStrokes[0].Id; s.PasteJson(s.CopySelectionJson()); Assert(s.Document.StrokeCount == 2 && !s.Selection.Contains(id)); });
        Test("Clipboard preserves source layer opacity", () => { var s = Session(Line()); s.UpdateLayer(s.ActiveLayerId, opacity: .4); s.SelectAll(); var d = InkSerializer.Deserialize(s.CopySelectionJson()); Near(d.AllStrokes.First().Brush.Opacity, .4); });
        Test("SVG escapes title and layer names", () => { var d = new InkDocument("<script>&", [new InkLayer("\"<&", [Line()])]); var svg = InkSerializer.ExportSvg(d); Assert(svg.Contains("&lt;script&gt;&amp;") && !svg.Contains("<script>")); });
        Test("SVG excludes hidden layers", () => { var d = new InkDocument(layers: [new InkLayer(strokes: [Line()], isVisible: false)]); Assert(!InkSerializer.ExportSvg(d).Contains("<path")); });
        Test("SVG fills each highlighter stroke once", () => { var svg = InkSerializer.ExportSvg(Session(new InkStroke([new InkPoint(0, 0), new(10, 10), new(20, 0)], InkPresets.Highlighter)).Document); Assert(svg.Split("<path").Length == 2 && svg.Contains("nonzero")); });
        Test("Spatial index matches exhaustive broad phase", () =>
        {
            var random = new Random(817); var strokes = Enumerable.Range(0, 800).Select(i => Line(i % 40 * 20).Transformed(InkTransform.Translation(i / 40 * 150 - 1200, 0))).ToArray();
            var index = new InkSpatialIndex(Session(strokes).Document);
            for (var i = 0; i < 100; i++)
            {
                var r = new InkRect(random.Next(-1400, 1800), random.Next(-100, 900), random.Next(1, 300), random.Next(1, 200));
                Assert(index.Query(r).Select(e => e.Stroke.Id).ToHashSet().SetEquals(strokes.Where(s => s.Bounds.Intersects(r)).Select(s => s.Id)), "Spatial broad phase mismatch");
            }
            Assert(index.CellEntryCount <= 250_000);
        });
        Test("Wide spatial query uses safe fallback", () => { var d = Session(Line(), Line(20)).Document; var q = new InkSpatialIndex(d).Query(new(-1_000_000, -1_000_000, 2_000_000, 2_000_000)); Assert(q.Count == 2); });
        Test("Session rejects reentrant edits", () => { var s = Session(); var rejected = false; s.Changed += (_, _) => { try { s.AddStroke(Line()); } catch (InvalidOperationException) { rejected = true; } }; s.AddStroke(Line()); Assert(rejected && s.Document.StrokeCount == 1); });
        return results;
    }
    public static string ToJson(IReadOnlyList<SpecResult> results)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("total", results.Count); writer.WriteNumber("passed", results.Count(r => r.Passed)); writer.WriteNumber("failed", results.Count(r => !r.Passed));
            writer.WriteStartArray("tests");
            foreach (var r in results) { writer.WriteStartObject(); writer.WriteString("name", r.Name); writer.WriteBoolean("passed", r.Passed); writer.WriteString("detail", r.Detail); writer.WriteNumber("milliseconds", r.Milliseconds); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
