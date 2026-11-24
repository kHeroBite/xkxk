#!/usr/bin/env python3
# 웹 PC 폭 패널 '위치' 메뉴 측정 — 데스크톱과 같은 항목 검사에 headless Chrome 안 Playwright 실우클릭·실클릭을 더해 메뉴 표시와 실행을 잰다(사용자 PC 입력과 무관)
"""
사용: python3 -B retest_panel_position_menu_web.py --bundle <wwwroot> --out <결과.json> --shots <폴더> [--port 5793] [--parts pc,reload,touch,phone]
종료 코드: 0=전부 통과, 1=실패 있음. logs/ui28/ 에는 쓰지 않는다(otest 전용).
"""
import argparse
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "desktop", "measure"))
from ui28_web import Web  # noqa: E402
import retest_panel_position_menu as core  # noqa: E402

SETTLE_S = 0.8


def center(w, ctl_id):
    return w.center(ctl_id)


def click_item(w, k, ctl_id, name):
    """열린 메뉴 항목을 실마우스로 누른다."""
    at = center(w, ctl_id)
    k.check(name + ".itemShown", at is not None, at)
    if at:
        w.page.mouse.move(*at)
        time.sleep(0.4)
        w.page.mouse.click(*at)
        time.sleep(SETTLE_S)
    return at is not None


def real_right_click(w, k):
    """제목 줄과 패널 탭을 실제로 우클릭해 메뉴가 뜨고 항목으로 옮겨지는지 본다. 브라우저 메뉴가 아니라 앱 메뉴여야 항목이 트리에 있다."""
    b = (w.res("ui.find", dock=core.NP) or {}).get("bounds")
    k.check("web.notifyChromeFound", bool(b), b)
    if not b:
        return
    w.page.mouse.click(b["x"] + 60, b["y"] + 12, button="right")
    time.sleep(SETTLE_S)
    w.shot("web_title_menu")
    if click_item(w, k, "panel.pos.menu", "web.title"):
        time.sleep(SETTLE_S)
        w.shot("web_title_submenu")
        click_item(w, k, "panel.pos.Edge.Left", "web.title.left")
    side = {p["id"]: p for p in w.res("panel.list")}[core.NP]["side"]
    k.check("web.title.moved", side == "Left", side)
    w.res("panel.dock", id=core.NP, side="right")
    time.sleep(SETTLE_S)

    doc = k.open_doc()
    w.res("panel.join", id=core.LP, doc=doc)
    time.sleep(SETTLE_S)
    tab = (w.res("panelmenu.read", tab=doc) or {}).get("hostBounds")
    k.check("web.tabFound", bool(tab), tab)
    if tab:
        w.page.mouse.click(tab["x"] + tab["w"] / 2, tab["y"] + tab["h"] / 2, button="right")
        time.sleep(SETTLE_S)
        w.shot("web_tab_menu")
        if click_item(w, k, "panel.pos.menu", "web.tab"):
            time.sleep(SETTLE_S)
            w.shot("web_tab_submenu")
            click_item(w, k, "panel.pos." + core.BP, "web.tab.bottom")
        tabs = k.tabs(core.BP)
        k.check("web.tab.moved", doc in tabs, tabs)
    k.click("Documents", tab=doc)
    w.res("docs.close", doc=doc)
    time.sleep(SETTLE_S)


def real_drag(w, k):
    """세 패널 제목 줄을 실마우스(headless Chrome)로 끌어 문서 영역 안내 방향에 놓는다. 끌기 뒤 시작 위치로 되돌린다."""
    d = (w.res("panel.bounds") or {}).get("Documents") or {}
    cx, cy = d.get("x", 0) + d.get("w", 0) / 2, d.get("y", 0) + d.get("h", 0) / 2
    for pane, home, want, to in ((core.NP, "right", "Left", (cx - 50, cy)), (core.LP, "left", "Top", (cx, cy - 50)), (core.BP, "bottom", "Right", (cx + 50, cy))):
        b = (w.res("ui.find", dock=pane) or {}).get("bounds") or {}
        m = w.page.mouse
        m.move(b["x"] + 40, b["y"] + 12)
        m.down()
        m.move(to[0], to[1], steps=14)
        time.sleep(0.2)
        m.move(to[0] + 1, to[1], steps=2)
        time.sleep(0.2)
        m.up()
        time.sleep(core.SETTLE_S + 0.4)
        p = k.panels()
        k.check("web.drag.%s.%s" % (pane, want), p[pane]["side"] == want and all(p[i]["visible"] for i in (core.LP, core.BP, core.NP)), {"side": p[pane]["side"]})
        w.res("panel.dock", id=pane, side=home)
        time.sleep(core.SETTLE_S)
    b = (w.res("ui.find", dock=core.LP) or {}).get("bounds") or {}
    t = (w.res("ui.find", dock=core.BP) or {}).get("bounds") or {}
    m = w.page.mouse
    m.move(b["x"] + 40, b["y"] + 12)
    m.down()
    m.move(t["x"] + 40, t["y"] + 12, steps=14)
    time.sleep(0.2)
    m.move(t["x"] + 42, t["y"] + 12, steps=2)
    time.sleep(0.2)
    m.up()
    time.sleep(core.SETTLE_S + 0.4)
    k.check("web.drag.join", "Explorer" in k.tabs(core.BP), k.tabs(core.BP))
    k.click(core.LP, tab="Explorer")


def goto(w, query):
    w.page.goto("http://127.0.0.1:%d/%s" % (w.port, query), wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    for _ in range(60):
        if w.call("status").get("ok"):
            break
        time.sleep(1)
    time.sleep(2)


def reload_keeps(w, k):
    """종료 저장이 없는 웹은 자동 저장으로 새로고침 뒤에 남아야 한다(D-NOTIPOS-SAVE). ?fresh 는 저장을 막으므로 뺀 주소로 연다."""
    goto(w, "")
    k.click("Edge.Left", pane=core.NP)
    k.click(core.BP, pane=core.LP)
    want = sorted(k.tabs(core.BP))
    time.sleep(4)  # 자동 저장 디바운스
    goto(w, "")
    p = k.panels()
    k.check("web.reload.edge", p[core.NP]["side"] == "Left", p[core.NP]["side"])
    k.check("web.reload.tabs", sorted(k.tabs(core.BP)) == want and not p[core.LP]["visible"], {"bottom": k.tabs(core.BP), "leftVisible": p[core.LP]["visible"]})
    k.check("web.reload.title", p[core.NP]["title"] == "알림(5건)", p[core.NP]["title"])


def touch_tablet(w, k):
    """--form-factor touch 태블릿 폭: 우클릭이 없으므로 머리줄 ▾ 가 같은 메뉴를 연다. 입력은 실마우스다(터치 이벤트 자체는 Android 시험 몫)."""
    w.page.set_viewport_size({"width": 1280, "height": 800})
    goto(w, "?fresh&form-factor=touch")
    w.shot("touch_start")
    for pane in (core.NP, core.LP, core.BP):
        k.check("touch.button." + pane, bool(k.menu(pane=pane).get("menuButton")), k.menu(pane=pane).get("menuButton"))
    ids = dict(core.tree_ids(w.res("tree.get", named=True, depth=60), []))
    k.check("touch.combo.removed", "notify.menu" not in ids and not ids.get("notify.count"), {"count": ids.get("notify.count")})
    b = k.menu(pane=core.NP).get("menuButton")
    if b:
        w.page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
        time.sleep(SETTLE_S)
        w.shot("touch_menu")
        if click_item(w, k, "panel.pos.menu", "touch.menu"):
            time.sleep(SETTLE_S)
            w.shot("touch_submenu")
            click_item(w, k, "panel.pos.Edge.Left", "touch.left")
        side = k.panels()[core.NP]["side"]
        k.check("touch.moved", side == "Left", side)
        w.res("panel.dock", id=core.NP, side="right")
        time.sleep(SETTLE_S)
    # 좁은 폭: 탐색기 몫이 160 아래로 가면 pdNarrow. ▾ 가 늘어도 제목 칸과 ✕ 가 남아야 한다
    w.page.set_viewport_size({"width": 800, "height": 1100})
    time.sleep(SETTLE_S * 2)
    m = k.menu(pane=core.LP)
    host, title, button = m.get("hostBounds") or {}, m.get("titleBounds") or {}, m.get("menuButton") or {}
    w.shot("touch_narrow")
    k.check("touch.narrow.fits", 0 < host.get("w", 0) < 160 and title.get("w", 0) >= 30 and button.get("w", 0) > 0 and button.get("x", 1e9) + button.get("w", 0) <= host.get("x", 0) + host.get("w", 0),
            {"chrome": host, "title": title, "button": button, "text": m.get("title")})


def phone(w, k):
    """폰 NAV: 패널 제목 줄이 없어 알림 화면 머리줄에 같은 제목을 보인다."""
    w.page.set_viewport_size({"width": 390, "height": 800})
    goto(w, "?fresh&form-factor=touch")
    w.res("ui.click", id="shell.btnNotify")
    time.sleep(SETTLE_S * 2)
    w.shot("phone_notify")
    found = w.res("ui.find", id="notify.count") or {}
    ids = dict(core.tree_ids(w.res("tree.get", named=True, depth=60), []))
    k.check("phone.titleShown", ids.get("notify.count") is True and found.get("text") == "알림(5건)", found.get("text"))
    k.check("phone.noCombo", "notify.menu" not in ids, None)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5793)
    ap.add_argument("--parts", default="pc,reload,touch,phone", help="부하가 높으면 나눠 돌린다")
    args = ap.parse_args()
    out = {}
    with sync_playwright() as p:
        w = Web(args.bundle, args.port, args.shots)
        w.open(p)
        k = core.Checks(w.res)
        time.sleep(2)
        out["viewport"] = w.viewport
        w.shot("web_start")
        if "pc" in args.parts:
            core.title_and_combo(k)
            core.panel_menus(k)
            core.edge_moves(k)
            doc = core.tab_moves(k)
            core.split_and_focus(k, doc)
            real_right_click(w, k)
            real_drag(w, k)
        if "reload" in args.parts:
            reload_keeps(w, k)
            w.shot("web_end")
        if "touch" in args.parts:
            touch_tablet(w, k)
        if "phone" in args.parts:
            phone(w, k)
        out["console_errors"] = w.errors[:20]
        w.close()
    out["items"], out["passed"], out["total"] = k.items, sum(i["pass"] for i in k.items), len(k.items)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("결과 %d/%d" % (out["passed"], out["total"]))
    return 0 if out["passed"] == out["total"] else 1


if __name__ == "__main__":
    sys.exit(main())
