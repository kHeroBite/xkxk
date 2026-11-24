#!/usr/bin/env python3
# 웹 마감 3건 측정 — ① 탐색기·알림·로그 세 패널 제목 줄 실끌기(네 방향) ② 폰 폭 서랍 항목에 '_' 표기 없음 ③ 스피너 활성 중 포인터가 페이지 밖으로 나갈 때 추적 스피너 숨김. 판정 없이 원자료만 낸다
"""
사용: python3 -B retest_web_last3.py --bundle <wwwroot> --out <결과.json> --shots <폴더> [--port 5799]
입력은 headless Chrome 안 Playwright 실마우스다(사용자 PC 입력과 무관).
"""
import argparse
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from ui28_web import Web, changed, box_at  # noqa: E402

NP, LP, BP = "NotificationPane", "LeftPane", "BottomPane"
HOME = {NP: "right", LP: "left", BP: "bottom"}


def panels(w):
    return {p["id"]: p for p in w.res("panel.list") or []}


def drag4(w, out):
    d = (w.res("panel.bounds") or {}).get("Documents") or {}
    cx, cy = d.get("x", 0) + d.get("w", 0) / 2, d.get("y", 0) + d.get("h", 0) / 2
    targets = {"Left": (cx - 50, cy), "Top": (cx, cy - 50), "Right": (cx + 50, cy), "Bottom": (cx, cy + 50)}
    rows = []
    for pane in (LP, NP, BP):
        for want, to in targets.items():
            b = (w.res("ui.find", dock=pane) or {}).get("bounds") or {}
            m = w.page.mouse
            m.move(b["x"] + 40, b["y"] + 12)
            m.down()
            m.move(to[0], to[1], steps=14)
            time.sleep(0.2)
            m.move(to[0] + 1, to[1], steps=2)
            time.sleep(0.2)
            m.up()
            time.sleep(1.0)
            p = panels(w)
            rows.append({"pane": pane, "want": want, "side": p[pane]["side"], "visible": {i: p[i]["visible"] for i in (LP, NP, BP)}, "ok": p[pane]["side"] == want})
            w.res("panel.dock", id=pane, side=HOME[pane])
            time.sleep(0.8)
    w.shot("drag4_end")
    out["drag4"] = rows


def drawer(w, p, out, bundle_port):
    w.page.set_viewport_size({"width": 390, "height": 800})
    w.page.goto("http://127.0.0.1:%d/?fresh&form-factor=touch" % bundle_port, wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    time.sleep(3)
    clicked = w.res("ui.click", id="shell.btnDrawer")
    time.sleep(1.2)
    w.shot("phone_drawer")
    ids = []

    def walk(n):
        if isinstance(n, dict):
            if n.get("id") and str(n["id"]).startswith("shell.navDrawer"):
                ids.append(str(n["id"]))
            for v in n.values():
                walk(v)
        elif isinstance(n, list):
            for v in n:
                walk(v)

    walk(w.res("tree.get", named=True, depth=60))
    items = []
    for i in ids:
        f = w.res("ui.find", id=i) or {}
        items.append({"id": i, "text": f.get("text"), "visible": f.get("visible", f.get("effectivelyVisible"))})
    nav = w.res("nav.menu") or {}
    out["drawer"] = {"clicked": clicked, "items": items, "nav_menu_drawer_ids": [d.get("id") for d in nav.get("drawer", [])],
                     "underscore_ids": [i for i in ids if "_" in i.split("shell.navDrawer", 1)[1]],
                     "underscore_texts": [x for x in items if x.get("text") and "_" in str(x["text"])]}


def follower(w, out):
    w.page.set_viewport_size({"width": 1400, "height": 900})
    inside, far_out = (700, 450), (-20, -20)
    w.page.mouse.move(*inside)
    time.sleep(0.4)
    tok = (w.res("busy.begin", scope="main", reason="last3-follower") or {}).get("token")
    time.sleep(1.2)
    w.page.mouse.move(300, 250, steps=6)
    time.sleep(0.6)
    s_in = w.shot("follower_inside")
    st_in = w.res("busy.state") or {}
    # ① 마우스를 뷰포트 밖 좌표로 이동(실마우스)
    w.page.mouse.move(*far_out, steps=6)
    time.sleep(0.8)
    s_out = w.shot("follower_out_mouse")
    st_out = w.res("busy.state") or {}
    # ② 문서에 mouseleave·pointerleave(relatedTarget=null) 합성 — 브라우저 창 밖으로 나갈 때와 같은 이벤트 모양
    w.page.mouse.move(300, 250, steps=3)
    time.sleep(0.6)
    w.page.evaluate("() => { for (const t of ['pointerout','mouseout','pointerleave','mouseleave']) document.dispatchEvent(new PointerEvent(t, {bubbles: true, relatedTarget: null, clientX: -1, clientY: -1})); document.documentElement.dispatchEvent(new MouseEvent('mouseleave', {relatedTarget: null})); }")
    time.sleep(0.8)
    s_leave = w.shot("follower_out_synth")
    st_leave = w.res("busy.state") or {}
    box = box_at(300, 250)
    # ③ 문서 뿌리(documentElement)의 pointerleave — PagePointerLeave 가 구독하는 바로 그 이벤트. headless 는 포인터를 실제로 창 밖으로 낼 수 없으므로 합성한다(실제 창 밖 이동은 사람 확인)
    w.page.mouse.move(300, 250, steps=3)
    time.sleep(0.6)
    st_before_root = w.res("busy.state") or {}
    w.page.evaluate("() => document.documentElement.dispatchEvent(new PointerEvent('pointerleave', {bubbles: false, relatedTarget: null, pointerType: 'mouse'}))")
    time.sleep(0.8)
    s_root = w.shot("follower_out_root")
    st_root = w.res("busy.state") or {}
    w.page.mouse.move(320, 260, steps=3)
    time.sleep(0.6)
    st_back = w.res("busy.state") or {}
    out["follower_root_leave"] = {
        "before": st_before_root.get("followerVisible"), "after_leave": st_root.get("followerVisible"), "after_return": st_back.get("followerVisible"),
        "px_vs_inside_at_old_pos": changed(s_root, s_in, box),
    }
    out["follower"] = {
        "inside": {"followerVisible": st_in.get("followerVisible"), "cursorMode": st_in.get("cursorMode")},
        "out_mouse": {"followerVisible": st_out.get("followerVisible"), "px_vs_inside_at_old_pos": changed(s_out, s_in, box)},
        "out_synth_leave": {"followerVisible": st_leave.get("followerVisible"), "px_vs_inside_at_old_pos": changed(s_leave, s_in, box)},
        "spinner_still_running_in_main": [o.get("spinnerRunning") for sc in st_leave.get("scopes", []) for o in sc.get("overlays", [])],
        "box": box,
    }
    if tok is not None:
        w.call("busy.end", token=tok)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5799)
    ap.add_argument("--parts", default="drag4,follower,drawer", help="쉼표 구분. 스피너만 재면 follower")
    a = ap.parse_args()
    out = {}
    parts = a.parts.split(",")
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        w.open(p)
        time.sleep(2)
        if "drag4" in parts:
            drag4(w, out)
        if "follower" in parts:
            follower(w, out)
        if "drawer" in parts:
            drawer(w, p, out, a.port)
        out["console_errors"] = w.errors[:20]
        w.close()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(json.dumps(out, ensure_ascii=False)[:3500])


if __name__ == "__main__":
    main()
