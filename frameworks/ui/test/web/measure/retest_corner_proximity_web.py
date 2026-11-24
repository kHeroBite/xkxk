#!/usr/bin/env python3
# 패널 끝 확장 버튼 근접 표시 웹 측정 — headless Chromium 실제 마우스 이벤트로 경계 거리·클릭·투어·관리형 창 덮임·떠남을, 터치 문맥으로 항상 표시를 값으로 남긴다
"""
사용: python3 -B retest_corner_proximity_web.py --bundle <진단판 wwwroot> --out <결과.json> [--port 5811]
Playwright 마우스는 페이지 안 포인터 이벤트다(OS 입력 아님). 시간 측정은 하지 않고 처리 횟수만 센다.
종료 코드: 0=전부 통과, 1=실패 있음, 2=앱 준비 실패.
"""
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
BL, BR = "panel.corner.BottomPane.BottomLeft", "panel.corner.BottomPane.BottomRight"
SETTLE_S = 0.3
SYNTH = """([type, x, y, buttons]) => { // 이동 없는 포인터 이벤트 합성
  const target = document.elementFromPoint(x, y);
  target.dispatchEvent(new PointerEvent(type, {bubbles: true, cancelable: true, composed: true, clientX: x, clientY: y, button: 0, buttons,
    pointerId: 1, pointerType: 'mouse', isPrimary: true}));
  return target.tagName;
}"""


class Page:
    def __init__(self, browser, url, errors, **context):
        self.context = browser.new_context(**context)
        self.page = self.context.new_page()
        self.page.on("pageerror", lambda e: errors.append("pageerror: " + str(e)[:300]))
        self.items = []
        self.page.goto(url, wait_until="load", timeout=120000)
        self.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=180000)
        for _ in range(120):
            if self.call("status") is not None and (self.state().get("buttons") or self.state().get("supported") is False):
                break
            time.sleep(1)
        time.sleep(1)

    def call(self, cmd, **a):
        try:
            r = json.loads(self.page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": a}, ensure_ascii=False)))
        except Exception as e:  # noqa: BLE001
            print("  호출 실패", cmd, str(e)[:160])
            return None
        if not r.get("ok"):
            print("  실패 응답", cmd, str(r.get("error"))[:160])
        return r.get("result") if r.get("ok") else None

    def state(self):
        return self.call("panel.corner.state") or {}

    def button(self, button_id):
        return next((b for b in self.state().get("buttons", []) if b["id"] == button_id), None)

    def shown(self, button_id):
        return (self.button(button_id) or {}).get("shown")

    def move(self, x, y):
        self.page.mouse.move(x, y)
        time.sleep(SETTLE_S)

    def check(self, name, passed, detail=None):
        self.items.append({"name": name, "pass": bool(passed), "detail": detail})
        print(("PASS " if passed else "FAIL ") + name + ("  " + json.dumps(detail, ensure_ascii=False)[:240] if detail is not None else ""))

    def far(self):
        d = self.call("panel.bounds")["Documents"]
        return d["x"] + d["w"] / 2, d["y"] + d["h"] / 2


def center(rect):
    return rect["x"] + rect["w"] / 2, rect["y"] + rect["h"] / 2


def run_pc(t, shots):
    raw = {"coarse": t.page.evaluate("() => matchMedia('(pointer: coarse)').matches"), "hover": t.page.evaluate("() => matchMedia('(hover: hover)').matches")}
    t.check("pc.not_coarse", raw["coarse"] is False, raw)
    t.move(*t.far())
    s = t.state()
    t.check("pc.hidden_when_far", s["buttons"] and all(b["shown"] is False and b["opacity"] == 0 and b["hitTest"] is False for b in s["buttons"]), s["buttons"])
    t.page.screenshot(path=os.path.join(shots, "web_far.png"))
    rect = t.button(BR)["rect"]
    cx, cy = center(rect)
    for name, x, y, want in (("left49", rect["x"] - 49, cy, True), ("left50", rect["x"] - 50, cy, True), ("left51", rect["x"] - 51, cy, False),
                             ("up49", cx, rect["y"] - 49, True), ("up50", cx, rect["y"] - 50, True), ("up51", cx, rect["y"] - 51, False)):
        t.move(x, y)
        b = t.button(BR)
        t.check("pc." + name, b["shown"] is want and b["hitTest"] is want, {"x": x, "y": y, "shown": b["shown"], "opacity": b["opacity"]})
    t.move(rect["x"] - 30, cy)
    t.page.screenshot(path=os.path.join(shots, "web_near.png"))
    t.move(cx, cy)
    t.move(cx + 1, cy)
    on = t.button(BR)
    t.check("pc.on_button_full_opacity", on["shown"] is True and on["opacity"] == 1, on)  # 웹은 실제 포인터라 포인터 위 강조까지 잰다

    notify = t.call("panel.bounds")["NotificationPane"]
    x = rect["x"] + rect["w"] + 30
    t.move(x, cy)
    t.check("pc.over_other_panel", notify["x"] <= x and t.shown(BR) is True, {"x": x, "notify": notify})

    # 숨긴 버튼 자리에 이동 없이 누름만 합성한다(실제 마우스는 먼저 이동해 버튼이 보이므로 이 경우가 생기지 않는다)
    t.move(*t.far())
    lcx, lcy = center(t.button(BL)["rect"])
    t.page.evaluate(SYNTH, ["pointerdown", lcx, lcy, 1])
    t.page.evaluate(SYNTH, ["pointerup", lcx, lcy, 0])
    time.sleep(SETTLE_S)
    t.check("pc.hidden_click_not_claimed", not t.state()["owners"], t.state()["owners"])
    t.move(*t.far())

    # 실제 클릭 확장
    before = t.call("panel.bounds")
    t.move(lcx, lcy)
    t.check("pc.revealed_before_click", t.shown(BL) is True)
    t.page.mouse.down()
    t.page.mouse.up()
    time.sleep(0.6)
    owners = t.state()["owners"]
    after = t.call("panel.bounds")
    t.check("pc.click_claims", owners.get("BottomLeft") == "BottomPane", owners)
    t.check("pc.click_reaches_left", abs(after["BottomPane"]["x"] - before["LeftPane"]["x"]) <= 1.5, {"before": before["LeftPane"], "after": after["BottomPane"]})
    t.call("panel.corner.release", corner="BottomLeft")
    time.sleep(0.5)

    # 분할선 끌기: 눌린 채 움직이는 동안 숨김
    rect = t.button(BR)["rect"]
    cy = center(rect)[1]
    notify = t.call("panel.bounds")["NotificationPane"]
    sx = (rect["x"] + rect["w"] + notify["x"]) / 2
    t.move(sx, cy)
    t.check("pc.shown_over_splitter", t.shown(BR) is True, {"x": sx})
    t.page.mouse.down()
    for step in (4, 8, 12, 16, 20):
        t.page.mouse.move(sx - step, cy)
        time.sleep(0.05)
    time.sleep(SETTLE_S)
    during = t.button(BR)
    moved = t.call("panel.bounds")["NotificationPane"]
    t.page.mouse.up()
    time.sleep(SETTLE_S)
    t.check("pc.splitter_moved", abs(moved["x"] - notify["x"]) > 5, {"before": notify["x"], "during": moved["x"]})
    t.check("pc.hidden_during_drag", during["shown"] is False, during)
    rect = t.button(BR)["rect"]
    cy = center(rect)[1]

    # 투어가 덮는 동안
    t.call("tour.start", scope="main")
    time.sleep(1)
    t.move(rect["x"] - 30, cy)
    t.check("pc.hidden_during_tour", t.shown(BR) is False, t.button(BR))
    t.call("tour.end")
    time.sleep(0.8)
    t.move(rect["x"] - 31, cy)
    t.check("pc.shown_after_tour", t.shown(BR) is True)

    # 관리형 창이 버튼을 덮는 동안
    t.call("docs.open", kind="form", count=1)
    time.sleep(0.5)
    doc = t.call("docs.list")[-1]["id"]
    t.call("docs.sdi", doc=doc, on=True)
    time.sleep(0.8)
    placed = t.call("sdi.setBounds", doc=doc, x=int(rect["x"]) - 200, y=int(rect["y"]) - 200, w=400, h=400)
    time.sleep(0.5)
    t.move(rect["x"] - 30, cy)
    raw["managed"] = {"placed": placed, "windows": t.call("windows.list")}
    t.check("pc.hidden_under_managed_window", t.shown(BR) is False, raw["managed"])
    t.call("docs.sdi", doc=doc, on=False)
    time.sleep(0.5)
    t.move(*t.far())

    # 메뉴가 열린 동안: 메뉴가 버튼 자리를 덮지 않아도 거리 안에서 숨는다(실측. 그 클릭은 메뉴를 닫을 뿐이라 이 동작을 기대값으로 둔다)
    menu = t.call("ui.find", id="shell.mnuSys")["bounds"]
    t.page.mouse.click(menu["x"] + menu["w"] / 2, menu["y"] + menu["h"] / 2)
    time.sleep(0.8)
    t.page.screenshot(path=os.path.join(shots, "web_menu_open.png"))
    t.check("pc.hidden_with_pointer_on_menu", t.shown(BR) is False, t.button(BR))
    t.move(rect["x"] - 30, cy)
    raw["menu_open_near"] = t.button(BR)
    t.page.screenshot(path=os.path.join(shots, "web_menu_open_near.png"))
    t.check("pc.hidden_near_while_menu_open", raw["menu_open_near"]["shown"] is False, raw["menu_open_near"])
    t.page.mouse.click(*t.far())  # 바깥 클릭으로 메뉴를 닫는다
    time.sleep(0.5)

    # 창 떠남: 페이지 밖 좌표 이동을 먼저 보고, headless 가 떠남을 내지 않으면 문서 뿌리에 pointerleave 를 합성해 호스트 연결을 본다
    t.move(rect["x"] - 30, cy)
    t.check("pc.shown_before_leave", t.shown(BR) is True)
    t.page.mouse.move(-20, -20)
    time.sleep(SETTLE_S)
    raw["leave_real"] = t.shown(BR)
    if raw["leave_real"] is not False:
        t.page.evaluate("() => document.documentElement.dispatchEvent(new PointerEvent('pointerleave'))")
        time.sleep(SETTLE_S)
    t.check("pc.hidden_after_leave", t.shown(BR) is False, {"real_move": raw["leave_real"], "after": t.button(BR)})
    t.move(rect["x"] - 30, cy)
    t.check("pc.shown_on_return", t.shown(BR) is True)

    # 비용: 먼 곳 이동은 적중 검사를 늘리지 않는다
    a = t.state()["proximity"]["windows"][0]
    fx, fy = t.far()
    for i in range(100):
        t.page.mouse.move(fx + i, fy)
    time.sleep(SETTLE_S)
    b = t.state()["proximity"]["windows"][0]
    raw["cost"] = {"before": a, "after": b}
    t.check("pc.far_moves_no_hit_test", b["moves"] - a["moves"] >= 50 and b["hitTests"] == a["hitTests"], raw["cost"])
    return raw


def run_touch(t, shots):
    raw = {"coarse": t.page.evaluate("() => matchMedia('(pointer: coarse)').matches")}
    t.check("touch.coarse", raw["coarse"] is True, raw)
    s = t.state()
    raw["state"] = {"supported": s.get("supported"), "buttons": s.get("buttons"), "proximity": s.get("proximity")}
    t.check("touch.buttons_always_shown", s.get("buttons") and all(b["shown"] is True and b["hitTest"] is True and b["opacity"] > 0 for b in s["buttons"]), s.get("buttons"))
    t.check("touch.no_subscription", not any(w["subscribed"] for w in s["proximity"]["windows"]), s.get("proximity"))
    t.page.screenshot(path=os.path.join(shots, "web_touch.png"))
    return raw


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bundle", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--port", type=int, default=5811)
    a = p.parse_args()
    shots = os.path.dirname(os.path.abspath(a.out))
    os.makedirs(shots, exist_ok=True)
    http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
    socketserver.TCPServer.allow_reuse_address = True
    httpd = socketserver.TCPServer(("127.0.0.1", a.port), partial(http.server.SimpleHTTPRequestHandler, directory=a.bundle))
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    errors = []
    raw = {"errors": errors}
    items = []
    url = "http://127.0.0.1:%d/?fresh" % a.port
    try:
        with sync_playwright() as pw:
            browser = pw.chromium.launch(headless=True, args=ARGS)
            pc = Page(browser, url, errors, viewport={"width": 1400, "height": 900})
            raw["pc"] = run_pc(pc, shots)
            items += pc.items
            pc.context.close()
            touch = Page(browser, url, errors, viewport={"width": 1280, "height": 800}, has_touch=True, is_mobile=True)
            raw["touch"] = run_touch(touch, shots)
            items += touch.items
            browser.close()
    finally:
        httpd.shutdown()
        raw["checks"] = items
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=1)
    failed = [c["name"] for c in items if not c["pass"]]
    print("합계 %d, 실패 %d %s, 페이지 오류 %d" % (len(items), len(failed), failed, len(errors)))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
