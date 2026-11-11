using System.Text.Json;
using System.Text.Json.Nodes;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var fixture = File.ReadAllText(Path.Combine(root, "tests/Fixtures/whiteboard-v2.json"));
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
BoardScene Scene(string text) => JsonSerializer.Deserialize<BoardScene>(text, json)!;
void Reject(Action<JsonNode> mutate, string name)
{
    var node = JsonNode.Parse(fixture)!; mutate(node);
    try { Whiteboard.Validate(Scene(node.ToJsonString())); throw new Exception("Accepted invalid scene: " + name); }
    catch (InvalidDataException) { Console.WriteLine("PASS: rejects " + name); }
}
Whiteboard.Validate(Scene(fixture)); Check(true, "Valid editor scene accepted");
Reject(n => n["elements"]![0]!["type"] = "image", "image");
Reject(n => n["elements"]![0]!["link"] = "https://example.com", "external link");
Reject(n => n["elements"]![0]!["customData"] = new JsonObject(), "custom data");
Reject(n => n["appState"]!["zoom"] = 2, "shared viewport");
Reject(n => n["elements"]![0]!["x"] = 100001, "coordinate overflow");
Reject(n => n["elements"]!.AsArray().Add(n["elements"]![0]!.DeepClone()), "duplicate identifiers");
Reject(n => n["elements"]![0]!["boundElements"] = JsonNode.Parse("[{\"id\":\"missing\",\"type\":\"text\"}]"), "dangling binding");
Reject(n => { var a = n["elements"]!.AsArray(); var e = a[0]!.DeepClone(); for (var i=1;i<=1000;i++) { var c=e.DeepClone();c["id"]="item-"+i;a.Add(c); } }, "element count");
var dir = Path.Combine(root, ".run/whiteboard-model-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
var path = Path.Combine(dir, "legacy.json");
var legacy = "{\"revision\":7,\"strokes\":[{\"color\":\"#7156ed\",\"width\":4,\"points\":[[0.1,0.2],[0.5,0.6]]},{\"color\":\"#243451\",\"width\":2,\"points\":[[0.2,0.3]]}]}";
File.WriteAllText(path, legacy);
var migrated = Whiteboard.Load(path);
Check(migrated.Revision == 7 && migrated.Scene.Elements.Length == 2, "Migration preserves revision, lines and single-point strokes");
var first = migrated.Scene.Elements[0];
Check(first.GetProperty("x").GetDouble() == 120 && first.GetProperty("y").GetDouble() == 135 && first.GetProperty("points")[1][0].GetDouble() == 480 && first.GetProperty("strokeWidth").GetDouble() == 4, "Migration preserves geometry and pen width");
Check(File.ReadAllText(Directory.GetFiles(dir, "*.bak").Single()) == legacy, "Exact legacy backup retained");
var bytes = File.ReadAllBytes(path); var again = Whiteboard.Load(path);
Check(again.Scene.Elements.Length == 2 && File.ReadAllBytes(path).SequenceEqual(bytes) && Directory.GetFiles(dir,"*.bak").Length == 1, "Reload is stable and migration runs once");
foreach (var corrupt in new[] { "{broken", "{\"schemaVersion\":99}", "{\"revision\":0,\"strokes\":[{\"points\":[]}]}", "{\"hello\":1}" })
{
    var file = Path.Combine(dir, Guid.NewGuid()+".json"); File.WriteAllText(file, corrupt);
    try { Whiteboard.Load(file); throw new Exception("Corrupt file accepted"); } catch (InvalidDataException) { }
    Check(File.ReadAllText(file) == corrupt, "Corrupt/unknown file is never overwritten");
}
Check(new BoardState().Scene.Elements.Length == 0, "Empty board supported");
Console.WriteLine("Whiteboard model verification complete.");
