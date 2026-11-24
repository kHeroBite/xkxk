// 기기 Chrome(adb forward tcp:9333 → chrome_devtools_remote)의 PD 탭에 CDP 로 붙어 시나리오 모듈을 실행하는 모바일 웹 시험 도구(Windows node.exe 로 실행)
// 사용: node.exe cdp_win.mjs <URL 일부> <시나리오.mjs(Windows 경로)> <결과 폴더(Windows 경로)> [인자 JSON]
// 시나리오는 export default async function (ctx) 형태이고 반환값을 <결과 폴더>/<시나리오 이름>_result.json 으로 저장한다.
// ctx: inv(cmd,args) — pdAutomation.invoke 왕복(wall ms 포함), ev(식), tap(x,y) — CDP 터치, pinch(x,y,scale), shot(이름) — 페이지 캡처, sleep(ms), arg
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [, , urlPart, scenPath, outDir, argJson = '{}'] = process.argv;
const PORT = process.env.CDP || 9333;
const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
const page = list.find(p => p.type === 'page' && String(p.url).includes(urlPart));
if (!page) { console.error('대상 탭 없음', JSON.stringify(list.map(p => p.url))); process.exit(2); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0; const pend = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
const send = (method, params = {}) => new Promise(r => { const i = ++id; pend.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const sleep = ms => new Promise(r => setTimeout(r, ms));

const ev = async (expression) => {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.result?.exceptionDetails) return { __error: r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text };
  return r.result?.result?.value;
};
const inv = async (cmd, args = {}) => {
  const t = Date.now();
  const s = await ev(`(async()=>await pdAutomation.invoke(${JSON.stringify({ cmd, args })}))()`);
  let j; try { j = typeof s === 'string' ? JSON.parse(s) : s; } catch { j = { raw: s }; }
  if (j && typeof j === 'object') j.wall = Date.now() - t;
  return j;
};
const tap = async (x, y) => {
  const tp = [{ x, y, id: 1, radiusX: 4, radiusY: 4, force: 1 }];
  await send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: tp });
  await sleep(60);
  await send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
};
const pinch = async (x, y, scaleFactor) => (await send('Input.synthesizePinchGesture', { x, y, scaleFactor, relativeSpeed: 400, gestureSourceType: 'touch' })).result;
const shot = async (name) => {
  const r = await send('Page.captureScreenshot', { format: 'png' });
  if (r.result?.data) { fs.writeFileSync(path.join(outDir, name + '.png'), Buffer.from(r.result.data, 'base64')); return name + '.png'; }
  return null;
};

fs.mkdirSync(outDir, { recursive: true });
const mod = await import(pathToFileURL(scenPath).href);
const name = path.basename(scenPath, '.mjs');
let result;
try {
  result = await mod.default({ inv, ev, tap, pinch, shot, sleep, send, arg: JSON.parse(argJson) });
} catch (e) {
  result = { __fatal: String(e && e.stack || e) };
}
const tag = JSON.parse(argJson).tag || '';
fs.writeFileSync(path.join(outDir, `${name}${tag ? '_' + tag : ''}_result.json`), JSON.stringify(result, null, 1));
console.log(JSON.stringify(result).slice(0, 3000));
ws.close();
process.exit(0);
