// 웹 속도 측정용 Windows Chrome CDP 다리 — 자기 프로필로 chrome.exe 를 띄우고 한 줄 JSON 명령(이동·평가·추적·준비 대기·스크린샷)을 처리한다(otest-w-6 회차 도구)
// 사용: node.exe pdchrome.mjs <디버그 포트> <프로필 폴더(Windows)> <창 x> <창 y> <창 w> <창 h>
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import fs from 'node:fs';

const [, , PORT = '9350', PROFILE, WX = '-2500', WY = '40', WW = '1440', WH = '940'] = process.argv;
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const out = o => console.log(JSON.stringify(o));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const now = () => performance.now();

const chrome = spawn(CHROME, [`--remote-debugging-port=${PORT}`, `--user-data-dir=${PROFILE}`, '--no-first-run', '--no-default-browser-check',
  '--disable-features=Translate,OptimizationHints', `--window-position=${WX},${WY}`, `--window-size=${WW},${WH}`, '--new-window', ...(process.env.PDCHROME_HEADLESS ? ['--headless=new'] : []), 'about:blank'],
  { detached: false, stdio: 'ignore' });
let ver = null;
for (let i = 0; i < 100 && !ver; i++) { try { ver = await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json(); } catch { await sleep(200); } }
if (!ver) { out({ ok: false, error: 'no-cdp' }); process.exit(2); }
const ws = new WebSocket(ver.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0; const pend = new Map(); const listeners = [];
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); return; }
  for (const l of listeners) l(m);
};
const send = (method, params = {}, sessionId) => new Promise(r => { const i = ++id; pend.set(i, r); ws.send(JSON.stringify(sessionId ? { id: i, method, params, sessionId } : { id: i, method, params })); });
let pageT = null;
for (let i = 0; i < 50 && !pageT; i++) { pageT = (await send('Target.getTargets')).result.targetInfos.find(t => t.type === 'page'); if (!pageT) await sleep(200); }
const SID = (await send('Target.attachToTarget', { targetId: pageT.targetId, flatten: true })).result.sessionId;
const ps = (method, params) => send(method, params, SID);
await ps('Page.enable'); await ps('Runtime.enable'); await ps('Inspector.enable');
const consoleErr = [];
// otest-w-7: 렌더러 중단·페이지 이동 감지(웹 조작 1차 실패 원인 확인용)
listeners.push(m => { if (m.sessionId === SID && m.method === 'Inspector.targetCrashed') consoleErr.push('RENDERER_CRASHED ' + Math.round(now())); });
listeners.push(m => { if (m.sessionId === SID && m.method === 'Page.frameNavigated' && !m.params.frame.parentId) consoleErr.push('NAVIGATED ' + m.params.frame.url + ' ' + Math.round(now())); });
listeners.push(m => { if (m.sessionId === SID && m.method === 'Runtime.executionContextsCleared') consoleErr.push('CONTEXTS_CLEARED ' + Math.round(now())); });
listeners.push(m => { if (m.sessionId === SID && m.method === 'Runtime.exceptionThrown') consoleErr.push(m.params.exceptionDetails?.exception?.description?.slice(0, 200) || 'exception'); });
listeners.push(m => { if (m.sessionId === SID && m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') consoleErr.push(String(m.params.args?.[0]?.value ?? '').slice(0, 200)); });

const ev = async (expression, awaitPromise = true) => {
  const r = await ps('Runtime.evaluate', { expression, awaitPromise, returnByValue: true });
  if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text);
  return r.result?.result?.value;
};
const shotSize = async (scale = 0.25) => {
  const lm = await ps('Page.getLayoutMetrics');
  const v = lm.result.cssLayoutViewport;
  const r = await ps('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: v.clientWidth, height: v.clientHeight, scale } });
  return r.result?.data?.length || 0;
};

// 준비 대기: 이동 직후부터 캔버스 생성·로딩 문구 제거·화면 그려짐(스크린샷 크기 급증)·안정까지 시각(ms)
async function navReady(url, timeoutMs = 120000, needAuto = false) {
  const t0 = now();
  await ps('Page.navigate', { url });
  let base = 0, tCanvas = null, tPaint = null, tSettle = null, tAuto = null, last = 0, stable = 0;
  const shots = [];
  while (now() - t0 < timeoutMs) {
    let st = null;
    try { st = await ev("({h: location.href, c: !!document.querySelector('#out canvas'), l: !!document.getElementById('pd-loading'), a: !!(window.pdAutomation && window.pdAutomation.invoke)})", false); } catch { }
    if (st && st.h !== 'about:blank') {
      // otest-w-7: 기준 화면은 로딩 문구가 보일 때만 잡는다(웜에서 첫 표본이 이미 그려진 화면이면 tPaint 가 영영 안 잡혀 120s 헛대기)
      if (!base && st.l && !st.c) { try { base = await shotSize(); } catch { } }
      if (st.a && tAuto === null) tAuto = now() - t0;
      if (st.c && !st.l && tCanvas === null) tCanvas = now() - t0;
      if (tCanvas !== null) {
        const s = await shotSize(); shots.push([Math.round(now() - t0), s]);
        if (tPaint === null && s > Math.max(base * 3, base + 6000)) tPaint = now() - t0;
        if (tPaint !== null) { if (last && Math.abs(s - last) / last < 0.02) { if (++stable >= 3 && tSettle === null) tSettle = now() - t0; } else stable = 0; }
        last = s;
        if (tSettle !== null && (!needAuto || tAuto !== null)) break;
      }
    }
    await sleep(tCanvas === null ? 30 : 80);
  }
  let perf = null;
  try {
    perf = await ev(`(() => { const n = performance.getEntriesByType('navigation')[0]; const rs = performance.getEntriesByType('resource');
      const wasm = rs.filter(r => /\\.(wasm|dat)$/.test(r.name));
      return { domContentLoaded: n && Math.round(n.domContentLoadedEventEnd), load: n && Math.round(n.loadEventEnd), resources: rs.length,
        transfer: rs.reduce((a, r) => a + (r.transferSize || 0), 0), decoded: rs.reduce((a, r) => a + (r.decodedBodySize || 0), 0),
        encoded: rs.reduce((a, r) => a + (r.encodedBodySize || 0), 0),
        lastResEnd: Math.round(Math.max(0, ...rs.map(r => r.responseEnd))), wasmCount: wasm.length,
        wasmFetchEnd: Math.round(Math.max(0, ...wasm.map(r => r.responseEnd))), fromCache: rs.filter(r => r.transferSize === 0).length,
        bigs: rs.filter(r => r.decodedBodySize > 500000).map(r => ({ n: r.name.split('/').pop(), start: Math.round(r.startTime), dur: Math.round(r.duration), ts: r.transferSize, enc: r.encodedBodySize, dec: r.decodedBodySize })) }; })()`, false);
  } catch (e) { perf = { error: String(e) }; }
  return { tCanvas, tPaint, tSettle, tAuto, base, shots: shots.slice(-6), perf };
}

// 추적 시작·종료: 종료 시 원자료를 파일로 쓰고 EventLatency·프레임·긴 작업 요약을 돌려준다
let traceEvents = [];
listeners.push(m => { if (m.method === 'Tracing.dataCollected') traceEvents.push(...m.params.value); });
async function traceEnd(file) {
  const done = new Promise(r => listeners.push(m => { if (m.method === 'Tracing.tracingComplete') r(); }));
  await send('Tracing.end'); await done;
  if (file) fs.writeFileSync(file, JSON.stringify({ traceEvents }));
  const evs = traceEvents; traceEvents = [];
  const tn = evs.filter(e => e.name === 'thread_name' && e.args?.name === 'CrRendererMain');
  const mains = new Set(tn.map(e => `${e.pid}:${e.tid}`));
  const tasks = evs.filter(e => e.ph === 'X' && (e.name === 'RunTask' || e.name === 'ThreadControllerImpl::RunTask') && mains.has(`${e.pid}:${e.tid}`)).map(e => e.dur / 1000);
  const longT = tasks.filter(d => d >= 50);
  const open = new Map(); const lat = [];
  for (const e of evs) {
    if (e.name !== 'EventLatency') continue;
    const k = `${e.pid}:${e.id ?? e.id2?.local ?? e.id2?.global}`;
    if (e.ph === 'b') open.set(k, e); else if (e.ph === 'e' && open.has(k)) { const b = open.get(k); open.delete(k); lat.push({ type: b.args?.event_latency?.event_type || b.args?.data?.type || '?', ms: (e.ts - b.ts) / 1000 }); }
  }
  const swaps = evs.filter(e => e.name === 'Display::DrawAndSwap' && e.ph === 'X') /* otest-w-7: 중첩 이벤트 중복 제거 */.map(e => e.ts).sort((a, b) => a - b);
  const fi = []; for (let i = 1; i < swaps.length; i++) fi.push((swaps[i] - swaps[i - 1]) / 1000);
  const gc = evs.filter(e => e.ph === 'X' && /^(MajorGC|MinorGC|V8\.GC)/.test(e.name) && mains.has(`${e.pid}:${e.tid}`)).map(e => e.dur / 1000);
  const byName = {};
  for (const e of evs) { if (e.ph === 'X' && mains.has(`${e.pid}:${e.tid}`) && e.dur >= 1000) { const s = byName[e.name] ||= { n: 0, ms: 0 }; s.n++; s.ms += e.dur / 1000; } }
  const top = Object.entries(byName).map(([k, v]) => ({ k, n: v.n, ms: Math.round(v.ms) })).sort((a, b) => b.ms - a.ms).slice(0, 25);
  return { events: evs.length, tasks: tasks.length, longTasks: longT.length, longTaskMs: longT.map(x => Math.round(x)), taskMax: Math.round(Math.max(0, ...tasks)),
    eventLatency: lat, frameIntervals: fi, gcMs: gc.map(x => Math.round(x * 10) / 10), mainTop: top };
}

out({ ok: true, ready: true, pid: chrome.pid, browser: ver.Browser });
const rl = readline.createInterface({ input: process.stdin });
for await (const line of rl) {
  if (!line.trim()) continue;
  let o;
  try {
    const q = JSON.parse(line);
    if (q.op === 'goto') { await ps('Page.navigate', { url: q.url }); await sleep(q.ms || 300); o = { ok: true }; }
    else if (q.op === 'nav') o = { ok: true, value: await navReady(q.url, q.timeout || 120000, !!q.needAuto) };
    else if (q.op === 'eval') o = { ok: true, value: await ev(q.expr) };
    else if (q.op === 'invoke') { const t = now(); const v = await ev(`(async()=>await window.pdAutomation.invoke(${JSON.stringify(q.body)}))()`); o = { ok: true, value: v, wall: now() - t }; }
    else if (q.op === 'cdp') { const r = await (q.browser ? send(q.method, q.params || {}) : ps(q.method, q.params || {})); o = r.error ? { ok: false, error: JSON.stringify(r.error) } : { ok: true, value: r.result }; }
    else if (q.op === 'traceStart') { traceEvents = []; const r = await send('Tracing.start', { transferMode: 'ReportEvents', traceConfig: { includedCategories: q.cats || ['toplevel', 'viz', 'latencyInfo', 'input', 'devtools.timeline', 'disabled-by-default-v8.gc'] /* otest-w-7: 추적 부담을 줄인 기본 범주 */ } }); o = r.error ? { ok: false, error: JSON.stringify(r.error) } : { ok: true }; }
    else if (q.op === 'traceEnd') o = { ok: true, value: await traceEnd(q.file) };
    else if (q.op === 'drag') {
      const { x, y, dx, dy, steps = 60, interval = 16 } = q; const t = now();
      await ps('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
      await ps('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
      const lag = [];
      for (let i = 1; i <= steps; i++) {
        const t1 = now();
        await ps('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x + dx * i / steps, y: y + dy * i / steps, button: 'left', buttons: 1 });
        lag.push(Math.round((now() - t1) * 10) / 10);
        const w = interval - (now() - t1); if (w > 0) await sleep(w);
      }
      await ps('Input.dispatchMouseEvent', { type: 'mouseReleased', x: x + dx, y: y + dy, button: 'left', buttons: 0, clickCount: 1 });
      o = { ok: true, value: { wall: now() - t, dispatchAckMs: lag } };
    }
    else if (q.op === 'wheel') {
      const { x, y, dy = 120, n = 20, interval = 16 } = q; const t = now(); const lag = [];
      for (let i = 0; i < n; i++) { const t1 = now(); await ps('Input.dispatchMouseEvent', { type: 'mouseWheel', x, y, deltaX: 0, deltaY: dy }); lag.push(Math.round((now() - t1) * 10) / 10); const w = interval - (now() - t1); if (w > 0) await sleep(w); }
      o = { ok: true, value: { wall: now() - t, dispatchAckMs: lag } };
    }
    else if (q.op === 'click') { const { x, y, button = 'left' } = q; await ps('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y }); await ps('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button, buttons: button === 'right' ? 2 : 1, clickCount: 1 }); await ps('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button, buttons: 0, clickCount: 1 }); o = { ok: true }; }
    else if (q.op === 'shot') { const r = await ps('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(q.file, Buffer.from(r.result.data, 'base64')); o = { ok: true }; }
    else if (q.op === 'errors') o = { ok: true, value: consoleErr.splice(0) };
    else if (q.op === 'sleep') { await sleep(q.ms); o = { ok: true }; }
    else if (q.op === 'quit') { try { await send('Browser.close'); } catch { } out({ ok: true }); break; }
    else o = { ok: false, error: 'unknown op' };
  } catch (e) { o = { ok: false, error: String(e && e.message || e) }; }
  out(o);
}
ws.close();
await sleep(500);
process.exit(0);
