#!/usr/bin/env python3
# 웹 보충 측정 — UI 형 전환(ui.style)과 mac·win 창 크롬의 관리형 창 오른쪽·아래 가장자리 크기 조절을 headless Chrome 실마우스로 잰다. 판정 없이 원자료만 낸다
"""
사용: python3 -B retest_web_modes_resize.py --bundle <wwwroot> --out <결과.json> --shots <폴더> [--port 5797]
mac 경로는 headless Chromium 의 navigator.platform 을 MacIntel 로 덮어 확인한다(실제 macOS Chrome 은 미측정).
"""
import argparse
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from ui28_web import Web  # noqa: E402


def doc_bounds(w):
    """MDI 문서의 페이지 좌표 경계(placement.state 의 area 원점 + 문서 bounds)."""
    st = w.res("placement.state") or {}
    ds = st.get("docs") or []
    if not ds:
        return None, {}
    d = ds[-1]
    a, b = st.get("area") or {}, d.get("bounds") or {}
    return d.get("id"), {"x": a.get("x", 0) + b.get("x", 0), "y": a.get("y", 0) + b.get("y", 0), "w": b.get("w"), "h": b.get("h")}


def edge_drag(w, label, shots):
    w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.5)
    did, b = doc_bounds(w)
    out = {"doc": did, "before": {k: b.get(k) for k in ("x", "y", "w", "h")}}
    if not b:
        return out
    m = w.page.mouse
    g = (w.res("ui.find", id="chrome.gripResize", doc=did) or {}).get("bounds") or {}
    out["grip"] = g
    if not g:
        return out
    # 크기 조절 손잡이(chrome.gripResize) 안쪽 점에서 가로만·세로만·대각선으로 끈다
    for name, dx, dy in (("right", 60, 0), ("bottom", 0, 50), ("diag", 40, 30)):
        _, cur = doc_bounds(w)
        g = (w.res("ui.find", id="chrome.gripResize", doc=did) or {}).get("bounds") or g
        sx, sy = g["x"] + g["w"] - 3, g["y"] + g["h"] - 3
        m.move(sx, sy)
        m.down()
        for i in range(1, 11):
            m.move(sx + dx * i / 10, sy + dy * i / 10)
            time.sleep(0.03)
        m.up()
        time.sleep(0.8)
        _, aft = doc_bounds(w)
        out[name] = {"start": [sx, sy], "before": {k: cur.get(k) for k in ("w", "h")}, "after": {k: aft.get(k) for k in ("w", "h")}, "dw": (aft.get("w") or 0) - (cur.get("w") or 0), "dh": (aft.get("h") or 0) - (cur.get("h") or 0)}
    w.shot(shots + "_" + label)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5797)
    a = ap.parse_args()
    out = {"modes": {}, "resize": {}}
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        w.open(p)
        time.sleep(2)
        out["modes"]["initial"] = w.res("ui.style")
        for s in ("pc", "tablet", "mobile", "auto"):
            r = w.res("ui.style", set=s)
            time.sleep(1.2)
            now = w.res("ui.style") or {}
            out["modes"][s] = {"set_result": r, "effective": now.get("effective"), "setting": now.get("setting")}
            w.shot("mode_" + s)
        w.res("ui.style", set="pc")
        time.sleep(1)
        out["resize"]["win"] = edge_drag(w, "win", "resize")
        out["console_errors_win"] = w.errors[:10]
        w.browser.close()
        # mac 크롬: platform 덮어쓰기
        w2 = Web(a.bundle, a.port + 1, a.shots)
        w2.browser = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"])
        w2.page = w2.browser.new_page(viewport={"width": 1400, "height": 900})
        w2.page.add_init_script("Object.defineProperty(navigator,'platform',{get:()=>'MacIntel'});Object.defineProperty(navigator,'userAgentData',{get:()=>({platform:'macOS',mobile:false,brands:[]})});")
        w2.page.on("console", lambda m: w2.errors.append(m.text[:300]) if m.type == "error" else None)
        w2.page.goto("http://127.0.0.1:%d/?fresh" % w2.port, wait_until="load", timeout=90000)
        w2.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
        for _ in range(60):
            if w2.call("status").get("ok"):
                break
            time.sleep(1)
        time.sleep(2)
        w2.res("ui.style", set="pc")
        time.sleep(1)
        out["resize"]["mac"] = edge_drag(w2, "mac", "resize")
        out["console_errors_mac"] = w2.errors[:10]
        w2.browser.close()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(json.dumps(out, ensure_ascii=False)[:2500])


if __name__ == "__main__":
    main()
