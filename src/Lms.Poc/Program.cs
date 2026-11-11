using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 160 * 1024);
builder.Services.AddHttpClient("livekit", c => c.Timeout = TimeSpan.FromSeconds(3));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var sessions = new ConcurrentDictionary<string, DemoUser>();
var rooms = new ConcurrentDictionary<string, BoardRoom>();
var recordingDownloads = new ConcurrentDictionary<string, (string Id, string Path, string Name, DateTimeOffset Expires)>();
var dataPath = Path.Combine(app.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataPath);
var config = app.Configuration;
string Key() => config["LiveKit:ApiKey"] ?? "devkey";
string Secret() => config["LiveKit:ApiSecret"] ?? "secret";
string ApiUrl() => (config["LiveKit:ApiUrl"] ?? "http://127.0.0.1:7880").TrimEnd('/');

string Token(string identity, string name, object grant)
{
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var head = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
    var body = B64(JsonSerializer.SerializeToUtf8Bytes(new { iss = Key(), sub = identity, name, nbf = now - 5, exp = now + 3600, video = grant }));
    var input = head + "." + body;
    return input + "." + B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret()), Encoding.UTF8.GetBytes(input)));
}

DemoUser? User(HttpContext ctx)
{
    var key = ctx.Request.Headers.Authorization.ToString();
    return key.StartsWith("Bearer ", StringComparison.Ordinal) && sessions.TryGetValue(key[7..], out var u)
        && u.Expires > DateTimeOffset.UtcNow ? u : null;
}

BoardRoom Board(string room) => rooms.GetOrAdd(room, id =>
{
    var path = Path.Combine(dataPath, id + ".json");
    try { return new BoardRoom { State = JsonSerializer.Deserialize<BoardState>(File.ReadAllText(path), json) ?? new() }; }
    catch (FileNotFoundException) { return new BoardRoom(); }
});

app.MapGet("/api/health", async (IHttpClientFactory clients) =>
{
    var available = false;
    try { using var response = await clients.CreateClient("livekit").GetAsync(ApiUrl()); available = response.IsSuccessStatusCode; }
    catch (HttpRequestException) { }
    catch (TaskCanceledException) { }
    return Results.Ok(new { backend = "ok", livekit = available, mode = "Local disposable PoC" });
});

app.MapPost("/api/join", async (JoinRequest req, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 40 || !Regex.IsMatch(req.Room ?? "", "^[a-zA-Z0-9-]{1,40}$"))
        return Results.BadRequest(new { error = "نام و شناسه کلاس معتبر وارد کنید." });
    var presenter = req.Role == "presenter";
    if (req.Role is not ("presenter" or "participant")) return Results.BadRequest();
    if (presenter && req.Code != (config["Demo:PresenterCode"] ?? "demo123"))
        return Results.Json(new { error = "کد مدرس صحیح نیست." }, statusCode: 403);
    var board = Board(req.Room!);
    await board.Gate.WaitAsync();
    try
    {
        // One presenter lease, reclaimed if its heartbeat has expired.
        if (presenter && board.PresenterId is not null && board.PresenterSeen > DateTimeOffset.UtcNow.AddSeconds(-30))
            return Results.Conflict(new { error = "مدرس دیگری در کلاس فعال است. ابتدا از آن پنجره خارج شوید." });
        var id = Guid.NewGuid().ToString("N");
        var access = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var user = new DemoUser(id, req.Name.Trim(), req.Room!, presenter, DateTimeOffset.UtcNow.AddHours(1));
        sessions[access] = user;
        if (presenter) { board.PresenterId = id; board.PresenterSeen = DateTimeOffset.UtcNow; }
        var token = Token(id, user.Name, new { roomJoin = true, room = user.Room, canSubscribe = true, canPublish = true, canPublishData = true, canPublishSources = presenter ? new[] { "camera", "microphone", "screen_share", "screen_share_audio" } : new[] { "camera", "microphone" } });
        return Results.Ok(new { token, access, identity = id, name = user.Name, room = user.Room, role = presenter ? "presenter" : "participant", url = config["LiveKit:Url"] ?? "ws://localhost:7880" });
    }
    finally { board.Gate.Release(); }
});

app.MapPost("/api/heartbeat", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user is null) return Results.Unauthorized();
    var board = Board(user.Room);
    await board.Gate.WaitAsync();
    try
    {
        if (user.Presenter && board.PresenterId != user.Id) return Results.StatusCode(403);
        if (user.Presenter) board.PresenterSeen = DateTimeOffset.UtcNow;
        return Results.Ok(new { board.State.Revision, recording = board.Recording && board.PresenterSeen > DateTimeOffset.UtcNow.AddSeconds(-30) });
    }
    finally { board.Gate.Release(); }
});

app.MapPost("/api/recording", async (RecordingCommand command, HttpContext ctx) =>
{
    var user = User(ctx);
    if (user is null) return Results.Unauthorized();
    var board = Board(user.Room);
    await board.Gate.WaitAsync();
    try
    {
        if (!user.Presenter || board.PresenterId != user.Id || board.PresenterSeen < DateTimeOffset.UtcNow.AddSeconds(-30)) return Results.StatusCode(403);
        board.Recording = command.Active;
        return Results.Ok(new { recording = board.Recording });
    }
    finally { board.Gate.Release(); }
});

app.MapPost("/api/leave", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user is null) return Results.Ok();
    var board = Board(user.Room);
    await board.Gate.WaitAsync();
    try { if (board.PresenterId == user.Id) { board.PresenterId = null; board.Recording = false; } }
    finally { board.Gate.Release(); }
    sessions.TryRemove(ctx.Request.Headers.Authorization.ToString()[7..], out _);
    return Results.Ok();
});

app.MapGet("/api/board", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user is null) return Results.Unauthorized();
    var board = Board(user.Room);
    await board.Gate.WaitAsync();
    try { return Results.Json(board.State); }
    finally { board.Gate.Release(); }
});

app.MapPut("/api/board", async (BoardCommand command, HttpContext ctx, IHttpClientFactory clients) =>
{
    var user = User(ctx);
    if (user is null) return Results.Unauthorized();
    if (!user.Presenter) return Results.StatusCode(403);
    if (command.Strokes is null || command.Strokes.Length > 300 || command.Strokes.Any(s =>
        s is null || s.Points is null || s.Points.Length is < 1 or > 1000 || !double.IsFinite(s.Width) || s.Width is < 1 or > 16 ||
        !Regex.IsMatch(s.Color ?? "", "^#[a-fA-F0-9]{6}$") || s.Points.Any(p => p is null || p.Length != 2 || p.Any(v => !double.IsFinite(v) || v is < 0 or > 1))))
        return Results.BadRequest(new { error = "حد تخته: ۳۰۰ خط، هر خط ۱۰۰۰ نقطه." });
    var board = Board(user.Room);
    await board.Gate.WaitAsync();
    try
    {
        if (board.PresenterId != user.Id || board.PresenterSeen < DateTimeOffset.UtcNow.AddSeconds(-30))
            return Results.StatusCode(403);
        if (command.BaseRevision != board.State.Revision) return Results.Conflict(board.State);
        var next = new BoardState(board.State.Revision + 1, command.Strokes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, json);
        if (bytes.Length > 128 * 1024) return Results.Json(new { error = "تخته پر شده است؛ چند خط را حذف یا تخته را پاک کنید." }, statusCode: 413);
        var path = Path.Combine(dataPath, user.Room + ".json");
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, path, true);
        board.State = next;
        // Server-origin invalidation only; browsers fetch the authoritative snapshot.
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { revision = next.Revision }));
        var notified = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl() + "/twirp/livekit.RoomService/SendData");
            request.Headers.Authorization = new("Bearer", Token("lms-backend", "LMS", new { roomAdmin = true, room = user.Room }));
            request.Content = JsonContent.Create(new { room = user.Room, data = payload, kind = 0, topic = "board.changed" });
            using var response = await clients.CreateClient("livekit").SendAsync(request);
            notified = response.IsSuccessStatusCode;
            if (!notified) app.Logger.LogWarning("LiveKit board notification failed: {Status}", response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { app.Logger.LogWarning("LiveKit unavailable; snapshot saved, polling will recover it."); }
        return Results.Ok(new { revision = next.Revision, notified });
    }
    finally { board.Gate.Release(); }
});

app.MapPost("/api/recordings", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user is null) return Results.Unauthorized();
    if (!user.Presenter) return Results.StatusCode(403);
    const long limit = 270L * 1024 * 1024;
    var bodyFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (bodyFeature is { IsReadOnly: false }) bodyFeature.MaxRequestBodySize = limit;
    if (ctx.Request.ContentLength is not long length || length < 32 || length > limit) return Results.BadRequest(new { error = "فایل ضبط خالی یا اندازه آن نامعتبر است." });
    var id = Guid.NewGuid().ToString("N");
    var directory = Path.Combine(dataPath, "recordings", user.Room);
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, id + ".webm");
    var temp = path + ".tmp";
    try
    {
        await using (var file = File.Create(temp)) await ctx.Request.Body.CopyToAsync(file, ctx.RequestAborted);
        var info = new FileInfo(temp);
        if (info.Length != length) return Results.BadRequest(new { error = "فایل ضبط کامل دریافت نشد؛ دوباره ذخیره کنید." });
        var header = new byte[4];
        await using (var file = File.OpenRead(temp)) await file.ReadExactlyAsync(header, ctx.RequestAborted);
        if (!header.SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return Results.BadRequest(new { error = "ساختار فایل ضبط معتبر نیست." });
        File.Move(temp, path);
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var name = $"class-{user.Room}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.webm";
        recordingDownloads[key] = (id, path, name, DateTimeOffset.UtcNow.AddHours(1));
        return Results.Ok(new { bytes = length, url = $"/api/recordings/{id}?key={key}", name });
    }
    finally { if (File.Exists(temp)) File.Delete(temp); }
});

app.MapGet("/api/recordings/{id}", (string id, string? key) =>
{
    if (key is null || !recordingDownloads.TryGetValue(key, out var recording) || recording.Id != id || recording.Expires < DateTimeOffset.UtcNow)
        return Results.Unauthorized();
    return Results.File(recording.Path, "video/webm", recording.Name, enableRangeProcessing: true);
});

app.Run();

record JoinRequest(string Name, string Room, string Role, string? Code);
record RecordingCommand(bool Active);
record DemoUser(string Id, string Name, string Room, bool Presenter, DateTimeOffset Expires);
record Stroke(string Color, double Width, double[][] Points);
record BoardCommand(long BaseRevision, Stroke[] Strokes);
record BoardState(long Revision, Stroke[] Strokes)
{
    public BoardState() : this(0, []) { }
}
class BoardRoom
{
    public bool Recording { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public BoardState State { get; set; } = new();
    public string? PresenterId { get; set; }
    public DateTimeOffset PresenterSeen { get; set; }
}
