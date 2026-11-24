#!/usr/bin/env python3
# A7 문서 열기 원인 분리 측정 — headless Chrome 에서 Fold5 펼침·접힘 화면 크기를 흉내 내 설정 조건별 doc·grid 열기 시간을 잰다(참고 측정, 공식 판정 아님)
"""
사용: python3 -B a7_docopen_web.py --bundle <wwwroot> --out <결과.json> [--mode open|closed] [--runs 10]
      [--settings-json '{"MdiMoveLight":false}'] [--query grid-open=fluentcell] [--dpr 1] [--port 5793] [--label 이름]
- 시행마다 새 브라우저 컨텍스트(저장소 빈 상태)로 ?fresh 를 열고, 앱 시작 전 pd-poc-settings 에 --settings-json 을 넣는다.
- doc  = 문서 0개에서 첫 그리드 열기, grid = 그리드·간트가 열린 상태에서 그리드 하나 더 열기(Fold5 webperf 의 doc·open_grid 대응).
- 열기는 진단 브리지 docs.open(kind=grid) — 셸 OpenDocument 와 같은 경로다. 메뉴 탭 비용(입력 전달·메뉴 닫힘)은 들어가지 않는다.
- 시각은 페이지 안 performance.now 기준(rAF 기록은 invoke 직전부터). sync=invoke(Promise) 완료까지, first=sync 뒤 첫 rAF,
  settle=max(first, 50ms 넘게 걸린 마지막 프레임 간격이 끝난 rAF 시각), long=[시작, 길이] 50ms 넘는 간격 목록, busy=long 합. app=앱 PerfProbe 의 doc.open.grid(뷰 생성→두 번째 rAF).
- headless 는 swiftshader(CPU 그리기)라 절대값은 Fold5 와 다르다. 같은 PC·같은 조건끼리 비교만 한다.
종료 코드: 0=측정 완료, 1=측정 오류.
"""
import argparse
import http.server
import json
import os
import socketserver
import statistics
import sys
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
# Fold5 실측(otest-m-1 r2 p1 webperf_web_*.json): 펼침 1812px 폭·innerHeight 820·dpr 2.25·데스크톱 UA, 접힘 904px 폭·innerHeight 920·dpr 2.25·모바일 UA.
# CSS 크기만 맞추고 dpr 은 1 이 기본이다 — headless 에서 device_scale_factor 2.25 를 주면 앱 폭이 357.8(=805/2.25)로 잡혀 펼침이 모바일형이 된다
# (11-11 실측). Fold5 실기 펼침은 태블릿형(문서 탭·좌우 패널)이므로 실기와 같은 형이 되는 dpr 1 로 잰다. --dpr 로 바꿀 수 있다.
MODES = {
    "open": {"viewport": {"width": 805, "height": 820}, "dpr": 1, "mobile": False,
             "ua": "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36"},
    "closed": {"viewport": {"width": 402, "height": 920}, "dpr": 1, "mobile": True,
               "ua": "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Mobile Safari/537.36"},
}
LONG_MS = 50
WINDOW_MS = 2000

OPEN_JS = """async ([body, windowMs, longMs]) => {
  const frames = [];
  const t0 = performance.now();
  const done = new Promise(res => { const f = () => { const t = performance.now(); frames.push(t); if (t - t0 > windowMs) res(); else requestAnimationFrame(f); }; requestAnimationFrame(f); });
  const r = await window.pdAutomation.invoke(body);
  const tSync = performance.now();
  await done;
  const after = frames.filter(t => t >= tSync);
  const gaps = []; let settle = (after.length ? after[0] : tSync) - t0; let prev = t0;
  for (const t of frames) { const g = t - prev; if (g > longMs) { gaps.push([Math.round(prev - t0), Math.round(g)]); settle = Math.max(settle, t - t0); } prev = t; }
  return {ok: JSON.parse(r).ok, sync: Math.round(tSync - t0), first: Math.round((after.length ? after[0] : tSync) - t0), settle: Math.round(settle), long: gaps,
          busy: Math.round(gaps.reduce((s, x) => s + x[1], 0))};
}"""


class Server:
    def __init__(self, bundle, port):
        http.server.SimpleHTTPRequestHandler.log_message = lambda *a: None
        socketserver.TCPServer.allow_reuse_address = True
        self.httpd = socketserver.ThreadingTCPServer(("127.0.0.1", port), partial(http.server.SimpleHTTPRequestHandler, directory=bundle))
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()


def call(page, cmd, **a):
    body = json.dumps({"cmd": cmd, "args": a}, ensure_ascii=False)
    try:
        return json.loads(page.evaluate("b => window.pdAutomation.invoke(b)", body))
    except Exception as e:  # noqa: BLE001
        return {"ok": False, "error": "EXC " + str(e)[:200]}


def open_measured(page, kind):
    call(page, "perf.reset")
    body = json.dumps({"cmd": "docs.open", "args": {"kind": kind}})
    row = page.evaluate(OPEN_JS, [body, WINDOW_MS, LONG_MS])
    ops = call(page, "perf.ops").get("result")  # 앱 안 단계 시간 [sync, frame](grid.ctor.*·grid.open.*·ui.activate.* 등, 이번 열기만)
    if isinstance(ops, dict):
        ops = ops.get("operations", ops)
    if isinstance(ops, list):
        row["ops"] = {o.get("name"): [o.get("sync_p95_ms"), o.get("frame_max_ms")] for o in ops if isinstance(o, dict) and not str(o.get("name", "")).startswith("remote.perf")}
    return row


def app_opens(page):
    r = call(page, "perf.get")
    res = r.get("result") if r.get("ok") else None
    return [o for o in (res or {}).get("opens", [])] if isinstance(res, dict) else []


def one_run(browser, port, mode, settings, idle_s, dpr, query=""):
    m = dict(MODES[mode], dpr=dpr or MODES[mode]["dpr"])
    ctx = browser.new_context(viewport=m["viewport"], device_scale_factor=m["dpr"], is_mobile=m["mobile"], has_touch=True, user_agent=m["ua"])
    errors = []
    try:
        if settings:
            ctx.add_init_script("try{localStorage.setItem('pd-poc-settings', %s)}catch(e){}" % json.dumps(json.dumps(settings)))
        page = ctx.new_page()
        page.on("pageerror", lambda e: errors.append(str(e)[:200]))
        page.goto("http://127.0.0.1:%d/?fresh%s" % (port, ("&" + query) if query else ""), wait_until="load", timeout=120000)
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=180000)
        for _ in range(90):
            if call(page, "status").get("ok"):
                break
            time.sleep(1)
        time.sleep(idle_s)
        info = call(page, "status").get("result")
        row = {"doc": open_measured(page, "grid")}
        time.sleep(1.5)
        call(page, "docs.open", kind="gantt")
        time.sleep(1.5)
        row["grid"] = open_measured(page, "grid")
        time.sleep(1.0)
        row["app_opens"] = app_opens(page)
        pl = call(page, "placement.state")
        plr = (pl.get("result") or {}) if pl.get("ok") else {}
        row["placement"] = {k: v for k, v in plr.items() if not isinstance(v, (list, dict))} if isinstance(plr, dict) else None
        row["docs"] = [(d.get("kind"), d.get("state"), d.get("floating")) for d in (call(page, "docs.list").get("result") or [])]
        row["chrome_active"] = call(page, "chrome.state").get("result")
        st = call(page, "ui.style").get("result") or {}
        row["ui_style"] = {k: st.get(k) for k in ("device", "setting", "effective", "width")} if isinstance(st, dict) else None
        row["status_ok"] = bool(info)
        pf = call(page, "perf.get").get("result") or {}
        row["perf_text"] = {k: v for k, v in pf.items() if isinstance(v, (str, int, float)) and k.startswith(("grid_", "zoom_first"))} if isinstance(pf, dict) else None
        row["errors"] = errors
        return row
    finally:
        ctx.close()


def stats(vals):
    vals = [v for v in vals if v is not None]
    if not vals:
        return None
    return {"n": len(vals), "median": statistics.median(vals), "max": max(vals), "min": min(vals)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--mode", choices=sorted(MODES), default="open")
    ap.add_argument("--runs", type=int, default=10)
    ap.add_argument("--settings-json", default="")
    ap.add_argument("--port", type=int, default=5793)
    ap.add_argument("--idle", type=float, default=4.0, help="시작 뒤 첫 열기 전 대기 초")
    ap.add_argument("--label", default="")
    ap.add_argument("--query", default="", help="시험 URL 에 덧붙일 시작 인자 query(예: grid-open=litecell — 진단판 대조 스위치)")
    ap.add_argument("--dpr", type=float, default=0, help="device_scale_factor 덮어쓰기(0=형 기본)")
    a = ap.parse_args()
    if "logs/ui28" in os.path.abspath(a.out).replace("\\", "/"):
        print("logs/ui28/ 는 otest 결과 전용이다")
        return 1
    settings = json.loads(a.settings_json) if a.settings_json else {}
    res = {"bundle": a.bundle, "mode": a.mode, "settings": settings, "query": a.query, "label": a.label, "env": MODES[a.mode],
           "started_at": time.strftime("%Y-%m-%d %H:%M:%S"), "runs": []}
    srv = Server(a.bundle, a.port)
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, args=ARGS)
            try:
                for i in range(a.runs):
                    try:
                        row = one_run(browser, a.port, a.mode, settings, a.idle, a.dpr, a.query)
                    except Exception as e:  # noqa: BLE001
                        row = {"error": str(e)[:300]}
                    row["i"] = i
                    res["runs"].append(row)
                    d, g = row.get("doc") or {}, row.get("grid") or {}
                    print("run %d doc settle=%s first=%s long=%s | grid settle=%s first=%s long=%s" % (
                        i, d.get("settle"), d.get("first"), d.get("long"), g.get("settle"), g.get("first"), g.get("long")), flush=True)
            finally:
                browser.close()
    finally:
        srv.close()
    summ = {}
    for k in ("doc", "grid"):
        for f in ("sync", "first", "settle", "busy"):
            summ["%s_%s" % (k, f)] = stats([(r.get(k) or {}).get(f) for r in res["runs"]])
    res["summary"] = summ
    res["finished_at"] = time.strftime("%Y-%m-%d %H:%M:%S")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps({k: (v and [v["median"], v["max"]]) for k, v in summ.items()}, ensure_ascii=False))
    return 1 if any(r.get("error") for r in res["runs"]) else 0


if __name__ == "__main__":
    sys.exit(main())
