using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var output = Path.Combine(root, ".run");
Directory.CreateDirectory(output);
var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080"), Timeout = TimeSpan.FromSeconds(10) };
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
var health = await http.GetFromJsonAsync<JsonElement>("/api/health");
Check(health.GetProperty("livekit").GetBoolean(), "Backend and LiveKit healthy");
var testRoom = "smoke-" + Guid.NewGuid().ToString("N")[..10];
var denied = await http.PostAsJsonAsync("/api/join", new { name = "Bad", room = testRoom, role = "presenter", code = "wrong" });
Check(denied.StatusCode == HttpStatusCode.Forbidden, "Presenter code enforced");
var join = await http.PostAsJsonAsync("/api/join", new { name = "API Presenter", room = testRoom, role = "presenter", code = "demo123" });
var presenter = await join.Content.ReadFromJsonAsync<JsonElement>();
var studentJoin = await http.PostAsJsonAsync("/api/join", new { name = "API Participant", room = testRoom, role = "participant" });
var student = await studentJoin.Content.ReadFromJsonAsync<JsonElement>();
async Task<HttpResponseMessage> Auth(string method, string path, JsonElement who, object? body = null)
{
    var request = new HttpRequestMessage(new HttpMethod(method), path);
    request.Headers.Authorization = new("Bearer", who.GetProperty("access").GetString());
    if (body is not null) request.Content = JsonContent.Create(body);
    return await http.SendAsync(request);
}
var command = new { baseRevision = 0, strokes = new[] { new { color = "#7156ed", width = 4, points = new[] { new[] { .1, .2 }, new[] { .4, .5 } } } } };
Check((await Auth("PUT", "/api/board", student, command)).StatusCode == HttpStatusCode.Forbidden, "Participant cannot write board");
var update = await Auth("PUT", "/api/board", presenter, command);
var ack = await update.Content.ReadFromJsonAsync<JsonElement>();
Check(update.IsSuccessStatusCode && ack.GetProperty("revision").GetInt32() == 1, "Board persisted even before a LiveKit room exists");
Check((await Auth("PUT", "/api/board", presenter, command)).StatusCode == HttpStatusCode.Conflict, "Stale revision rejected");
var board = await (await Auth("GET", "/api/board", student)).Content.ReadFromJsonAsync<JsonElement>();
Check(board.GetProperty("revision").GetInt32() == 1 && board.GetProperty("strokes").GetArrayLength() == 1, "Late join snapshot available");
Check((await http.GetAsync("/api/board")).StatusCode == HttpStatusCode.Unauthorized, "Snapshot requires demo session");
Check((await Auth("POST", "/api/recording", student, new { active = true })).StatusCode == HttpStatusCode.Forbidden, "Learner cannot start recording status");
Check((await http.PostAsJsonAsync("/api/recording", new { active = true })).StatusCode == HttpStatusCode.Unauthorized, "Recording status requires authentication");
Check((await Auth("POST", "/api/recordings", presenter)).StatusCode == HttpStatusCode.BadRequest, "Empty recording upload rejected");
Check((await Auth("POST", "/api/recordings", student)).StatusCode == HttpStatusCode.Forbidden, "Learner cannot upload recordings");
await Auth("POST", "/api/leave", presenter); await Auth("POST", "/api/leave", student);

var browserPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
if (!File.Exists(browserPath)) browserPath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
if (!File.Exists(browserPath)) throw new Exception("Chrome or Edge is required for browser smoke tests.");
var profile = Path.Combine(output, "smoke-browser-" + Guid.NewGuid().ToString("N")[..8]);
var info = new ProcessStartInfo(browserPath) { UseShellExecute = false, CreateNoWindow = true };
foreach (var arg in new[] { "--headless=new", "--remote-debugging-port=9223", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--disable-background-timer-throttling", "--disable-renderer-backgrounding", "--disable-backgrounding-occluded-windows", "--use-fake-ui-for-media-stream", "--use-fake-device-for-media-stream", "--autoplay-policy=no-user-gesture-required", "--window-size=1440,1000", "about:blank" }) info.ArgumentList.Add(arg);
using var browser = Process.Start(info) ?? throw new Exception("Browser launch failed");
try
{
    using var debug = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9223") };
    for (var i = 0; ; i++) { try { await debug.GetStringAsync("/json/version"); break; } catch when (i < 30) { await Task.Delay(300); } }
    async Task<Cdp> Page()
    {
        using var response = await debug.PutAsync("/json/new?http://localhost:5080", null);
        var target = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cdp = new Cdp(); await cdp.Connect(target.GetProperty("webSocketDebuggerUrl").GetString()!);
        await cdp.Call("Page.enable", new { });
        await cdp.Wait("!!document.getElementById('join-form') && !!window.LivekitClient");
        return cdp;
    }
    await using var teacher = await Page();
    await teacher.Call("Emulation.setDeviceMetricsOverride", new { width = 1440, height = 1000, deviceScaleFactor = 1, mobile = false });
    await teacher.Eval("document.fonts.ready.then(()=>true)");
    await teacher.Screenshot(Path.Combine(output, "demo-lobby.png"));
    await using var learner = await Page();
    var browserRoom = "browser-" + Guid.NewGuid().ToString("N")[..8];
    await teacher.Eval($"document.getElementById('room-name').value='{browserRoom}';document.getElementById('join-form').requestSubmit();true");
    await teacher.Wait("document.getElementById('connection').textContent === 'متصل به LiveKit'");
    await learner.Eval($"document.getElementById('room-name').value='{browserRoom}';document.querySelector('[value=participant]').click();document.getElementById('join-form').requestSubmit();true");
    await learner.Wait("document.getElementById('connection').textContent === 'متصل به LiveKit'");
    await teacher.Wait("document.querySelectorAll('.person').length===2");
    Check(true, "Two real browser clients joined the same LiveKit room");
    await teacher.Call("Page.bringToFront", new { });
    await teacher.Eval("document.getElementById('record-class').click();true");
    await teacher.Wait("classRecording?.recorder.state==='recording'");
    await Task.Delay(1500);
    await teacher.Eval("stopClassRecording().then(()=>true)");
    var silentUrl = await teacher.Eval("document.getElementById('record-download').getAttribute('href')");
    var silentBytes = await http.GetByteArrayAsync(silentUrl.GetString());
    Check(silentBytes.Length > 1000 && silentBytes[0] == 0x1a, "Recording without microphones or cameras saves nonempty WebM on server");
    await teacher.Eval("recordingNeedsDownload=false;true");
    await teacher.Eval("document.getElementById('chat-input').value='LiveKit data smoke test';document.getElementById('chat-form').requestSubmit();true");
    await learner.Wait("document.getElementById('chat-messages').textContent.includes('LiveKit data smoke test')");
    Check(true, "Reliable LiveKit Data chat delivered between browsers");
    await teacher.Eval("document.getElementById('toggle-camera').click();document.getElementById('toggle-mic').click();true");
    try { await learner.Wait("[...document.querySelectorAll('video')].some(v=>v.readyState>=2) && document.querySelector('#audio-tracks audio')!==null"); }
    catch
    {
        Console.WriteLine("Teacher diagnostics: " + await teacher.Eval("({toast:document.getElementById('toast').textContent,cam:room.localParticipant.isCameraEnabled,mic:room.localParticipant.isMicrophoneEnabled,videos:[...document.querySelectorAll('video')].map(v=>({state:v.readyState,hidden:v.hidden}))})"));
        Console.WriteLine("Learner diagnostics: " + await learner.Eval("({toast:document.getElementById('toast').textContent,videos:[...document.querySelectorAll('video')].map(v=>({state:v.readyState,hidden:v.hidden,w:v.videoWidth})),audio:document.querySelectorAll('audio').length})"));
        await teacher.Screenshot(Path.Combine(output, "failure-teacher.png"));
        await learner.Screenshot(Path.Combine(output, "failure-learner.png"));
        throw;
    }
    Check(true, "LiveKit remote video frames and audio track received");
    await teacher.Wait("[...document.querySelectorAll('#video-grid video')].some(v=>v.readyState>=2 && !v.hidden)");
    Check(true, "Local camera preview visible");
    await learner.Eval("clearInterval(pollTimer);true");
    // Draw using actual browser pointer input rather than calling application internals.
    await teacher.Wait("!document.getElementById('board-status').textContent.includes('در حال دریافت')");
    var bounds = await teacher.Eval("(()=>{const r=document.getElementById('board').getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}})()");
    var x = bounds.GetProperty("x").GetDouble(); var y = bounds.GetProperty("y").GetDouble();
    var w = bounds.GetProperty("w").GetDouble(); var h = bounds.GetProperty("h").GetDouble();
    await teacher.Call("Input.dispatchMouseEvent", new { type = "mousePressed", x = x + w * .2, y = y + h * .4, button = "left", clickCount = 1 });
    for (var i = 0; i < 8; i++) await teacher.Call("Input.dispatchMouseEvent", new { type = "mouseMoved", x = x + w * (.2 + i * .05), y = y + h * (.4 + Math.Sin(i) * .1), button = "left", buttons = 1 });
    await teacher.Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x = x + w * .55, y = y + h * .4, button = "left", clickCount = 1 });
    await learner.Wait("document.getElementById('board-placeholder').hidden && document.getElementById('board-status').textContent.includes('ذخیره شد')");
    await teacher.Wait("document.getElementById('board-status').textContent.includes('نسخه')");
    Check(true, "Teacher pointer drawing synchronized via server LiveKit Data with learner polling disabled");
    Check((await learner.Eval("document.getElementById('toggle-screen').hidden && document.querySelectorAll('[role=tab]').length===0")).GetBoolean(), "Learner follows one stage without content tabs");
    // Substitute only the capture picker; publication/subscription still use real LiveKit media.
    await learner.Eval("document.getElementById('toggle-mic').click();true");
    await teacher.Wait("[...room.remoteParticipants.values()].some(p=>p.audioTrackPublications.size>0)");
    await teacher.Eval("window.__recordText=[];window.__originalFillText=CanvasRenderingContext2D.prototype.fillText;CanvasRenderingContext2D.prototype.fillText=function(text,...args){if(this.canvas!==canvas)window.__recordText.push(String(text));return window.__originalFillText.call(this,text,...args)};startClassRecording().then(()=>true)");
    await teacher.Wait("classRecording?.recorder.state==='recording' && classRecording.sources.size===2");
    await learner.Eval("poll().then(()=>true)");
    Check((await learner.Eval("!document.getElementById('recording-notice').hidden")).GetBoolean(), "Learner sees recording notice; local and remote microphones mixed");
    await teacher.Eval("navigator.mediaDevices.getDisplayMedia=async()=>{const c=document.createElement('canvas');c.width=640;c.height=360;const x=c.getContext('2d');window.__captureTimer=setInterval(()=>{x.fillStyle='#7156ed';x.fillRect(0,0,640,360);x.fillStyle='white';x.fillText(String(Date.now()),20,30)},100);return c.captureStream(10)};document.getElementById('toggle-screen').click();true");
    await learner.Wait("document.getElementById('board-panel').hidden && !document.getElementById('screen-panel').hidden && document.querySelector('#screen-tracks video').readyState>=2");
    await teacher.Wait("!document.getElementById('return-board').hidden");
    Check(true, "Presenter screen replaces board automatically for learner");
    await learner.Eval("window.__screenReload=true");
    await learner.Call("Page.reload", new { });
    await learner.Wait("!window.__screenReload && !!window.LivekitClient && !!document.getElementById('join-form')");
    await learner.Eval($"document.getElementById('room-name').value='{browserRoom}';document.querySelector('[value=participant]').click();document.getElementById('join-form').requestSubmit();true");
    await learner.Wait("!document.getElementById('classroom').hidden && document.getElementById('board-panel').hidden && document.querySelector('#screen-tracks video').readyState>=2");
    Check(true, "Late join follows active presenter screen");
    await teacher.Eval("document.getElementById('return-board').click();true");
    try { await learner.Wait("!document.getElementById('board-panel').hidden && document.getElementById('screen-panel').hidden && document.getElementById('board-placeholder').hidden"); }
    catch {
        Console.WriteLine("Return teacher: " + await teacher.Eval("({sharing:room.localParticipant.isScreenShareEnabled,tracks:document.querySelectorAll('#screen-tracks video').length,toast:document.getElementById('toast').textContent})"));
        Console.WriteLine("Return learner: " + await learner.Eval("({tracks:document.querySelectorAll('#screen-tracks video').length,boardHidden:document.getElementById('board-panel').hidden,strokes:strokes.length})"));
        throw;
    }
    Check(true, "Return to board stops sharing and preserves learner drawing");
    await teacher.Eval("document.getElementById('toggle-screen').click();true");
    await learner.Wait("document.getElementById('board-panel').hidden");
    await teacher.Eval("room.localParticipant.getTrackPublication(LivekitClient.Track.Source.ScreenShare).track.mediaStreamTrack.dispatchEvent(new Event('ended'));true");
    await learner.Wait("!document.getElementById('board-panel').hidden && document.getElementById('screen-panel').hidden");
    await teacher.Eval("clearInterval(window.__captureTimer);true");
    Check(true, "Browser capture ended returns everyone to board");
    await teacher.Eval("stopClassRecording().then(()=>true)");
    Check((await teacher.Eval("recordingNeedsDownload && !document.getElementById('record-download').hidden && !window.__recordText.some(t=>t.includes('LiveKit data smoke test'))")).GetBoolean(), "Recording ready for download and chat excluded from rendered text");
    var recording = await teacher.Eval("(async()=>{const blob=await(await fetch(recordingUrl)).blob();const bytes=new Uint8Array(await blob.arrayBuffer());let binary='';for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));return btoa(binary)})()");
    var recordingBytes = Convert.FromBase64String(recording.GetString()!);
    Check(recordingBytes.Length > 10000 && recordingBytes[0] == 0x1a && recordingBytes[1] == 0x45, "Nonempty WebM recording generated");
    await File.WriteAllBytesAsync(Path.Combine(output, "demo-recording.webm"), recordingBytes);
    var downloadPath = Path.Combine(output, "recording-download-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(downloadPath);
    await teacher.Call("Browser.setDownloadBehavior", new { behavior = "allow", downloadPath });
    await teacher.Eval("document.getElementById('record-download').click();true");
    string? downloadedFile = null;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        downloadedFile = Directory.GetFiles(downloadPath, "*.webm").FirstOrDefault();
        if (downloadedFile is not null && new FileInfo(downloadedFile).Length == recordingBytes.Length) break;
        await Task.Delay(200);
    }
    Check(downloadedFile is not null && (await File.ReadAllBytesAsync(downloadedFile)).SequenceEqual(recordingBytes), "Actual browser download on disk exactly matches nonempty recorded bytes");
    await teacher.Call("Page.bringToFront", new { });
    await teacher.Eval("window.__playback=document.createElement('video');window.__playback.style='position:fixed;inset:0;width:100vw;height:100vh;z-index:9999;background:black';window.__playback.muted=true;window.__playback.src=recordingUrl;document.body.append(window.__playback);window.__playback.play().then(()=>true)");
    await teacher.Wait("window.__playback.videoWidth===1280 && window.__playback.currentTime>0.5");
    Check(true, "Recorded WebM decodes and plays at 1280x720");
    await teacher.Screenshot(Path.Combine(output, "demo-recording-playback.png"));
    var audioEnergy = await teacher.Eval("(async()=>{const a=new AudioContext();try{const b=await a.decodeAudioData(await(await fetch(recordingUrl)).arrayBuffer());return b.getChannelData(0).some(v=>Math.abs(v)>0.001)}finally{await a.close()}})()");
    Check(audioEnergy.GetBoolean(), "Recorded WebM audio decodes with non-silent samples");
    await teacher.Eval("window.__playback.pause();window.__playback.remove();recordingNeedsDownload=false;CanvasRenderingContext2D.prototype.fillText=window.__originalFillText;true");
    await teacher.Screenshot(Path.Combine(output, "demo-classroom.png"));
    await teacher.Call("Emulation.setDeviceMetricsOverride", new { width = 390, height = 844, deviceScaleFactor = 1, mobile = true });
    await teacher.Screenshot(Path.Combine(output, "demo-mobile.png"));
    var overflow = await teacher.Eval("document.documentElement.scrollWidth > window.innerWidth");
    Check(!overflow.GetBoolean(), "Mobile layout has no horizontal overflow");
    // New browser application instance obtains the persisted board after a reload.
    await learner.Eval("window.__smokeOldPage=true");
    await learner.Call("Page.reload", new { });
    await learner.Wait("!window.__smokeOldPage && !!document.getElementById('join-form') && !!window.LivekitClient");
    await learner.Eval($"document.getElementById('room-name').value='{browserRoom}';document.querySelector('[value=participant]').click();document.getElementById('join-form').requestSubmit();true");
    await learner.Wait("document.getElementById('classroom').hidden===false && document.getElementById('board-placeholder').hidden");
    Check(true, "Reload and rejoin recovered persisted whiteboard");
    await teacher.Eval("document.getElementById('leave').click();true");
    await learner.Eval("document.getElementById('leave').click();true");
    Console.WriteLine("Screenshots: .run/demo-lobby.png, demo-classroom.png, demo-mobile.png");
}
finally { if (!browser.HasExited) browser.Kill(true); }

sealed class Cdp : IAsyncDisposable
{
    readonly ClientWebSocket socket = new(); int nextId;
    public Task Connect(string url) => socket.ConnectAsync(new Uri(url), CancellationToken.None);
    public async Task<JsonElement> Call(string method, object parameters)
    {
        var id = ++nextId;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        while (true)
        {
            using var stream = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult response;
            do { response = await socket.ReceiveAsync(buffer, timeout.Token); stream.Write(buffer, 0, response.Count); } while (!response.EndOfMessage);
            using var doc = JsonDocument.Parse(stream.ToArray());
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
            if (root.TryGetProperty("error", out var error)) throw new Exception(error.ToString());
            return root.GetProperty("result").Clone();
        }
    }
    public async Task<JsonElement> Eval(string expression)
    {
        var result = await Call("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true });
        if (result.TryGetProperty("exceptionDetails", out var error)) throw new Exception(error.ToString());
        return result.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
    }
    public async Task Wait(string condition)
    {
        for (var i = 0; i < 60; i++)
        {
            var result = await Eval(condition);
            if (result.ValueKind == JsonValueKind.True) return;
            await Task.Delay(250);
        }
        throw new Exception("Browser condition timed out: " + condition);
    }
    public async Task Screenshot(string path)
    {
        var result = await Call("Page.captureScreenshot", new { format = "png", captureBeyondViewport = false });
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(result.GetProperty("data").GetString()!));
    }
    public ValueTask DisposeAsync() { socket.Dispose(); return ValueTask.CompletedTask; }
}
