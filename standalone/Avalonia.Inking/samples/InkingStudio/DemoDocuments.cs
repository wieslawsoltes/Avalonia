using Avalonia.Inking.Core;
namespace InkingStudio;
public static class DemoDocuments
{
    public static InkDocument Showcase()
    {
        var strokes = new List<InkStroke>();
        InkStroke Path(double[][] xy, uint color = 0xFF5265D9, double width = 4) => new(xy.Select((p, i) => new InkPoint(p[0], p[1], .45 + .5 * Math.Sin((i + 1d) / (xy.Length + 1) * Math.PI))), InkPresets.Pen with { Color = color, Width = width });
        // Every mark in the showcase is real editable vector ink, including the lettering.
        strokes.Add(Path([[76,90],[116,90],[96,90],[96,168],[76,168],[116,168]], width: 7));
        strokes.Add(Path([[148,168],[148,92],[211,168],[211,92]], width: 7));
        strokes.Add(Path([[247,91],[247,169],[247,133],[293,91],[247,133],[298,169]], width: 7));
        strokes.Add(new InkStroke(Enumerable.Range(0, 140).Select(i => new InkPoint(75 + i * 3.65, 215 + 21 * Math.Sin(i * .075), .15 + .8 * Math.Sin(i / 139d * Math.PI))), InkPresets.Pen with { Color = 0xFF9361CE, Width = 14 }));
        strokes.Add(InkGeometry.Shape(InkTool.Rectangle, new(85, 295), new(230, 375), InkPresets.Pen with { Color = 0xFF279B87, Width = 3 }));
        strokes.Add(InkGeometry.Shape(InkTool.Ellipse, new(320, 285), new(445, 390), InkPresets.Pen with { Color = 0xFF5265D9, Width = 3 }));
        strokes.Add(Path([[246,335],[296,335],[285,325],[298,335],[285,345]], 0xFF738096, 3));
        strokes.Add(Path([[142,337],[155,350],[180,321]], 0xFF279B87, 5));
        strokes.Add(Path([[365,325],[385,309],[400,329],[385,349],[365,365]], 0xFFDA5C72, 3));
        strokes.Add(new InkStroke(Enumerable.Range(0, 70).Select(i => new InkPoint(84 + i * 6.6, 437 + 4 * Math.Sin(i * .2), .3 + .6 * i / 70d)), InkPresets.Pencil with { Width = 3 }));
        var highlights = new[] { new InkStroke([new InkPoint(68, 177), new(303, 177)], InkPresets.Highlighter with { Width = 20 }) };
        return new InkDocument("Creative notes", [new InkLayer("Highlights", highlights), new InkLayer("Sketch", strokes)]);
    }
    public static InkDocument Large(int count)
    {
        count = Math.Clamp(count, 1, 50_000);
        var strokes = Enumerable.Range(0, count).Select(i => new InkStroke(Enumerable.Range(0, 8).Select(j => new InkPoint(i % 200 * 48 + j * 4, i / 200 * 32 + Math.Sin(j * .8) * 7, .3 + j * .08)), InkPresets.Pen with { Width = 2, Color = InkPresets.Palette[i % InkPresets.Palette.Length] }));
        return new InkDocument($"{count:N0} strokes", [new InkLayer("Performance fixture", strokes)]);
    }
}
