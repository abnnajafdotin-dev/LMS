using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080"), Timeout = TimeSpan.FromSeconds(15) };
var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/Fixtures/whiteboard-v2.json")))!;
void Check(bool ok, string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
async Task<JsonElement> Join(string room, string role) => await (await http.PostAsJsonAsync("/api/join",new { name="API validation", room, role, code="demo123" })).Content.ReadFromJsonAsync<JsonElement>();
async Task<HttpResponseMessage> Send(string method, JsonElement user, string path, object? body = null, string? raw = null)
{
    using var request = new HttpRequestMessage(new HttpMethod(method),path); request.Headers.Authorization = new("Bearer",user.GetProperty("access").GetString());
    if(raw is not null) request.Content = new StringContent(raw,Encoding.UTF8,"application/json"); else if(body is not null) request.Content = JsonContent.Create(body);
    return await http.SendAsync(request);
}
object Command(JsonNode scene, long revision=0) => new { schemaVersion=2,engine="excalidraw",baseRevision=revision,scene };
var room="api-v2-"+Guid.NewGuid().ToString("N")[..8];
var teacher=await Join(room,"presenter"); var learner=await Join(room,"participant");
Check((await http.GetAsync("/api/board")).StatusCode==HttpStatusCode.Unauthorized,"Unauthenticated snapshot denied");
Check((await Send("PUT",learner,"/api/board",Command(fixture))).StatusCode==HttpStatusCode.Forbidden,"Learner writes denied by server");
Check((await Send("PUT",teacher,"/api/board",raw:"{oops")).StatusCode==HttpStatusCode.BadRequest,"Malformed JSON rejected");
Check((await Send("PUT",teacher,"/api/board",new {baseRevision=0,strokes=Array.Empty<int>()})).StatusCode==HttpStatusCode.BadRequest,"Legacy client rejected");
Check((await Send("PUT",teacher,"/api/board",new {schemaVersion=99,engine="excalidraw",baseRevision=0,scene=fixture})).StatusCode==HttpStatusCode.BadRequest,"Unknown schema rejected");
foreach(var kind in new[]{"image","embeddable","iframe","unknown"}) { var scene=fixture.DeepClone();scene["elements"]![0]!["type"]=kind; Check((await Send("PUT",teacher,"/api/board",Command(scene))).StatusCode==HttpStatusCode.BadRequest,"Rejects element "+kind); }
var linked=fixture.DeepClone();linked["elements"]![0]!["link"]="https://example.com";
Check((await Send("PUT",teacher,"/api/board",Command(linked))).StatusCode==HttpStatusCode.BadRequest,"Links rejected");
var extra=fixture.DeepClone();extra["appState"]!["scrollX"]=20;
Check((await Send("PUT",teacher,"/api/board",Command(extra))).StatusCode==HttpStatusCode.BadRequest,"Shared viewport rejected");
var deep=fixture.DeepClone(); JsonNode nested=new JsonObject(); for(var i=0;i<28;i++) nested=new JsonObject{["nested"]=nested}; deep["elements"]![0]!["unexpected"]=nested;
Check((await Send("PUT",teacher,"/api/board",Command(deep))).StatusCode==HttpStatusCode.BadRequest,"Deep payload rejected");
var text=fixture["elements"]![0]!.DeepClone();text["type"]="text";text["text"]=new string('a',10000);text["originalText"]=new string('a',10000);text["fontSize"]=20;text["fontFamily"]=5;text["textAlign"]="left";text["verticalAlign"]="top";text["autoResize"]=true;text["lineHeight"]=1.25;text["containerId"]=null;
var large=fixture.DeepClone();large["elements"]=new JsonArray();
for(var i=0;i<53;i++){var e=text.DeepClone();e["id"]="large-"+i;large["elements"]!.AsArray().Add(e);}
Check((await Send("PUT",teacher,"/api/board",Command(large))).StatusCode==HttpStatusCode.RequestEntityTooLarge,"Snapshot over 1 MiB rejected");
Check((await Send("PUT",teacher,"/api/board",raw:new string('x',1200*1024))).StatusCode==HttpStatusCode.RequestEntityTooLarge,"Request body over 1100 KiB rejected");
var saved=await Send("PUT",teacher,"/api/board",Command(fixture));var ack=await saved.Content.ReadFromJsonAsync<JsonElement>();
Check(saved.IsSuccessStatusCode && ack.GetProperty("revision").GetInt64()==1,"Valid scene persisted after invalid requests");
Check((await Send("PUT",teacher,"/api/board",Command(fixture))).StatusCode==HttpStatusCode.Conflict,"Stale revision rejected");
var current=await (await Send("GET",learner,"/api/board")).Content.ReadFromJsonAsync<JsonElement>();
Check(current.GetProperty("revision").GetInt64()==1 && current.GetProperty("scene").GetProperty("elements").GetArrayLength()==1,"Rejected requests never overwrite state");
var other=await Join("other-"+Guid.NewGuid().ToString("N")[..8],"participant");
var isolated=await (await Send("GET",other,"/api/board")).Content.ReadFromJsonAsync<JsonElement>();
Check(isolated.GetProperty("scene").GetProperty("elements").GetArrayLength()==0,"Rooms isolated");
// This teacher deliberately sends no heartbeat so the server lease expires.
Console.WriteLine("Waiting for the 30-second presenter lease to expire..."); await Task.Delay(TimeSpan.FromSeconds(31));
Check((await Send("PUT",teacher,"/api/board",Command(fixture,1))).StatusCode==HttpStatusCode.Forbidden,"Expired presenter lease cannot write");
var replacement=await Join(room,"presenter");
Check((await Send("POST",teacher,"/api/heartbeat")).StatusCode==HttpStatusCode.Forbidden,"Reclaimed lease cannot be renewed by previous presenter");
await Send("POST",replacement,"/api/leave");await Send("POST",teacher,"/api/leave");await Send("POST",learner,"/api/leave");await Send("POST",other,"/api/leave");
Console.WriteLine("Whiteboard API verification complete.");
