# 웹 번들 기동 스모크 — 정적 서버로 번들을 열고(헤드리스 Chromium) 앱 시작 여부와 콘솔 오류를 확인하는 스크립트. 인자: 번들경로 포트 태그
import http.server
import json
import os
import socketserver
import sys
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

bundle, port, tag = sys.argv[1], int(sys.argv[2]), sys.argv[3]
handler = partial(http.server.SimpleHTTPRequestHandler, directory=bundle)
http.server.SimpleHTTPRequestHandler.log_message = lambda *a: None
httpd = socketserver.TCPServer(("127.0.0.1", port), handler)
threading.Thread(target=httpd.serve_forever, daemon=True).start()

errors, logs = [], []
result = {"tag": tag, "bundle": bundle}
try:
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"])
        page = b.new_page(viewport={"width": 1400, "height": 900})
        page.on("console", lambda m: (errors if m.type == "error" else logs).append(m.text[:300]))
        page.on("pageerror", lambda e: errors.append("pageerror: " + str(e)[:300]))
        page.goto(f"http://127.0.0.1:{port}/?fresh", wait_until="load", timeout=60000)
        started = None
        for _ in range(60):  # 최대 30초
            started = page.evaluate("() => (window.__pdPoc && window.__pdPoc.mainStartedMs) || null")
            failed = any("기동 실패" in t or "TypeInitialization" in t for t in errors + logs)
            if started or failed:
                break
            time.sleep(0.5)
        time.sleep(2)
        result["mainStartedMs"] = started
        result["canvas"] = page.evaluate("() => document.querySelectorAll('canvas').length")
        result["pdAutomation"] = page.evaluate("() => typeof window.pdAutomation")
        shot = os.path.join(os.path.dirname(os.path.abspath(__file__)), f"web_smoke_{tag}.png")
        page.screenshot(path=shot)
        result["screenshot"] = shot
        b.close()
finally:
    httpd.shutdown()

result["console_errors"] = errors[:10]
result["boot_failure"] = any("기동 실패" in t or "TypeInitialization" in t for t in errors + logs)
result["ok"] = (not result["boot_failure"]) and result.get("canvas", 0) > 0 and not any("pageerror" in e for e in errors)
print(json.dumps(result, ensure_ascii=False))
