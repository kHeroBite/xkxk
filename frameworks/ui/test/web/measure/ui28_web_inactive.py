# 웹 추가 확인 — 트리밍한 번들에서 CreateInactiveChromeStyle(ChromeInactive.axaml 런타임 로드)이 실제로 도는지(콘솔·페이지 오류 0)
# 이 경로는 비활성 흐림을 '브러시' 방식으로 켰을 때만 탄다(WindowChromeDiag.SetInactiveDimMode). 웹은 쿼리 chrome-inactive-dim=brush 로 켠다.
# 실행: python3 -B ui28_web_inactive.py --bundle <wwwroot> --out <json> --shots <폴더> [--port 5781]
import argparse
import http.server
import json
import os
import socketserver
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5781)
    ap.add_argument("--query", default="fresh&chrome-inactive-dim=brush")
    a = ap.parse_args()
    os.makedirs(a.shots, exist_ok=True)
    http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
    httpd = socketserver.TCPServer(("127.0.0.1", a.port), partial(http.server.SimpleHTTPRequestHandler, directory=a.bundle))
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    errors, logs, res = [], [], {"query": a.query, "started_at": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
    t0 = time.time()
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=ARGS)
        page = b.new_page(viewport={"width": 1400, "height": 900})
        page.on("console", lambda m: (errors if m.type == "error" else logs).append(m.text[:300]))
        page.on("pageerror", lambda e: errors.append("pageerror: " + str(e)[:300]))
        page.goto("http://127.0.0.1:%d/?%s" % (a.port, a.query), wait_until="load", timeout=90000)
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)

        def call(cmd, **k):
            try:
                return json.loads(page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": k}, ensure_ascii=False)))
            except Exception as e:  # noqa: BLE001
                return {"ok": False, "error": "EXC " + str(e)[:200]}

        for _ in range(60):
            if call("status").get("ok"):
                break
            time.sleep(1)
        perf = call("perf.get")
        res["perf_chrome_diag"] = json.dumps(perf, ensure_ascii=False).count("dim=brush")
        for i in (1, 2):
            call("docs.open", kind="grid", title="W%d" % i)
            time.sleep(1.0)
            call("docs.sdi", doc="Grid-%d" % i, on=True)
            time.sleep(1.0)
        call("docs.activate", doc="Grid-1")
        time.sleep(1.0)
        call("docs.activate", doc="Grid-2")
        time.sleep(1.0)
        page.screenshot(path=os.path.join(a.shots, "inactive_brush.png"))
        res["docs"] = (call("docs.list") or {}).get("result")
        res["chrome_state_inactive"] = call("chrome.state", doc="Grid-1").get("result")
        perf = call("perf.get")
        res["dim_brush_in_state"] = "dim=brush" in json.dumps(perf, ensure_ascii=False)
        b.close()
    httpd.shutdown()
    res["console_errors"] = errors
    res["console_log_sample"] = logs[-12:]
    res["elapsed_ms"] = round((time.time() - t0) * 1000)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print("saved", a.out, "errors", len(errors))


if __name__ == "__main__":
    main()
