using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Avalonia.Inking.Core;

public sealed class InkFormatException(string message, Exception? inner = null) : FormatException(message, inner);

/// <summary>Versioned, bounded, reflection-free persistence suitable for trimmed WebAssembly applications.</summary>
public static class InkSerializer
{
    public const string Schema = "avalonia-ink";
    public const int Version = 1;
    public static string Serialize(InkDocument document)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteString("schema", Schema); w.WriteNumber("version", Version); w.WriteString("title", document.Title);
            w.WriteStartArray("layers");
            foreach (var layer in document.Layers)
            {
                w.WriteStartObject(); w.WriteString("id", layer.Id); w.WriteString("name", layer.Name);
                w.WriteBoolean("visible", layer.IsVisible); w.WriteBoolean("locked", layer.IsLocked); w.WriteNumber("opacity", layer.Opacity);
                w.WriteStartArray("strokes");
                foreach (var s in layer.Strokes)
                {
                    w.WriteStartObject(); w.WriteString("id", s.Id); w.WriteStartObject("brush");
                    var b = s.Brush; w.WriteNumber("kind", (int)b.Kind); w.WriteNumber("color", b.Color); w.WriteNumber("width", b.Width); w.WriteNumber("opacity", b.Opacity);
                    w.WriteBoolean("pressure", b.UsePressure); w.WriteNumber("minimum", b.MinimumPressureWidth); w.WriteNumber("curve", b.PressureCurve);
                    w.WriteNumber("aspect", b.NibAspect); w.WriteNumber("angle", b.NibAngle); w.WriteNumber("tilt", b.TiltInfluence); w.WriteEndObject();
                    w.WriteStartArray("transform");
                    foreach (var n in new[] { s.Transform.A, s.Transform.B, s.Transform.C, s.Transform.D, s.Transform.Tx, s.Transform.Ty }) w.WriteNumberValue(n);
                    w.WriteEndArray(); w.WriteStartArray("points");
                    foreach (var p in s.Points)
                    {
                        w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(p.Pressure);
                        w.WriteNumberValue(p.TiltX); w.WriteNumberValue(p.TiltY); w.WriteNumberValue(p.Twist); w.WriteNumberValue(p.Timestamp); w.WriteEndArray();
                    }
                    w.WriteEndArray(); w.WriteEndObject();
                }
                w.WriteEndArray(); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());
        if (json.Length > InkLimits.MaxJsonCharacters) throw new InkFormatException("Serialized document exceeds the 64 Mi-character limit.");
        return json;
    }
    public static InkDocument Deserialize(string json)
    {
        if (json is null || json.Length == 0 || json.Length > InkLimits.MaxJsonCharacters) throw new InkFormatException("Invalid ink document size.");
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var root = parsed.RootElement;
            if (root.GetProperty("schema").GetString() != Schema || root.GetProperty("version").GetInt32() != Version) throw new InkFormatException("Unsupported ink document schema or version.");
            var sourceLayers = root.GetProperty("layers");
            InkLimits.Require(sourceLayers.ValueKind == JsonValueKind.Array && sourceLayers.GetArrayLength() is > 0 and <= InkLimits.MaxLayers, "Invalid layers.");
            var layers = new List<InkLayer>(); var strokeCount = 0; var pointCount = 0;
            foreach (var layer in sourceLayers.EnumerateArray())
            {
                var sourceStrokes = layer.GetProperty("strokes");
                InkLimits.Require(sourceStrokes.ValueKind == JsonValueKind.Array && (strokeCount += sourceStrokes.GetArrayLength()) <= InkLimits.MaxStrokes, "Too many strokes.");
                var strokes = new List<InkStroke>();
                foreach (var s in sourceStrokes.EnumerateArray())
                {
                    var b = s.GetProperty("brush");
                    var brush = new InkBrush
                    {
                        Kind = (InkBrushKind)b.GetProperty("kind").GetInt32(), Color = b.GetProperty("color").GetUInt32(), Width = b.GetProperty("width").GetDouble(),
                        Opacity = b.GetProperty("opacity").GetDouble(), UsePressure = b.GetProperty("pressure").GetBoolean(), MinimumPressureWidth = b.GetProperty("minimum").GetDouble(),
                        PressureCurve = b.GetProperty("curve").GetDouble(), NibAspect = b.GetProperty("aspect").GetDouble(), NibAngle = b.GetProperty("angle").GetDouble(), TiltInfluence = b.GetProperty("tilt").GetDouble()
                    };
                    var t = s.GetProperty("transform"); InkLimits.Require(t.GetArrayLength() == 6, "Invalid transform.");
                    var transform = new InkTransform(t[0].GetDouble(), t[1].GetDouble(), t[2].GetDouble(), t[3].GetDouble(), t[4].GetDouble(), t[5].GetDouble());
                    var sourcePoints = s.GetProperty("points"); var length = sourcePoints.GetArrayLength();
                    InkLimits.Require(length is > 0 and <= InkLimits.MaxPointsPerStroke && (pointCount += length) <= InkLimits.MaxPoints, "Too many points.");
                    var points = new InkPoint[length]; var index = 0;
                    foreach (var p in sourcePoints.EnumerateArray())
                    {
                        InkLimits.Require(p.GetArrayLength() == 7, "An ink sample requires seven channels.");
                        points[index++] = new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble(), p[3].GetDouble(), p[4].GetDouble(), p[5].GetDouble(), p[6].GetDouble());
                    }
                    strokes.Add(new InkStroke(points, brush, s.GetProperty("id").GetGuid(), transform));
                }
                layers.Add(new InkLayer(layer.GetProperty("name").GetString()!, strokes, layer.GetProperty("id").GetGuid(), layer.GetProperty("visible").GetBoolean(), layer.GetProperty("locked").GetBoolean(), layer.GetProperty("opacity").GetDouble()));
            }
            return new InkDocument(root.GetProperty("title").GetString()!, layers);
        }
        catch (InkFormatException) { throw; }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InkFormatException("Invalid ink document: " + e.Message, e); }
    }

    public static string ExportSvg(InkDocument document, uint? background = null, double margin = 24)
    {
        InkLimits.Require(double.IsFinite(margin) && margin is >= 0 and <= 1024, "Invalid export margin.");
        var bounds = document.ContentBounds;
        if (bounds.IsEmpty) bounds = new(0, 0, 800, 600);
        bounds = bounds.Inflate(margin);
        var text = new StringBuilder();
        using (var xml = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true }))
        {
            xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            xml.WriteAttributeString("viewBox", $"{N(bounds.X)} {N(bounds.Y)} {N(Math.Max(1, bounds.Width))} {N(Math.Max(1, bounds.Height))}");
            xml.WriteElementString("title", document.Title);
            if (background is { } bg)
            {
                xml.WriteStartElement("rect"); xml.WriteAttributeString("x", N(bounds.X)); xml.WriteAttributeString("y", N(bounds.Y));
                xml.WriteAttributeString("width", N(bounds.Width)); xml.WriteAttributeString("height", N(bounds.Height));
                xml.WriteAttributeString("fill", Color(bg)); xml.WriteAttributeString("fill-opacity", N((bg >> 24) / 255d)); xml.WriteEndElement();
            }
            foreach (var layer in document.Layers.Where(l => l.IsVisible && l.Opacity > 0))
            {
                xml.WriteStartElement("g"); xml.WriteAttributeString("data-layer", layer.Name); xml.WriteAttributeString("opacity", N(layer.Opacity));
                foreach (var stroke in layer.Strokes)
                {
                    var sink = new SvgSink(); InkGeometry.EmitStroke(stroke, sink);
                    xml.WriteStartElement("path"); xml.WriteAttributeString("d", sink.ToString()); xml.WriteAttributeString("fill", Color(stroke.Brush.Color));
                    xml.WriteAttributeString("fill-opacity", N(stroke.Brush.Opacity * (stroke.Brush.Color >> 24) / 255d));
                    xml.WriteAttributeString("fill-rule", "nonzero"); xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return text.ToString();
    }
    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Color(uint value) => "#" + (value & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
    private sealed class SvgSink : IInkPathSink
    {
        private readonly StringBuilder _path = new();
        private void Point(InkVector p) => _path.Append(N(p.X)).Append(' ').Append(N(p.Y)).Append(' ');
        public void Begin(InkVector p) { _path.Append('M'); Point(p); }
        public void Line(InkVector p) { _path.Append('L'); Point(p); }
        public void Cubic(InkVector a, InkVector b, InkVector end) { _path.Append('C'); Point(a); Point(b); Point(end); }
        public void Close() => _path.Append('Z');
        public override string ToString() => _path.ToString();
    }
}
