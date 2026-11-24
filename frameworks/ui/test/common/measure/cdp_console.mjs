// 기기 Chrome(adb forward tcp:9333)의 PD 탭을 다시 읽어(Page.reload) 기동 중 콘솔 줄(Runtime.consoleAPICalled·Log.entryAdded)을 모아 출력하는 CDP 스크립트(UX1 '[host] form-factor=' 줄 수집용)
// 사용: node.exe cdp_console.mjs <URL 일부> <대기 초=25> [필터 문자열]
const [, , urlPart = '127.0.0.1:5185', waitS = '25', filt = ''] = process.argv;
const PORT = process.env.CDP || 9333;
const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
const page = list.find(p => p.type === 'page' && String(p.url).includes(urlPart));
if (!page) { console.log(JSON.stringify({ ok: false, error: 'no-tab', tabs: list.map(p => p.url) })); process.exit(2); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0; const pend = new Map(); const lines = [];
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); return; }
  if (m.method === 'Runtime.consoleAPICalled') {
    const t = (m.params.args || []).map(a => a.value !== undefined ? String(a.value) : (a.description || '')).join(' ');
    lines.push({ src: 'console.' + m.params.type, text: t });
  } else if (m.method === 'Log.entryAdded') {
    lines.push({ src: 'log.' + m.params.entry.level, text: m.params.entry.text });
  }
};
const send = (method, params = {}) => new Promise(r => { const i = ++id; pend.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
await send('Runtime.enable');
await send('Log.enable');
await send('Page.enable');
await send('Page.reload', { ignoreCache: false });
await new Promise(r => setTimeout(r, Number(waitS) * 1000));
const out = filt ? lines.filter(l => l.text.includes(filt)) : lines;
console.log(JSON.stringify({ ok: true, url: page.url, total: lines.length, lines: out }));
ws.close();
process.exit(0);
