# 웹 측면 패널 📌 왕복(고정→자동 숨김→다시 고정)과 ✕ 동작 판정 — 머리줄 버튼·패널 bounds·Pin 관련 노드 덤프와 단계별 캡처
# 사용: PD_URL=http://127.0.0.1:5215/ PD_OUT=<폴더> python3 tools/retest_browser29.py   (진단판·헤드리스 전용)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from retest_browser11 import _walk, full_tree  # noqa: E402

OUT = t.OUT
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, json.dumps(actual, ensure_ascii=False)[:700]), flush=True)


def chrome(r):
    tr = full_tree(r)
    out, pins = [], []
    for n, _ in _walk(tr.get("tree"), []):
        ty = str(n.get("type") or "")
        if ty == "ToolChromeControl" and n.get("visible") is not False:
            btns = [{"name": c.get("name"), "bounds": c.get("bounds")} for c, _ in _walk(n.get("children"), [])
                    if c.get("type") in ("Button", "ToggleButton") and c.get("visible")]
            out.append({"bounds": n.get("bounds"), "buttons": btns})
        if "Pin" in ty and n.get("visible") is not False and n.get("bounds"):
            pins.append({"type": ty, "bounds": n.get("bounds"), "text": n.get("text")})
    return out, pins


def click(page, bb):
    page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)


def btn(c, name):
    return next((x for x in c["buttons"] if x["name"] == name), None)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-repin", ctx=ctx)
        time.sleep(1.0)
        c0, p0 = chrome(r)
        page.screenshot(path=os.path.join(OUT, "repin_0_initial.png"))
        left = min(c0, key=lambda c: ((c["bounds"] or {}).get("x", 9999), (c["bounds"] or {}).get("y", 9999)))  # 가장 왼쪽 = 탐색기
        lb0 = left["bounds"]
        pin = btn(left, "PART_PinButton")
        click(page, pin["bounds"])
        time.sleep(1.0)
        c1, p1 = chrome(r)
        page.screenshot(path=os.path.join(OUT, "repin_1_autohide.png"))
        rec("A1 📌 → 자동 숨김(기록)", "탐색기 머리줄 사라지고 가장자리 탭(Pin 노드) 생김", {"before_panels": len(c0), "after_panels": len(c1), "pin_nodes": p1[:8]}, "기록")
        # 가장자리 탭을 눌러 펼친 뒤 📌 로 다시 고정
        tabs = [x for x in p1 if x["bounds"]["x"] < 60 and x["bounds"]["w"] > 0 and x["bounds"]["h"] > 0]
        tabs.sort(key=lambda x: x["bounds"]["w"] * x["bounds"]["h"])
        tab = tabs[0] if tabs else None
        flyout, c2 = None, []
        if tab:
            click(page, tab["bounds"])
            time.sleep(1.0)
            c2, _ = chrome(r)
            page.screenshot(path=os.path.join(OUT, "repin_2_flyout.png"))
            flyout = min(c2, key=lambda c: ((c["bounds"] or {}).get("x", 9999), (c["bounds"] or {}).get("y", 9999))) if c2 else None
        pin2 = btn(flyout, "PART_PinButton") if flyout else None
        c3 = []
        if pin2:
            click(page, pin2["bounds"])
            time.sleep(1.2)
            c3, p3 = chrome(r)
            page.screenshot(path=os.path.join(OUT, "repin_3_repinned.png"))
        lb3 = min(c3, key=lambda c: ((c["bounds"] or {}).get("x", 9999), (c["bounds"] or {}).get("y", 9999)))["bounds"] if c3 else None
        ok = bool(c3) and len(c3) == len(c0) and lb3 and abs(lb3["x"] - lb0["x"]) <= 2 and abs(lb3["w"] - lb0["w"]) <= 4 and abs(lb3["h"] - lb0["h"]) <= 4
        rec("A2 자동 숨김 → 펼침 → 📌 다시 고정: 처음 고정 자리·크기로 복귀", "패널 수 처음과 같음, 탐색기 bounds x·w·h 처음과 ±2~4",
            {"tab": tab, "flyout_bounds": (flyout or {}).get("bounds"), "pin2": pin2, "c2": c2, "c3": c3, "initial": lb0, "repinned": lb3, "panels": [len(c0), len(c2), len(c3)]},
            "PASS" if ok else "FAIL")
        # ✕: 가장 왼쪽(탐색기) 패널 닫기
        cA, _ = chrome(r)
        left = min(cA, key=lambda c: ((c["bounds"] or {}).get("x", 9999), (c["bounds"] or {}).get("y", 9999)))
        cl = btn(left, "PART_CloseButton")
        cB = []
        if cl:
            click(page, cl["bounds"])
            time.sleep(1.0)
            cB, pB = chrome(r)
            page.screenshot(path=os.path.join(OUT, "repin_4_closed.png"))
        menu = None
        rec("A3 ✕ → 패널 닫힘(머리줄 수 1 감소, 가장자리 탭으로 남지 않음)", "패널 수 1 감소, 닫힌 패널 자리 bounds 없음",
            {"close_btn": cl, "panels_before": len(cA), "panels_after": len(cB), "after_bounds": [c["bounds"] for c in cB], "menuHeaders": menu},
            "PASS" if cl and len(cB) == len(cA) - 1 and not any(abs((c["bounds"] or {}).get("x", -99) - left["bounds"]["x"]) <= 2 and abs((c["bounds"] or {}).get("y", -99) - left["bounds"]["y"]) <= 2 for c in cB) else "FAIL")
        with open(os.path.join(OUT, "retest_web29_repin.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
