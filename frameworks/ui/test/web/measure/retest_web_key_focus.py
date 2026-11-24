# 웹 키 도달 회귀 측정(진단판) — 조건별 DOM 초점과 F1(투어 시작)·Ctrl+0(글자 크기 복원)·입력 칸 입력·끌어 선택·IME 조합을 Playwright 실키로 잰다. 판정 없이 원자료만 낸다
# 실행: python3 -B retest_web_key_focus.py <wwwroot> <out.json>. 다른 담당 빌드에 흔들리지 않게 복사본 번들로 재고, 결과는 logs/<에이전트>/<회차>/ 에 쓴다
import http.server, json, socketserver, sys, threading, time
from functools import partial
from playwright.sync_api import sync_playwright

BUNDLE, OUT = sys.argv[1], sys.argv[2]
PORT = 5882
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
socketserver.TCPServer.allow_reuse_address = True
httpd = socketserver.TCPServer(("127.0.0.1", PORT), partial(http.server.SimpleHTTPRequestHandler, directory=BUNDLE))
threading.Thread(target=httpd.serve_forever, daemon=True).start()
res = {"errors": []}
TRACK = """() => { window.__pdF1 = []; addEventListener('keydown', e => { if (e.key === 'F1') window.__pdF1.push(e.defaultPrevented) }) }"""


def session(p, query="?fresh", **ctx):
    b = p.chromium.launch(headless=True, args=ARGS)
    page = b.new_context(viewport=ctx.pop("viewport", {"width": 1400, "height": 900}), **ctx).new_page()
    page.on("console", lambda m: res["errors"].append(m.text[:200]) if m.type == "error" else None)
    page.on("pageerror", lambda e: res["errors"].append("pageerror: " + str(e)[:200]))
    page.goto("http://127.0.0.1:%d/%s" % (PORT, query), wait_until="load", timeout=120000)
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=180000)
    page.evaluate(TRACK)

    def call(cmd, **k):
        return json.loads(page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": k})))

    for _ in range(90):
        if call("status").get("ok"):
            break
        time.sleep(1)
    time.sleep(2)
    return b, page, call


def dom(page):
    return page.evaluate("() => { const a = document.activeElement; return a ? a.tagName + '#' + (a.id || '') : null }")


def probe(page, call, tag):
    """휠은 문서 위에서만 글자 크기를 바꾸므로 문서를 열어 둔 조건에서 부른다"""
    r = {"dom": dom(page)}
    page.mouse.move(700, 400)
    page.keyboard.down("Control"); page.mouse.wheel(0, -240); page.keyboard.up("Control"); time.sleep(0.8)
    r["after_wheel"] = (call("status").get("result") or {}).get("baseFontSize")
    page.keyboard.press("Control+0"); time.sleep(0.8)
    r["after_ctrl0"] = (call("status").get("result") or {}).get("baseFontSize")
    call("tour.end")
    page.keyboard.press("F1"); time.sleep(1.5)
    st = call("tour.state").get("result") or []
    r["f1_tour"] = bool(st)
    r["f1_default_prevented"] = page.evaluate("() => window.__pdF1")
    call("tour.end"); time.sleep(0.5)
    res[tag] = r


with sync_playwright() as p:
    # 기동 직후, 아무것도 누르지 않음
    b, page, call = session(p)
    res["K0_boot_dom"] = dom(page)
    call("docs.open", kind="grid"); time.sleep(1)
    probe(page, call, "K1_no_click")
    b.close()

    # 패널 빈 곳 클릭
    b, page, call = session(p)
    call("docs.open", kind="grid"); time.sleep(1)
    page.mouse.click(100, 700); time.sleep(0.8)
    probe(page, call, "K2_panel_blank")
    b.close()

    # 문서 없는 빈 작업 영역 클릭, F1 만
    b, page, call = session(p)
    page.mouse.click(700, 400); time.sleep(0.8)
    res["K3_blank_dom"] = dom(page)
    page.keyboard.press("F1"); time.sleep(1.5)
    res["K3_f1_tour"] = bool(call("tour.state").get("result"))
    call("tour.end")
    b.close()

    # 입력 칸: 초점을 뺏지 않는가. 설정 검색 칸을 누르고 입력, 다시 칸 안을 눌러 입력, 칸 밖을 누른 뒤 상태
    b, page, call = session(p)
    for mid in ("shell.mnuSys", "shell.mnuSysSettings"):  # 설정은 시스템 메뉴 → 설정 두 번 클릭으로 연다
        bd = (call("ui.find", id=mid).get("result") or {}).get("bounds")
        page.mouse.click(bd["x"] + bd["w"] / 2, bd["y"] + bd["h"] / 2); time.sleep(1)
    time.sleep(1)
    sb = (call("ui.find", id="settings.search").get("result") or {}).get("bounds")
    res["I_search_bounds"] = sb
    page.mouse.click(sb["x"] + 40, sb["y"] + sb["h"] / 2); time.sleep(0.8)
    res["I1_dom_after_click"] = dom(page)
    page.keyboard.type("ab", delay=80); time.sleep(0.8)
    res["I1_search"] = (call("settings.window").get("result") or {}).get("search")
    sb = (call("ui.find", id="settings.search").get("result") or {}).get("bounds")  # 검색 결과가 줄면 Flyout 이 줄어 칸 위치가 바뀐다
    res["I2_search_bounds"] = sb
    page.mouse.click(sb["x"] + 80, sb["y"] + sb["h"] / 2); time.sleep(0.8)
    res["I2_dom_after_reclick"] = dom(page)
    page.keyboard.type("c", delay=80); time.sleep(0.8)
    res["I2_search"] = (call("settings.window").get("result") or {}).get("search")
    # 텍스트 끌어 선택 뒤 입력: 선택이 유지되면 글자가 대체된다
    sb = (call("ui.find", id="settings.search").get("result") or {}).get("bounds")
    y = sb["y"] + sb["h"] / 2
    page.mouse.move(sb["x"] + 150, y); page.mouse.down(); page.mouse.move(sb["x"] + 8, y, steps=6); page.mouse.up(); time.sleep(0.6)
    res["I4_dom_after_drag"] = dom(page)
    page.keyboard.type("z", delay=80); time.sleep(0.8)
    res["I4_search_after_select_type"] = (call("settings.window").get("result") or {}).get("search")
    # IME 조합: 조합 중 입력 요소 값과 확정 뒤 값
    cdp = page.context.new_cdp_session(page)
    sb = (call("ui.find", id="settings.search").get("result") or {}).get("bounds")
    y = sb["y"] + sb["h"] / 2
    cdp.send("Input.imeSetComposition", {"text": "ㅎ", "selectionStart": 1, "selectionEnd": 1}); time.sleep(0.5)
    res["I5_dom_composing"] = dom(page)
    res["I5_input_value_composing"] = page.evaluate("() => document.activeElement.value")
    page.mouse.move(sb["x"] + 60, y); time.sleep(0.2)
    cdp.send("Input.imeSetComposition", {"text": "하", "selectionStart": 1, "selectionEnd": 1}); time.sleep(0.5)
    cdp.send("Input.insertText", {"text": "하"}); time.sleep(0.8)
    res["I5_search_after_commit"] = (call("settings.window").get("result") or {}).get("search")
    res["I6_outline"] = page.evaluate("() => { const c = document.querySelector('#out'); const s = getComputedStyle(c); return [s.outlineStyle, s.outlineWidth] }")
    page.keyboard.press("Escape"); time.sleep(1.2)
    res["I3_settings_open_after_esc"] = bool(call("settings.window").get("ok"))
    res["I3_dom"] = dom(page)
    b.close()

    # 터치 문맥 폰 폭: 입력 요소로 초점이 가지 않는가(가면 모바일 키보드가 뜬다)
    b, page, call = session(p, viewport={"width": 390, "height": 844}, has_touch=True, is_mobile=True)
    res["T0_boot_dom"] = dom(page)
    page.touchscreen.tap(195, 500); time.sleep(0.8)
    res["T1_dom_after_tap"] = dom(page)
    b.close()
httpd.shutdown()
json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(json.dumps(res, ensure_ascii=False))
