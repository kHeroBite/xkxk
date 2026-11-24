// Fold5 크롬(adb forward tcp:9333 → chrome_devtools_remote)에 CDP 로 붙어 웹 판의 innerWidth·devicePixelRatio·ui.mode(viewWidth)를 읽는 스크립트(Windows node.exe 로 실행)
// 사용: node.exe cdp_viewwidth.js <URL 일부(예 127.0.0.1:5190)> <결과 JSON 경로>
const [, , urlPart = '127.0.0.1:5190', outPath = 'cdp_viewwidth.json'] = process.argv;
const fs = require('fs');

async function main() {
    const list = await (await fetch('http://127.0.0.1:9333/json')).json();
    const page = list.find(p => p.type === 'page' && String(p.url).includes(urlPart));
    if (!page) throw new Error('대상 탭 없음: ' + JSON.stringify(list.map(p => p.url)));
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    let id = 0;
    const pending = new Map();
    ws.onmessage = (ev) => { const m = JSON.parse(ev.data); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
    await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    const send = (method, params) => new Promise(res => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
    const ev = async (expr) => (await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true })).result?.result?.value;
    const out = { url: page.url, at: new Date().toISOString() };
    out.innerWidth = await ev('innerWidth');
    out.innerHeight = await ev('innerHeight');
    out.devicePixelRatio = await ev('devicePixelRatio');
    out.visualViewport = await ev('JSON.stringify({w: visualViewport.width, h: visualViewport.height, scale: visualViewport.scale})');
    out.pdPoc = await ev('JSON.stringify({runtimeReadyMs: globalThis.__pdPoc?.runtimeReadyMs, mainStartedMs: globalThis.__pdPoc?.mainStartedMs, error: globalThis.__pdPoc?.error})');
    out.uiMode = await ev("(async () => { try { const r = await globalThis.pdAutomation.invoke({cmd:'ui.mode'}); return typeof r === 'string' ? r : JSON.stringify(r); } catch (e) { return 'ERR ' + e; } })()");
    fs.writeFileSync(outPath, JSON.stringify(out, null, 1));
    console.log(JSON.stringify(out));
    ws.close();
}
main().catch(e => { console.error(String(e)); process.exit(1); });
