# 웹 키 도달 회귀 측정(제품판) — 원격 명령이 없어 DOM 초점·F1 기본 동작·캡처 차이·입력 칸 값만 잰다. 판정 없이 원자료만 낸다
# 실행: python3 -B retest_web_key_focus_product.py <wwwroot> <out.json>. 입력 칸 좌표는 1400x900 진단판에서 잰 값이라 화면 배치가 바뀌면 고친다(P3_dom_field 가 INPUT 인지 먼저 본다)
import http.server, io, json, socketserver, sys, threading, time
from functools import partial
from PIL import Image, ImageChops
from playwright.sync_api import sync_playwright

BUNDLE, OUT = sys.argv[1], sys.argv[2]
PORT = 5883
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
socketserver.TCPServer.allow_reuse_address = True
httpd = socketserver.TCPServer(("127.0.0.1", PORT), partial(http.server.SimpleHTTPRequestHandler, directory=BUNDLE))
threading.Thread(target=httpd.serve_forever, daemon=True).start()
res = {"errors": []}


def dom(page):
    return page.evaluate("() => { const a = document.activeElement; return a ? a.tagName + '#' + (a.id ? a.id.replace(/[a-z]+$/, '') : '') : null }")


def shot(page):
    return Image.open(io.BytesIO(page.screenshot())).convert("RGB")


def changed(a, b):
    box = ImageChops.difference(a, b).getbbox()
    return list(box) if box else None


def session(p):
    b = p.chromium.launch(headless=True, args=ARGS)
    page = b.new_page(viewport={"width": 1400, "height": 900})
    page.on("console", lambda m: res["errors"].append(m.text[:200]) if m.type == "error" else None)
    page.on("pageerror", lambda e: res["errors"].append("pageerror: " + str(e)[:200]))
    page.goto("http://127.0.0.1:%d/?fresh" % PORT, wait_until="load", timeout=120000)
    page.wait_for_selector("canvas.avalonia-canvas", timeout=180000)
    time.sleep(12)
    page.evaluate("() => { window.__pdF1 = []; addEventListener('keydown', e => { if (e.key === 'F1') window.__pdF1.push(e.defaultPrevented) }) }")
    return b, page


def f1(page, tag):
    a = shot(page)
    page.keyboard.press("F1"); time.sleep(2.5)
    b_ = shot(page)
    res[tag] = {"dom": dom(page), "f1_default_prevented": page.evaluate("() => window.__pdF1"), "changed_box": changed(a, b_)}
    page.screenshot(path=OUT.replace(".json", "_%s.png" % tag))


with sync_playwright() as p:
    b, page = session(p)
    res["diag_absent"] = page.evaluate("() => !window.pdAutomation")
    res["P0_boot_dom"] = dom(page)
    res["P0_outline"] = page.evaluate("() => { const s = getComputedStyle(document.querySelector('#out')); return [s.outlineStyle, s.outlineWidth] }")
    f1(page, "P1_boot")
    b.close()

    b, page = session(p)
    page.mouse.click(700, 400); time.sleep(1)
    res["P2_dom_blank"] = dom(page)
    f1(page, "P2_blank")
    b.close()

    # 입력 칸: 설정 버튼 좌표는 같은 창 크기의 진단판에서 잰 값(메뉴·도구 줄 왼쪽)
    b, page = session(p)
    page.mouse.click(117, 80); time.sleep(2.5)
    page.mouse.click(372, 235); time.sleep(1)
    res["P3_dom_field"] = dom(page)
    page.keyboard.type("ab", delay=80); time.sleep(0.8)
    res["P3_input_value"] = page.evaluate("() => document.activeElement.value")
    page.screenshot(path=OUT.replace(".json", "_P3_field.png"))
    b.close()
httpd.shutdown()
json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(json.dumps(res, ensure_ascii=False))
