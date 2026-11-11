'use strict';
// Render an explicit allowlist of classroom media. Never capture the DOM or desktop.
let classRecording = null, recordingUrl = null, recordingNeedsDownload = false;
let pendingRecording = null;
$('record-download').onclick = async event => {
  if (!$('record-download').hasAttribute('href')) {
    event.preventDefault();
    if (pendingRecording) {
      try { await persistClassRecording(pendingRecording); } catch (error) { toast(error.message); }
    }
    return;
  }
  recordingNeedsDownload = false;
};
const recordButton = $('record-class');

async function persistClassRecording(blob) {
  const link = $('record-download');
  link.removeAttribute('href'); link.hidden = false; link.textContent = 'در حال ذخیره فایل…';
  try {
    const response = await fetch('/api/recordings', { method: 'POST', headers: { Authorization: 'Bearer ' + credentials.access, 'Content-Type': 'video/webm' }, body: blob });
    const result = await response.json();
    if (!response.ok || result.bytes !== blob.size || !result.url) throw new Error(result.error || 'ذخیره کامل فایل تأیید نشد.');
    link.href = result.url; link.download = result.name;
    link.textContent = 'دریافت ضبط (' + Math.ceil(result.bytes / 1024) + ' KB)';
    pendingRecording = null;
  } catch (error) {
    link.textContent = 'تلاش مجدد ذخیره ضبط';
    throw new Error('ذخیره ضبط انجام نشد؛ پنجره را نبندید و «تلاش مجدد» را بزنید. ' + error.message);
  }
}

function fitRecordingImage(context, source, x, y, width, height) {
  const sw = source.videoWidth || source.width, sh = source.videoHeight || source.height;
  if (!sw || !sh) return;
  const scale = Math.min(width / sw, height / sh);
  context.drawImage(source, x + (width - sw * scale) / 2, y + (height - sh * scale) / 2, sw * scale, sh * scale);
}

function paintClassRecording(state) {
  const c = state.context;
  c.fillStyle = '#142138'; c.fillRect(0, 0, 1280, 720);
  c.fillStyle = '#fff'; c.font = '18px Vazirmatn, sans-serif'; c.textAlign = 'right';
  c.fillText('کلاس ' + credentials.room + ' · ' + new Date().toLocaleTimeString('fa-IR'), 1255, 30, 1220);
  c.fillStyle = '#fff'; c.fillRect(12, 48, 956, 660);
  const screen = $('screen-tracks').querySelector('video');
  if (screen) {
    c.fillStyle = '#101a2d'; c.fillRect(12, 48, 956, 660);
    if (screen.readyState >= 2) fitRecordingImage(c, screen, 12, 48, 956, 660);
  } else fitRecordingImage(c, canvas, 12, 48, 956, 660);

  const people = [room.localParticipant, ...room.remoteParticipants.values()];
  const columns = people.length > 6 ? 2 : 1;
  const height = Math.min(160, 650 / Math.max(1, Math.ceil(people.length / columns)));
  const width = 288 / columns;
  people.forEach((person, index) => {
    const x = 980 + (index % columns) * width, y = 48 + Math.floor(index / columns) * height;
    c.fillStyle = '#26354d'; c.fillRect(x + 2, y + 2, width - 4, height - 4);
    const video = document.getElementById('tile-' + person.identity)?.querySelector('video');
    if (person.isCameraEnabled && video?.readyState >= 2) fitRecordingImage(c, video, x + 2, y + 2, width - 4, Math.max(1, height - 28));
    c.fillStyle = '#fff'; c.font = '13px Vazirmatn, sans-serif'; c.textAlign = 'right';
    c.fillText(person.name || 'فراگیر', x + width - 8, y + height - 10, width - 16);
  });
}

function syncRecordingAudio(state) {
  const active = new Set();
  for (const person of [room.localParticipant, ...room.remoteParticipants.values()]) {
    for (const publication of person.audioTrackPublications.values()) {
      const track = publication.track?.mediaStreamTrack;
      if (!track || track.readyState !== 'live') continue;
      active.add(track);
      let entry = state.sources.get(track);
      if (!entry) {
        const source = state.audio.createMediaStreamSource(new MediaStream([track]));
        const gain = state.audio.createGain(); source.connect(gain); gain.connect(state.destination);
        entry = { source, gain }; state.sources.set(track, entry);
      }
      entry.gain.gain.value = publication.isMuted ? 0 : 1;
    }
  }
  for (const [track, entry] of state.sources) {
    if (!active.has(track)) { entry.source.disconnect(); entry.gain.disconnect(); state.sources.delete(track); }
  }
}

async function startClassRecording() {
  if (classRecording || !connected || credentials?.role !== 'presenter') return;
  if (recordingNeedsDownload) throw new Error('ابتدا ضبط قبلی را دریافت کنید.');
  if (!window.MediaRecorder || !HTMLCanvasElement.prototype.captureStream) throw new Error('ضبط در این مرورگر پشتیبانی نمی‌شود؛ از Chrome یا Edge استفاده کنید.');
  const mimeType = ['video/webm;codecs=vp8,opus', 'video/webm'].find(type => MediaRecorder.isTypeSupported(type));
  if (!mimeType) throw new Error('خروجی WebM در این مرورگر پشتیبانی نمی‌شود.');
  const output = document.createElement('canvas'); output.width = 1280; output.height = 720;
  const audio = new AudioContext();
  const state = { canvas: output, context: output.getContext('2d'), audio, destination: audio.createMediaStreamDestination(), sources: new Map(), chunks: [], bytes: 0, started: Date.now() };
  try {
    await audio.resume();
    // Keep the audio clock running even when no microphone has been published.
    state.silence = audio.createConstantSource(); state.silence.offset.value = 0;
    state.silence.connect(state.destination); state.silence.start();
    paintClassRecording(state); syncRecordingAudio(state);
    state.stream = output.captureStream(15);
    state.stream.addTrack(state.destination.stream.getAudioTracks()[0]);
    state.recorder = new MediaRecorder(state.stream, { mimeType, videoBitsPerSecond: 1800000 });
    state.done = new Promise(resolve => { state.resolve = resolve; });
    state.recorder.ondataavailable = event => {
      if (event.data.size) { state.chunks.push(event.data); state.bytes += event.data.size; }
      if (state.bytes > 256 * 1024 * 1024) { toast('ضبط به حد حجم MVP رسید و متوقف شد. فایل را دریافت کنید.'); stopClassRecording(); }
    };
    state.recorder.onerror = event => { state.error = event.error?.message || 'خطای مرورگر در ضبط'; stopClassRecording(); };
    state.recorder.onstop = () => {
      const blob = new Blob(state.chunks, { type: mimeType });
      state.blob = blob;
      if (blob.size < 32) { state.error = 'مرورگر هیچ داده‌ای برای ضبط تولید نکرد؛ فایل خالی قابل دریافت نیست. دوباره ضبط کنید و تب را فعال نگه دارید.'; state.resolve(); return; }
      if (recordingUrl) URL.revokeObjectURL(recordingUrl);
      recordingUrl = URL.createObjectURL(blob);
      recordingNeedsDownload = true;
      pendingRecording = blob;
      state.resolve();
    };
    await api('recording', { method: 'POST', body: JSON.stringify({ active: true }) });
    classRecording = state;
    $('record-download').hidden = true; $('record-download').removeAttribute('href');
    state.recorder.start(1000);
    state.stream.getVideoTracks()[0].requestFrame?.();
    state.timer = setInterval(() => {
      paintClassRecording(state); syncRecordingAudio(state);
      if (Date.now() - state.started > 30 * 60 * 1000) { toast('حد ضبط MVP سی دقیقه است؛ فایل را دریافت کنید.'); stopClassRecording(); }
    }, 1000 / 15);
    recordButton.textContent = 'توقف ضبط'; $('recording-notice').hidden = false;
    room.once(LivekitClient.RoomEvent.Disconnected, stopClassRecording);
    toast('ضبط شروع شد؛ این پنجره را باز و فعال نگه دارید. چت ضبط نمی‌شود.');
  } catch (error) {
    classRecording = null;
    clearInterval(state.timer);
    state.stream?.getVideoTracks().forEach(track => track.stop());
    await audio.close();
    try { await api('recording', { method: 'POST', body: JSON.stringify({ active: false }) }); } catch {}
    throw error;
  }
}

async function stopClassRecording() {
  const state = classRecording;
  if (!state) return;
  if (state.stopping) return state.stopping;
  state.stopping = (async () => {
    clearInterval(state.timer);
    if (state.recorder.state !== 'inactive') state.recorder.stop();
    await state.done;
    state.stream.getTracks().forEach(track => track.stop());
    for (const entry of state.sources.values()) { entry.source.disconnect(); entry.gain.disconnect(); }
    await state.audio.close();
    room.off(LivekitClient.RoomEvent.Disconnected, stopClassRecording);
    try { await api('recording', { method: 'POST', body: JSON.stringify({ active: false }) }); } catch {}
    classRecording = null; recordButton.textContent = 'شروع ضبط'; $('recording-notice').hidden = true;
    if (state.error) toast(state.error);
    if (state.blob.size >= 32) {
      try {
        await persistClassRecording(state.blob);
        toast('فایل روی سرور ذخیره شد؛ «دریافت ضبط» را بزنید.');
      } catch (error) { toast(error.message); }
    }
  })();
  return state.stopping;
}

recordButton.onclick = async () => {
  recordButton.disabled = true;
  try { if (classRecording) await stopClassRecording(); else await startClassRecording(); }
  catch (error) { toast(error.message); }
  finally { recordButton.disabled = false; }
};
