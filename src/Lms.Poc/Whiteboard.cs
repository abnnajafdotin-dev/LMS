using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
record BoardCommand(int SchemaVersion, string Engine, long BaseRevision, BoardScene Scene);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
record BoardScene(JsonElement[] Elements, JsonElement AppState);
record BoardState(int SchemaVersion, string Engine, long Revision, BoardScene Scene)
{
    public BoardState() : this(2, "excalidraw", 0, new([], JsonSerializer.SerializeToElement(new { viewBackgroundColor = "#ffffff" }))) { }
}

static class Whiteboard
{
    public const int SnapshotLimit = 1024 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    static readonly HashSet<string> Types = ["freedraw", "line", "arrow", "rectangle", "ellipse", "diamond", "text"];
    static readonly HashSet<string> BaseFields = ["id", "type", "x", "y", "width", "height", "angle", "strokeColor", "backgroundColor", "fillStyle", "strokeWidth", "strokeStyle", "roughness", "opacity", "seed", "version", "versionNonce", "index", "isDeleted", "groupIds", "frameId", "boundElements", "updated", "link", "locked", "roundness"];
    static readonly HashSet<string> Arrowheads = ["arrow", "bar", "dot", "circle", "circle_outline", "triangle", "triangle_outline", "diamond", "diamond_outline", "crowfoot_one", "crowfoot_many", "crowfoot_one_or_many"];
    static bool Color(string? value) => value is "transparent" or "black" or "white" || Regex.IsMatch(value ?? "", "^#(?:[a-fA-F0-9]{3}|[a-fA-F0-9]{4}|[a-fA-F0-9]{6}|[a-fA-F0-9]{8})$");
    static void Require(bool valid) { if (!valid) throw new InvalidDataException("ساختار تخته نامعتبر است؛ فقط قلم، شکل، فلش و متن مجاز است."); }
    static double Number(JsonElement e, string name, double min, double max)
    { Require(e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n) && double.IsFinite(n) && n >= min && n <= max); return e.GetProperty(name).GetDouble(); }
    static string Text(JsonElement e, string name, int max = 128)
    { Require(e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.GetString()!.Length <= max); return e.GetProperty(name).GetString()!; }
    static void Boolean(JsonElement e, string name) => Require(e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False);
    static bool NullOrAbsent(JsonElement e, string name) => !e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null;

    public static void Validate(BoardScene? scene)
    {
        Require(scene?.Elements is not null && scene.Elements.Length <= 1000);
        var state = scene!.AppState;
        Require(state.ValueKind == JsonValueKind.Object && state.EnumerateObject().Count() == 1 && Color(Text(state, "viewBackgroundColor")));
        var ids = new HashSet<string>();
        foreach (var e in scene.Elements)
        {
            Require(e.ValueKind == JsonValueKind.Object);
            var id = Text(e, "id"); Require(id.Length > 0 && ids.Add(id));
            var type = Text(e, "type"); Require(Types.Contains(type));
            var extra = type switch {
                "freedraw" => new[] { "points", "pressures", "simulatePressure", "lastCommittedPoint" },
                "line" or "arrow" => ["points", "lastCommittedPoint", "startBinding", "endBinding", "startArrowhead", "endArrowhead", "elbowed", "fixedSegments", "startIsSpecial", "endIsSpecial"],
                "text" => ["fontSize", "fontFamily", "text", "textAlign", "verticalAlign", "containerId", "originalText", "autoResize", "lineHeight"],
                _ => Array.Empty<string>()
            };
            Require(e.EnumerateObject().All(p => BaseFields.Contains(p.Name) || extra.Contains(p.Name)));
            Require(NullOrAbsent(e, "customData") && NullOrAbsent(e, "link") && NullOrAbsent(e, "frameId") && NullOrAbsent(e, "fileId"));
            foreach (var field in new[] { "x", "y", "width", "height", "angle" }) Number(e, field, field is "width" or "height" ? 0 : -100000, 100000);
            Number(e, "strokeWidth", 0, 100); Number(e, "roughness", 0, 10); Number(e, "opacity", 0, 100);
            Number(e, "seed", 0, uint.MaxValue); Number(e, "version", 1, int.MaxValue); Number(e, "versionNonce", 0, uint.MaxValue); Number(e, "updated", 0, 9007199254740991);
            foreach (var name in new[] { "seed", "version", "versionNonce", "updated" }) Require(e.GetProperty(name).GetDouble() % 1 == 0);
            Boolean(e, "isDeleted"); Boolean(e, "locked");
            Require(Color(Text(e, "strokeColor")) && Color(Text(e, "backgroundColor")));
            Require(new[] { "solid", "dashed", "dotted" }.Contains(Text(e, "strokeStyle")));
            Require(new[] { "solid", "hachure", "cross-hatch", "zigzag" }.Contains(Text(e, "fillStyle")));
            Require(e.TryGetProperty("groupIds", out var groups) && groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() <= 32);
            foreach (var g in groups.EnumerateArray()) Require(g.ValueKind == JsonValueKind.String && g.GetString()!.Length <= 128);
            if (!NullOrAbsent(e, "index")) Require(Regex.IsMatch(Text(e, "index"), "^[a-zA-Z0-9]+$"));
            if (!NullOrAbsent(e, "roundness")) { var r = e.GetProperty("roundness"); Number(r, "type", 1, 3); if (r.TryGetProperty("value", out _)) Number(r, "value", 0, 100000); }
            if (type is "freedraw" or "line" or "arrow")
            {
                Require(e.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array && points.GetArrayLength() is >= 1 and <= 2000);
                foreach (var p in points.EnumerateArray()) { Require(p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2); foreach (var v in p.EnumerateArray()) Require(v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) && Math.Abs(n) <= 100000); }
                if (type == "freedraw") { Boolean(e, "simulatePressure"); Require(e.TryGetProperty("pressures", out var pressures) && pressures.ValueKind == JsonValueKind.Array && pressures.GetArrayLength() <= 2000); foreach (var p in pressures.EnumerateArray()) Require(p.TryGetDouble(out var n) && n >= 0 && n <= 1); }
                if (type is "line" or "arrow")
                {
                    foreach (var field in new[] { "startArrowhead", "endArrowhead" }) Require(NullOrAbsent(e, field) || Arrowheads.Contains(Text(e, field)));
                    if (e.TryGetProperty("elbowed", out _)) Boolean(e, "elbowed");
                    foreach (var field in new[] { "startIsSpecial", "endIsSpecial" }) if (!NullOrAbsent(e, field)) Boolean(e, field);
                    if (!NullOrAbsent(e, "fixedSegments"))
                    {
                        var segments = e.GetProperty("fixedSegments"); Require(segments.ValueKind == JsonValueKind.Array && segments.GetArrayLength() <= 2000);
                        foreach (var segment in segments.EnumerateArray()) { Number(segment, "index", 0, 2000); ValidatePoint(segment.GetProperty("start")); ValidatePoint(segment.GetProperty("end")); }
                    }
                }
                if (!NullOrAbsent(e, "lastCommittedPoint")) ValidatePoint(e.GetProperty("lastCommittedPoint"));
            }
            if (type == "text")
            {
                Text(e, "text", 10000); Text(e, "originalText", 10000); Number(e, "fontSize", 1, 1000); Number(e, "fontFamily", 1, 10); Number(e, "lineHeight", .1, 10); Boolean(e, "autoResize");
                Require(new[] { 1d, 2, 3, 5, 6, 7, 8, 9 }.Contains(e.GetProperty("fontFamily").GetDouble()));
                Require(new[] { "left", "center", "right" }.Contains(Text(e, "textAlign")) && new[] { "top", "middle", "bottom" }.Contains(Text(e, "verticalAlign")));
            }
            ValidateTree(e, 0);
        }
        foreach (var e in scene.Elements)
        {
            foreach (var field in new[] { "containerId", "startBinding", "endBinding" })
            {
                if (NullOrAbsent(e, field)) continue;
                var binding = e.GetProperty(field);
                Require(ids.Contains(field == "containerId" ? binding.GetString()! : Text(binding, "elementId")));
                if (field != "containerId") { Number(binding, "focus", -1, 1); Number(binding, "gap", 0, 100000); if (!NullOrAbsent(binding, "fixedPoint")) ValidatePoint(binding.GetProperty("fixedPoint")); }
            }
            if (!NullOrAbsent(e, "boundElements"))
            { var bound = e.GetProperty("boundElements"); Require(bound.ValueKind == JsonValueKind.Array && bound.GetArrayLength() <= 1000); foreach (var b in bound.EnumerateArray()) Require(ids.Contains(Text(b, "id")) && Text(b, "type") is "arrow" or "text"); }
        }
    }
    static void ValidatePoint(JsonElement point)
    {
        Require(point.ValueKind == JsonValueKind.Array && point.GetArrayLength() == 2);
        foreach (var v in point.EnumerateArray()) Require(v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) && Math.Abs(n) <= 100000);
    }
    static void ValidateTree(JsonElement e, int depth)
    {
        Require(depth <= 24);
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { Require(p.Name.Length <= 128); Require(p.Name is not ("customData" or "files" or "src" or "url")); ValidateTree(p.Value, depth + 1); }
        else if (e.ValueKind == JsonValueKind.Array) { Require(e.GetArrayLength() <= 2000); foreach (var p in e.EnumerateArray()) ValidateTree(p, depth + 1); }
        else if (e.ValueKind == JsonValueKind.String) Require(e.GetString()!.Length <= 10000);
        else if (e.ValueKind == JsonValueKind.Number) Require(e.TryGetDouble(out var n) && double.IsFinite(n));
    }

    public static BoardState Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var bytes = File.ReadAllBytes(path);
            Require(bytes.Length <= SnapshotLimit);
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.TryGetProperty("schemaVersion", out var schema))
            {
                Require(schema.GetInt32() == 2);
                var current = JsonSerializer.Deserialize<BoardState>(bytes, Json)!;
                Require(current.Engine == "excalidraw" && current.Revision >= 0); Validate(current.Scene); return current;
            }
            // Only the explicitly recognized legacy format may be migrated.
            Require(root.TryGetProperty("revision", out var rev) && rev.TryGetInt64(out var revision) && revision >= 0 && root.TryGetProperty("strokes", out var strokes) && strokes.ValueKind == JsonValueKind.Array && strokes.GetArrayLength() <= 300);
            var elements = new List<JsonElement>();
            foreach (var s in root.GetProperty("strokes").EnumerateArray())
            {
                var color = Text(s, "color"); Require(Color(color)); var width = Number(s, "width", 1, 16);
                var points = s.GetProperty("points").EnumerateArray().Select(p => p.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                Require(points.Length is >= 1 and <= 1000 && points.All(p => p.Length == 2 && p.All(v => double.IsFinite(v) && v >= 0 && v <= 1)));
                var x = points[0][0] * 1200; var y = points[0][1] * 675;
                var local = points.Select(p => new[] { p[0] * 1200 - x, p[1] * 675 - y }).ToArray();
                elements.Add(JsonSerializer.SerializeToElement(new { id = Guid.NewGuid().ToString("N"), type = "freedraw", x, y, width = (points.Max(p => p[0]) - points.Min(p => p[0])) * 1200, height = (points.Max(p => p[1]) - points.Min(p => p[1])) * 675, strokeColor = color, backgroundColor = "transparent", fillStyle = "solid", strokeWidth = width, strokeStyle = "solid", roughness = 0, opacity = 100, angle = 0, seed = 1, version = 1, versionNonce = 1, index = (string?)null, isDeleted = false, groupIds = Array.Empty<string>(), frameId = (string?)null, boundElements = (object?)null, updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), link = (string?)null, locked = false, roundness = (object?)null, points = local, pressures = Array.Empty<double>(), simulatePressure = true, lastCommittedPoint = (object?)null }));
            }
            var migrated = new BoardState(2, "excalidraw", root.GetProperty("revision").GetInt64(), new(elements.ToArray(), new BoardState().Scene.AppState));
            Validate(migrated.Scene);
            var output = JsonSerializer.SerializeToUtf8Bytes(migrated, Json); Require(output.Length <= SnapshotLimit);
            File.WriteAllBytes(path + ".legacy-" + Guid.NewGuid().ToString("N") + ".bak", bytes);
            File.WriteAllBytes(path + ".tmp", output); File.Move(path + ".tmp", path, true);
            return migrated;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or InvalidDataException)
        { throw new InvalidDataException("داده تخته قابل خواندن نیست؛ فایل اصلی حفظ شد. بازیابی نسخه پشتیبان لازم است.", e); }
    }
}
