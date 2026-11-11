'use strict';
const $ = id => document.getElementById(id);
let room, credentials, connected = false, backendReady = true, strokes = [], revision = 0;
let drawing = false, dirty = false, saving = false, fetching = false, activeStroke;
let color = '#243451', pollTimer, clockTimer, startedAt, toastTimer, saveTimer;
const canvas = $('board'), ctx = canvas.getContext('2d');
const encoder = new TextEncoder(), decoder = new TextDecoder();
const invite = new URLSearchParams(location.search);
if (/^[a-zA-Z0-9-]{1,40}$/.test(invite.get('room') || '')) $('room-name').value = invite.get('room');
if (invite.has('room')) { document.querySelector('[value="participant"]').checked = true; $('name').value = 'فراگیر دمو'; }

function roleSelection() {
  const presenter = document.querySelector('[name="role"]:checked').value === 'presenter';
  document.querySelectorAll('.role').forEach(el => el.classList.toggle('selected', el.querySelector('input').checked));
  $('code-field').hidden = !presenter;
}
document.querySelectorAll('[name="role"]').forEach(el => el.addEventListener('change', () => {
  $('name').value = el.value === 'presenter' ? 'مدرس دمو' : 'فراگیر دمو'; roleSelection();
}));
roleSelection();
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true, 5000); }
function boardStatus(message, unsaved = false) { $('board-status').textContent = message; $('board-status').classList.toggle('unsaved', unsaved); }
function connectionStatus(text, state) { $('connection').textContent = text; $('connection').className = 'status ' + state; }
async function api(path, options = {}) {
  const response = await fetch('/api/' + path, { ...options, headers: { 'Content-Type': 'application/json', ...(credentials ? { Authorization: 'Bearer ' + credentials.access } : {}), ...options.headers } });
  let data = null; try { data = await response.json(); } catch { /* Empty HTTP response. */ }
  if (!response.ok) { const error = new Error(data?.error || (response.status === 403 ? 'اجازه انجام این عملیات را ندارید.' : 'ارتباط با سرور برقرار نشد.')); error.status = response.status; throw error; }
  return data;
}
async function health() {
  try { const data = await api('health'); $('server-status').textContent = data.livekit ? 'سرورها آماده‌اند' : 'LiveKit در دسترس نیست'; $('server-status').className = 'status ' + (data.livekit ? 'ok' : 'warn'); }
  catch { $('server-status').textContent = 'بک‌اند در دسترس نیست'; $('server-status').className = 'status bad'; }
}
health();

$('join-form').addEventListener('submit', async event => {
  event.preventDefault(); $('join-error').textContent = ''; $('join-button').disabled = true;
  try {
    if (!window.LivekitClient) throw new Error('فایل SDK مرورگر بارگذاری نشده است؛ اسکریپت setup را اجرا کنید.');
    credentials = await api('join', { method: 'POST', body: JSON.stringify({ name: $('name').value.trim(), room: $('room-name').value.trim(), role: document.querySelector('[name="role"]:checked').value, code: $('presenter-code').value }) });
    const { Room, RoomEvent, Track } = LivekitClient;
    room = new Room({ adaptiveStream: true, dynacast: true, videoCaptureDefaults: { resolution: { width: 640, height: 360, frameRate: 20 } } });
    room.on(RoomEvent.TrackSubscribed, (track, publication, participant) => attachTrack(track, participant));
    room.on(RoomEvent.TrackUnsubscribed, detachTrack);
    room.on(RoomEvent.TrackUnpublished, removePublication);
    room.on(RoomEvent.LocalTrackPublished, publication => { if (publication.track) attachTrack(publication.track, room.localParticipant); renderPeople(); updateControls(); });
    room.on(RoomEvent.LocalTrackUnpublished, publication => {
      removePublication(publication);
      updateStage(); renderPeople(); updateControls();
    });
    for (const event of [RoomEvent.ParticipantConnected, RoomEvent.ParticipantDisconnected, RoomEvent.TrackMuted, RoomEvent.TrackUnmuted]) room.on(event, () => { renderPeople(); updateStage(); });
    room.on(RoomEvent.DataReceived, (payload, participant, kind, topic) => {
      // Server-origin packets have no participant. Never trust client board notifications.
      if (topic === 'board.changed' && !participant) { syncBoard(); return; }
      if (topic !== 'chat' || !participant || payload.length > 3000) return;
      try { const data = JSON.parse(decoder.decode(payload)); if (typeof data.text === 'string') addMessage(participant.name || 'فراگیر', data.text.slice(0, 500), false); } catch { /* Ignore malformed packets. */ }
    });
    room.on(RoomEvent.Reconnecting, () => { connected = false; connectionStatus('در حال اتصال مجدد…', 'warn'); boardStatus('اتصال قطع است؛ رسم موقتاً متوقف شد', true); });
    room.on(RoomEvent.Reconnected, async () => { connected = true; connectionStatus('متصل به LiveKit', 'ok'); await syncBoard(); toast('اتصال بازیابی شد'); });
    room.on(RoomEvent.Disconnected, () => { connected = false; connectionStatus('اتصال قطع شد', 'bad'); boardStatus('برای اتصال دوباره، خارج و وارد کلاس شوید', true); });
    room.on(RoomEvent.AudioPlaybackStatusChanged, () => $('start-audio').hidden = room.canPlaybackAudio);
    await room.connect(credentials.url, credentials.token);
    connected = true;
    $('lobby').hidden = true; $('classroom').hidden = false;
    $('room-label').textContent = credentials.room;
    $('self-name').textContent = credentials.name;
    $('self-avatar').textContent = credentials.name[0];
    const presenter = credentials.role === 'presenter';
    $('self-role').textContent = presenter ? 'مدرس کلاس' : 'فراگیر';
    $('role-badge').textContent = presenter ? 'مدرس' : 'فراگیر';
    $('record-class').hidden = !presenter;
    $('toggle-screen').hidden = !presenter; $('drawing-tools').hidden = !presenter; $('readonly-note').hidden = presenter;
    canvas.classList.toggle('readonly', !presenter);
    $('board-role').textContent = presenter ? 'قلم در اختیار شماست' : 'مشاهده تخته مدرس';
    connectionStatus('متصل به LiveKit', 'ok'); renderPeople(); updateControls();
    await syncBoard();
    pollTimer = setInterval(poll, 3000);
    startedAt = Date.now(); clockTimer = setInterval(() => { const s = Math.floor((Date.now() - startedAt) / 1000); $('clock').textContent = `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`; }, 1000);
  } catch (error) {
    $('join-error').textContent = error.message;
    await room?.disconnect();
    if (credentials) { try { await api('leave', { method: 'POST' }); } catch {} }
    credentials = null; connected = false; $('lobby').hidden = false; $('classroom').hidden = true;
  } finally { $('join-button').disabled = false; }
});

function attachTrack(track, participant) {
  const { Track } = LivekitClient;
  if (track.kind === Track.Kind.Audio && participant === room.localParticipant) return;
  if (track.kind === Track.Kind.Video && track.source !== Track.Source.ScreenShare) { renderPeople(); return; }
  const element = track.attach();
  element.dataset.trackSid = track.sid;
  if (track.kind === Track.Kind.Audio) { $('audio-tracks').append(element); return; }
  element.autoplay = true; element.playsInline = true;
  if (participant === room.localParticipant) { element.muted = true; element.classList.add('local'); }
  if (track.source === Track.Source.ScreenShare) { element.classList.remove('local'); $('screen-tracks').append(element); updateStage(); }
}
function renderPeople() {
  if (!room) return;
  const people = [room.localParticipant, ...room.remoteParticipants.values()];
  $('people-count').textContent = people.length.toLocaleString('fa-IR');
  const activeIds = new Set(people.map(p => 'tile-' + p.identity));
  [...$('video-grid').children].forEach(el => { if (!activeIds.has(el.id)) el.remove(); });
  $('participant-list').replaceChildren();
  for (const person of people) {
    let tile = document.getElementById('tile-' + person.identity);
    if (!tile) {
      tile = document.createElement('div'); tile.id = 'tile-' + person.identity; tile.className = 'video-tile';
      const avatar = document.createElement('span'); avatar.className = 'video-avatar'; avatar.textContent = (person.name || '؟')[0];
      const name = document.createElement('span'); name.className = 'video-name'; name.textContent = (person.name || 'فراگیر') + (person === room.localParticipant ? ' (شما)' : '');
      tile.append(avatar, name); $('video-grid').append(tile);
    }
    const row = document.createElement('div'); row.className = 'person';
    const avatar = document.createElement('span'); avatar.className = 'avatar'; avatar.textContent = (person.name || '؟')[0];
    const name = document.createElement('span'); name.textContent = person.name || 'فراگیر';
    const tag = document.createElement('small'); tag.textContent = person === room.localParticipant ? 'شما' : 'متصل';
    const mic = document.createElement('span'); mic.className = 'mic-indicator' + (person.isMicrophoneEnabled ? ' on' : ''); mic.textContent = '♩';
    row.append(avatar, name, tag, mic); $('participant-list').append(row);
  }
  // Muted/unpublished video must not leave a stale image on a participant tile.
  for (const person of people) {
    const tile = document.getElementById('tile-' + person.identity);
    const publication = person.getTrackPublication(LivekitClient.Track.Source.Camera);
    if (publication?.track && !tile.querySelector('video')) {
      const video = publication.track.attach(); video.autoplay = true; video.playsInline = true;
      if (person === room.localParticipant) { video.muted = true; video.classList.add('local'); }
      tile.append(video);
    }
    tile.querySelectorAll('video').forEach(el => el.hidden = !person.isCameraEnabled);
  }
}
function updateControls() {
  if (!room) return;
  for (const [id, on] of [['toggle-mic', room.localParticipant.isMicrophoneEnabled], ['toggle-camera', room.localParticipant.isCameraEnabled], ['toggle-screen', room.localParticipant.isScreenShareEnabled]]) {
    $(id).classList.toggle('enabled', on); $(id).setAttribute('aria-pressed', String(on));
  }
}
async function mediaAction(id, action) {
  if (!connected) { toast('ابتدا اتصال کلاس را برقرار کنید.'); return; }
  $(id).disabled = true;
  try { await action(); updateControls(); renderPeople(); }
  catch (error) { toast('فعال‌سازی انجام نشد. مجوز مرورگر و دسترسی دستگاه را بررسی کنید. ' + error.message); }
  finally { $(id).disabled = false; }
}
$('toggle-mic').onclick = () => mediaAction('toggle-mic', () => room.localParticipant.setMicrophoneEnabled(!room.localParticipant.isMicrophoneEnabled));
$('toggle-camera').onclick = () => mediaAction('toggle-camera', () => room.localParticipant.setCameraEnabled(!room.localParticipant.isCameraEnabled));
$('toggle-screen').onclick = () => mediaAction('toggle-screen', () => room.localParticipant.setScreenShareEnabled(!room.localParticipant.isScreenShareEnabled));
$('start-audio').onclick = () => room.startAudio().catch(() => toast('اجازه پخش صدا داده نشد.'));
function detachTrack(track) {
  track.detach().forEach(el => el.remove());
  removeTrackElements(track.sid);
  updateStage();
}
function removeTrackElements(sid) {
  // SDK cleanup can detach media before its event reaches us, leaving DOM nodes behind.
  document.querySelectorAll('[data-track-sid]').forEach(el => { if (el.dataset.trackSid === sid) el.remove(); });
}
function removePublication(publication) {
  if (publication.track) publication.track.detach().forEach(el => el.remove());
  removeTrackElements(publication.trackSid);
  updateStage();
}
function updateStage() {
  const sharing = $('screen-tracks').querySelector('video') !== null;
  $('board-panel').hidden = sharing; $('screen-panel').hidden = !sharing;
  $('stage-title').textContent = sharing ? '▣ صفحه مدرس' : '✎ تخته کلاس';
  $('return-board').hidden = !sharing || credentials?.role !== 'presenter';
  $('board-status').hidden = sharing;
}
$('return-board').onclick = () => mediaAction('return-board', () => room.localParticipant.setScreenShareEnabled(false));
$('copy-invite').onclick = async () => {
  const url = new URL(location.origin); url.searchParams.set('room', credentials.room);
  try { await navigator.clipboard.writeText(url.href); toast('لینک دعوت کپی شد؛ آن را در یک پنجره دیگر باز کنید.'); }
  catch { toast(url.href); }
};

function addMessage(name, text, mine) {
  $('chat-messages').querySelector('.chat-welcome')?.remove();
  const message = document.createElement('div'); message.className = 'message' + (mine ? ' mine' : '');
  const head = document.createElement('div'); head.className = 'message-head';
  const author = document.createElement('b'); author.textContent = name;
  const time = document.createElement('span'); time.textContent = new Date().toLocaleTimeString('fa-IR', { hour: '2-digit', minute: '2-digit' });
  const p = document.createElement('p'); p.textContent = text;
  head.append(author, time); message.append(head, p); $('chat-messages').append(message);
  while ($('chat-messages').children.length > 100) $('chat-messages').firstElementChild.remove();
  $('chat-messages').scrollTop = $('chat-messages').scrollHeight;
}
$('chat-form').onsubmit = async event => {
  event.preventDefault(); const text = $('chat-input').value.trim(); if (!text) return;
  if (!connected) { toast('ارتباط کلاس قطع است.'); return; }
  try { await room.localParticipant.publishData(encoder.encode(JSON.stringify({ text })), { reliable: true, topic: 'chat' }); addMessage(credentials.name, text, true); $('chat-input').value = ''; }
  catch { toast('ارسال پیام انجام نشد.'); }
};

function renderBoard() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.lineCap = 'round'; ctx.lineJoin = 'round';
  for (const stroke of strokes) {
    if (!stroke.points?.length) continue;
    ctx.strokeStyle = stroke.color; ctx.fillStyle = stroke.color; ctx.lineWidth = stroke.width;
    ctx.beginPath();
    stroke.points.forEach(([x, y], i) => { if (i === 0) ctx.moveTo(x * canvas.width, y * canvas.height); else ctx.lineTo(x * canvas.width, y * canvas.height); });
    if (stroke.points.length === 1) { ctx.arc(stroke.points[0][0] * canvas.width, stroke.points[0][1] * canvas.height, stroke.width / 2, 0, 2 * Math.PI); ctx.fill(); } else ctx.stroke();
  }
  $('board-placeholder').hidden = strokes.length > 0;
}
function point(event) {
  const rect = canvas.getBoundingClientRect();
  return [Number(Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)).toFixed(4)), Number(Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height)).toFixed(4))];
}
canvas.addEventListener('pointerdown', event => {
  if (!credentials || credentials.role !== 'presenter' || !connected || !backendReady || fetching || event.button !== 0) return;
  if (strokes.length >= 300) { toast('حد دمو ۳۰۰ خط است. تخته را پاک کنید.'); return; }
  drawing = true; activeStroke = { color, width: Number($('pen-width').value), points: [point(event)] };
  strokes.push(activeStroke); canvas.setPointerCapture(event.pointerId); changed(); renderBoard();
});
canvas.addEventListener('pointermove', event => {
  if (!drawing) return;
  if (!connected) { finishStroke(); return; }
  const next = point(event), prev = activeStroke.points.at(-1);
  if (activeStroke.points.length < 1000 && Math.hypot(next[0] - prev[0], next[1] - prev[1]) > .001) { activeStroke.points.push(next); changed(); renderBoard(); }
});
function finishStroke() { if (!drawing) return; drawing = false; activeStroke = null; changed(); }
canvas.addEventListener('pointerup', finishStroke); canvas.addEventListener('pointercancel', finishStroke); canvas.addEventListener('lostpointercapture', finishStroke);
document.querySelectorAll('.color').forEach(el => el.onclick = () => { color = el.dataset.color; document.querySelectorAll('.color').forEach(c => c.classList.toggle('selected', c === el)); });
$('undo').onclick = () => { if (!connected || drawing) return; strokes.pop(); renderBoard(); changed(); };
$('clear-board').onclick = () => { if (!connected || drawing) return; strokes = []; renderBoard(); changed(); };
function changed() { dirty = true; boardStatus('در حال همگام‌سازی…', true); if (!saveTimer) saveTimer = setTimeout(() => { saveTimer = null; saveBoard(); }, 250); }
async function saveBoard() {
  if (saving || !dirty || !connected) return;
  saving = true; dirty = false;
  const sent = structuredClone(strokes);
  try {
    const result = await api('board', { method: 'PUT', body: JSON.stringify({ baseRevision: revision, strokes: sent }) });
    revision = result.revision;
    if (!dirty) boardStatus(result.notified ? `ذخیره شد · نسخه ${revision.toLocaleString('fa-IR')}` : 'ذخیره شد · بازیابی با polling');
  } catch (error) {
    connected = error.status === 403 ? false : connected;
    drawing = false; activeStroke = null; dirty = false;
    toast(error.status === 409 ? 'وضعیت سرور تغییر کرده است؛ تخته دوباره دریافت شد.' : error.message);
    boardStatus('تغییر تأیید نشد؛ در حال بازیابی…', true);
    if (error.status === 403) connectionStatus('مجوز مدرس منقضی شد؛ دوباره وارد شوید', 'bad');
    try { const snapshot = await api('board'); strokes = snapshot.strokes; revision = snapshot.revision; renderBoard(); boardStatus('آخرین وضعیت ذخیره‌شده بازیابی شد'); }
    catch { boardStatus('ذخیره تأیید نشد؛ اتصال را بررسی کنید', true); }
  } finally { saving = false; if (dirty) saveBoard(); }
}
async function syncBoard() {
  if (!credentials || fetching || saving || dirty || drawing) return;
  fetching = true;
  try { const snapshot = await api('board'); if (snapshot.revision >= revision) { strokes = snapshot.strokes; revision = snapshot.revision; renderBoard(); boardStatus(`ذخیره شد · نسخه ${revision.toLocaleString('fa-IR')}`); } }
  catch { boardStatus('دریافت تخته ناموفق؛ تلاش مجدد…', true); }
  finally { fetching = false; }
}
async function poll() {
  if (!credentials) return;
  try { const data = await api('heartbeat', { method: 'POST' }); $('recording-notice').hidden = !data.recording; const wasReady = backendReady; backendReady = true; if (data.revision !== revision || !wasReady) await syncBoard(); if (dirty && connected) saveBoard(); }
  catch (error) { backendReady = false; boardStatus('بک‌اند در دسترس نیست؛ رسم متوقف شد', true); if (error.status === 403) { connected = false; connectionStatus('مجوز مدرس تغییر کرده است', 'bad'); } }
}
$('leave').onclick = async () => {
  $('leave').disabled = true;
  await stopClassRecording();
  if (recordingNeedsDownload) { toast('ابتدا فایل ضبط را دریافت کنید، سپس خارج شوید.'); $('leave').disabled = false; return; }
  finishStroke(); clearTimeout(saveTimer); saveTimer = null;
  await saveBoard();
  // A previous save may still be in flight; do not claim pending strokes were saved.
  if (saving || dirty) { toast('همگام‌سازی در حال انجام است؛ چند لحظه دیگر خارج شوید.'); $('leave').disabled = false; return; }
  clearInterval(pollTimer); clearInterval(clockTimer);
  try { await api('leave', { method: 'POST' }); } catch {}
  await room.disconnect(); location.href = location.pathname;
};
document.addEventListener('visibilitychange', () => { if (!document.hidden && credentials) { poll(); syncBoard(); } });
window.addEventListener('beforeunload', event => { if (dirty || saving || classRecording || recordingNeedsDownload) { event.preventDefault(); event.returnValue = ''; } });
