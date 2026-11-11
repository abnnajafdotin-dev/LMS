// Run with Node >=22 while scripts/start.ps1 is running. No npm test packages required.
import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const output = path.join(root, '.run'); await mkdir(output, { recursive: true });
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const check = (condition, message) => { if (!condition) throw Error(message); console.log('PASS: ' + message); };
const chrome = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const browser = spawn(chrome, ['--headless=new', '--remote-debugging-port=9225', '--no-first-run', '--no-default-browser-check', '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows', '--window-size=1440,1000', '--user-data-dir=' + path.join(output, 'whiteboard-regression-' + Date.now()), 'about:blank'], { windowsHide: true, stdio: 'ignore' });
const pages = [];
class Page {
  counter = 0; pending = new Map(); external = []; errors = [];
  async connect() {
    const response = await fetch('http://127.0.0.1:9225/json/new?about:blank', { method: 'PUT' });
    const target = await response.json();
    this.socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { this.socket.addEventListener('open', resolve, { once: true }); this.socket.addEventListener('error', reject, { once: true }); });
    this.socket.addEventListener('message', event => {
      const m = JSON.parse(event.data);
      if (m.id) { const p = this.pending.get(m.id); if (!p) return; clearTimeout(p.timer); this.pending.delete(m.id); m.error ? p.reject(Error(JSON.stringify(m.error))) : p.resolve(m.result); }
      else if (m.method === 'Runtime.exceptionThrown') this.errors.push(m.params.exceptionDetails);
      else if (m.method === 'Fetch.requestPaused') {
        const { request, requestId } = m.params;
        const local = /^(https?|wss?):\/\/(localhost|127\.0\.0\.1)(:|\/)/.test(request.url) || request.url.startsWith('data:');
        if (!local) this.external.push(request.url);
        this.call(local ? 'Fetch.continueRequest' : 'Fetch.failRequest', local ? { requestId } : { requestId, errorReason: 'BlockedByClient' }).catch(() => {});
      }
    });
    await this.call('Runtime.enable'); await this.call('Page.enable');
    await this.call('Fetch.enable', { patterns: [{ urlPattern: '*' }] });
    await this.call('Page.addScriptToEvaluateOnNewDocument', { source: `
      window.testNetwork={delayPut:0,delayGet:0,failPut:0,failHeartbeat:false,active:0,maxActive:0,puts:0};
      const realFetch=window.fetch.bind(window);
      window.fetch=async(input,init)=>{
        const url=typeof input==='string'?input:input.url, method=init?.method||'GET', n=window.testNetwork;
        if(url==='/api/heartbeat'&&n.failHeartbeat)throw new TypeError('Test backend offline');
        if(url==='/api/board'&&method==='PUT'){
          n.puts++;n.active++;n.maxActive=Math.max(n.maxActive,n.active);
          const status=n.failPut;
          try { if(n.delayPut)await new Promise(r=>setTimeout(r,n.delayPut));
            if(status===1)throw new TypeError('Test network failure');
            if(status)return new Response(JSON.stringify({error:'Test rejection'}),{status,headers:{'Content-Type':'application/json'}});
            return await realFetch(input,init);
          }finally{n.active--;}
        }
        const result=await realFetch(input,init);
        if(url==='/api/board'&&method==='GET'&&n.delayGet) {await new Promise(r=>setTimeout(r,n.delayGet));}
        return result;
      };
    ` });
    await this.call('Page.navigate', { url: 'http://localhost:5080' });
    await this.wait("!!document.getElementById('join-form') && !!window.LivekitClient");
    pages.push(this); return this;
  }
  call(method, params = {}) {
    const id = ++this.counter;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(Error('CDP timed out: ' + method)); }, 20000);
      this.pending.set(id, { resolve, reject, timer }); this.socket.send(JSON.stringify({ id, method, params }));
    });
  }
  async eval(expression) {
    const result = await this.call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  }
  async wait(expression) {
    for (let i = 0; i < 100; i++) { if (await this.eval(expression)) return; await sleep(200); }
    await this.screenshot('whiteboard-regression-failure.png');
    throw Error('Condition timed out: ' + expression + '\n' + JSON.stringify(await this.eval("({error:document.getElementById('join-error')?.textContent,status:document.getElementById('board-status')?.textContent,dirty,saving,revision,scene:boardScene})")));
  }
  async join(room, role) {
    await this.eval(`document.getElementById('room-name').value=${JSON.stringify(room)};document.querySelector('[value=${role}]').click();document.getElementById('join-form').requestSubmit();true`);
    await this.wait('boardLoaded && connected');
  }
  async click(x, y, count = 1) {
    await this.call('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: count });
    await this.call('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: count });
  }
  async bounds() { return this.eval("(()=>{const r=document.getElementById('board').getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}})()"); }
  async draw(tool, offset = 0) {
    await this.call('Page.bringToFront');
    await this.eval(`document.querySelector('[data-testid=toolbar-${tool}]').click();true`);
    const r = await this.bounds(), x = r.x + r.w * .43, y = r.y + r.h * (.43 + offset);
    await this.call('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
    for (let i = 1; i <= 5; i++) await this.call('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x + i * 25, y: y + i * 12, button: 'left', buttons: 1 });
    await this.call('Input.dispatchMouseEvent', { type: 'mouseReleased', x: x + 125, y: y + 60, button: 'left', buttons: 0, clickCount: 1 });
    await sleep(100);
  }
  async text(value) {
    await this.eval("document.querySelector('[data-testid=toolbar-text]').click();true");
    const r = await this.bounds(); await this.click(r.x + r.w * .6, r.y + r.h * .7);
    await this.wait("!!document.querySelector('textarea.excalidraw-wysiwyg')");
    await this.call('Input.insertText', { text: value }); await this.key('Escape', 'Escape', 27);
    await sleep(100);
  }
  async key(key, code, windowsVirtualKeyCode, modifiers = 0) {
    await this.call('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode, modifiers });
    await this.call('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode, modifiers });
  }
  async settled() { await this.wait('!dirty && !saving && revision>0'); }
  async screenshot(name) { const r = await this.call('Page.captureScreenshot', { format: 'png' }); await writeFile(path.join(output, name), Buffer.from(r.data, 'base64')); }
  close() { for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(Error('Closed')); } this.socket.close(); }
}
try {
  for (let i = 0; i < 40; i++) { try { await fetch('http://127.0.0.1:9225/json/version'); break; } catch {} await sleep(250); }
  const teacher = await new Page().connect(), learner = await new Page().connect();
  const room = 'regression-' + Date.now(); await teacher.join(room, 'presenter'); await learner.join(room, 'participant');
  check(!await learner.eval("!!document.querySelector('[data-testid=toolbar-rectangle]')"), 'Learner has no editing toolbar');
  await teacher.draw('rectangle'); await teacher.settled();
  await teacher.text('سلام؛ تخته کلاس فارسی'); await teacher.settled();
  await teacher.draw('arrow', .1); await teacher.settled();
  await teacher.draw('freedraw', .15); await teacher.settled();
  await learner.wait("['rectangle','text','arrow','freedraw'].every(t=>boardScene.elements.some(e=>e.type===t&&!e.isDeleted))");
  check(true, 'Persian text, shape, arrow and freehand arrive at learner');
  const revision = await teacher.eval('revision');
  await teacher.key('z', 'KeyZ', 90, 2); await teacher.wait(`revision>${revision}`); await teacher.settled();
  check(await teacher.eval("boardScene.elements.some(e=>e.type==='freedraw'&&e.isDeleted)"), 'Undo persists a deletion');
  const undone = await teacher.eval('revision');
  await teacher.key('z', 'KeyZ', 90, 10); await teacher.wait(`revision>${undone}`); await teacher.settled();
  check(await teacher.eval("boardScene.elements.some(e=>e.type==='freedraw'&&!e.isDeleted)"), 'Redo restores freehand');
  const stable = await teacher.eval('revision');
  await teacher.eval("document.querySelector('[data-testid=toolbar-selection]').click();true");
  await teacher.click(600, 650); await teacher.call('Input.dispatchMouseEvent', { type: 'mouseWheel', x: 650, y: 550, deltaY: 120, deltaX: 0 });
  await sleep(1200); check(await teacher.eval('revision') === stable, 'Selection and viewport do not save');
  const studentBefore = await learner.eval('testNetwork.puts'); await learner.eval('syncBoard().then(()=>true)'); await sleep(600);
  check(await learner.eval('testNetwork.puts') === studentBefore, 'Remote snapshot never echoes PUT');
  await teacher.eval('testNetwork.delayPut=1600;true');
  await teacher.draw('ellipse'); await teacher.wait('saving'); await teacher.draw('diamond', -.1);
  await teacher.settled(); check(await teacher.eval('testNetwork.maxActive') === 1, 'Slow saves remain serialized');
  await learner.wait("boardScene.elements.some(e=>e.type==='diamond')&&boardScene.elements.some(e=>e.type==='ellipse')");
  await teacher.eval('testNetwork.delayPut=0;testNetwork.delayGet=1800;window.testGet=syncBoard();true');
  await teacher.wait('fetching'); await teacher.draw('rectangle', -.1); await teacher.settled();
  const newest = await teacher.eval('revision'); await teacher.eval('window.testGet.then(()=>true)');
  check(await teacher.eval('revision') === newest, 'Late GET cannot overwrite newer saved scene');
  await teacher.eval('testNetwork.delayGet=0;testNetwork.failPut=413;true');
  await teacher.draw('rectangle', .1); await teacher.wait('saveBlocked && dirty');
  const rejectedPuts = await teacher.eval('testNetwork.puts'); await sleep(1300);
  check(await teacher.eval('testNetwork.puts') === rejectedPuts, '413 preserves local scene without an automatic retry loop');
  await teacher.eval('testNetwork.failPut=0;true'); await teacher.draw('ellipse', .1); await teacher.settled();
  await teacher.eval('testNetwork.failPut=1;testNetwork.failHeartbeat=true;true');
  await teacher.draw('rectangle', -.1); await teacher.wait('!backendReady && dirty');
  check(!await teacher.eval("!!document.querySelector('[data-testid=toolbar-rectangle]')"), 'Backend failure freezes editing while preserving pending content');
  await teacher.eval('testNetwork.failPut=0;testNetwork.failHeartbeat=false;poll().then(()=>true)'); await teacher.settled();
  check(true, 'Pending scene saves after backend recovers');
  // Advance authoritative revision from a separate HTTP request using the same presenter session.
  const beforeConflict = await teacher.eval('revision');
  await teacher.eval("fetching=true;api('board',{method:'PUT',body:JSON.stringify({schemaVersion:2,engine:'excalidraw',baseRevision:revision,scene:boardScene})}).then(()=>true)");
  await teacher.draw('diamond', -.1); await teacher.wait(`revision>${beforeConflict} && !saving && !dirty`);
  check(await teacher.eval("!document.getElementById('download-unsaved').hidden"), '409 retains a downloadable local copy');
  await teacher.eval('fetching=false;true');
  await teacher.eval("boardAdapter.resize();true"); await teacher.screenshot('excalidraw-classroom.png');
  await learner.call('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true }); await sleep(500);
  check(!await learner.eval('document.documentElement.scrollWidth>innerWidth'), 'Learner mobile layout has no horizontal overflow');
  await learner.screenshot('excalidraw-learner-mobile.png');
  await teacher.eval('window.testNetwork.failPut=403;true'); await teacher.draw('rectangle', -.1); await teacher.wait('authorityLost && dirty');
  check(!await teacher.eval("!!document.querySelector('[data-testid=toolbar-rectangle]')"), '403 freezes editing and retains unsaved changes');
  for (const page of pages) check(page.external.length === 0 && page.errors.length === 0, 'No external asset requests or uncaught browser errors');
  await writeFile(path.join(output, 'whiteboard-browser-result.json'), JSON.stringify(pages.map(p => ({ external: p.external, errors: p.errors })), null, 2));
  // Release test sessions explicitly; browser closes below even with intentionally unsaved edits.
  await teacher.eval("api('leave',{method:'POST'}).then(()=>true)"); await learner.eval("api('leave',{method:'POST'}).then(()=>true)");
  console.log('Whiteboard browser regression complete.');
} finally { for (const page of pages) page.close(); browser.kill(); }
