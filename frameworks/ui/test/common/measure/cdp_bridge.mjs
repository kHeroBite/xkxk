// 기기 Chrome(adb forward tcp:9333)의 PD 탭과 한 줄 JSON 요청·응답으로 통신하는 상주 CDP 다리(Windows node.exe 로 실행, Python 시험 스크립트가 stdin/stdout 으로 사용)
// 사용: node.exe cdp_bridge.mjs <URL 일부>
// 요청(한 줄 JSON): {"op":"invoke","body":"<pdAutomation.invoke 인자 JSON 문자열>"} | {"op":"eval","expr":"<식>"} | {"op":"cdp","method":"...","params":{...}} | {"op":"sleep","ms":n}
// 응답(한 줄 JSON): {"ok":true,"value":...} 또는 {"ok":false,"error":"..."}
import readline from 'node:readline';

const [, , urlPart = '127.0.0.1:5185'] = process.argv;
const PORT = process.env.CDP || 9333;
const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
const page = list.find(p => p.type === 'page' && String(p.url).includes(urlPart));
if (!page) { console.log(JSON.stringify({ ok: false, error: 'no-tab', tabs: list.map(p => p.url) })); process.exit(2); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0; const pend = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
const send = (method, params = {}) => new Promise(r => { const i = ++id; pend.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (expression) => {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text);
  return r.result?.result?.value;
};
console.log(JSON.stringify({ ok: true, ready: page.url }));
const rl = readline.createInterface({ input: process.stdin });
for await (const line of rl) {
  if (!line.trim()) continue;
  let out;
  try {
    const q = JSON.parse(line);
    if (q.op === 'invoke') out = { ok: true, value: await ev(`(async()=>await pdAutomation.invoke(${JSON.stringify(q.body)}))()`) };
    else if (q.op === 'eval') out = { ok: true, value: await ev(q.expr) };
    else if (q.op === 'cdp') { const r = await send(q.method, q.params || {}); out = r.error ? { ok: false, error: JSON.stringify(r.error) } : { ok: true, value: r.result }; }
    else if (q.op === 'sleep') { await new Promise(r => setTimeout(r, q.ms)); out = { ok: true }; }
    else if (q.op === 'quit') { console.log(JSON.stringify({ ok: true })); break; }
    else out = { ok: false, error: 'unknown op' };
  } catch (e) { out = { ok: false, error: String(e && e.message || e) }; }
  console.log(JSON.stringify(out));
}
ws.close();
process.exit(0);
