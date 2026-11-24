#!/usr/bin/env python3
# 패널 탭을 SDI 창에 넣은 뒤 복구 측정(D-PANELLOST) — 보기 메뉴로 되찾기, SDI 창 닫기 뒤 위치·복구, 새로고침 복원을 도구마다 값으로 남긴다
"""
사용: python3 -B retest_panel_in_sdi_web.py --bundle <진단판 wwwroot> --out <결과 폴더> [--port 5827] [--tools Notifications,Explorer,Log]
넣기는 진단 split.do(Dock 끌어 놓기와 같은 실행 경로), 닫기는 창 틀 ✕ 실마우스, 보기 메뉴는 panel.show(메뉴와 같은 ShowTool)다. 종료 코드 0=통과, 1=실패.
"""
import argparse
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui28_web import Web  # noqa: E402

CHECKS = []
HOME = {"Notifications": "NotificationPane", "Explorer": "LeftPane", "Log": "BottomPane"}


def check(name, ok, detail=None):
    CHECKS.append({"name": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:300], flush=True)


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


def state(w):
    panels = w.res("panel.list") or []
    return {
        "panels": {p["id"]: {"visible": p.get("visible"), "tabs": p.get("tabs")} for p in panels},
        "docs": [{"id": d.get("id"), "floating": d.get("floating")} for d in w.res("docs.list") or []],
        "windows": [{"title": x.get("title"), "docs": x.get("docs")} for x in w.res("windows.list") or [] if not x.get("main")],
        "frames": sum(1 for n in walk(w.res("tree.get", named=False, depth=60).get("tree"), []) if n.get("type") == "MdiDocumentWindow" and n.get("effectivelyVisible")),
    }


def in_panel(st, tool):
    """보이는 패널의 탭에 도구가 있는가. tabs 항목은 id 문자열이거나 {id} 다."""
    for panel in st["panels"].values():
        ids = [t.get("id") if isinstance(t, dict) else t for t in panel.get("tabs") or []]
        if panel.get("visible") and tool in ids:
            return True
    return False


def setup(w, tool):
    w.res("docs.close", all=True)
    w.res("docs.open", kind="grid")
    time.sleep(1)
    doc = w.res("docs.list")[0]["id"]
    w.res("docs.sdi", doc=doc, on=True)
    time.sleep(1.2)
    result = w.call("split.do", target="doc:" + doc, tool=tool, op="Fill")
    time.sleep(1.5)
    return doc, {k: (result.get("result") or result).get(k) for k in ("status", "validate")}


def click_close(w):
    frames = [n for n in walk(w.res("tree.get", named=False, depth=60).get("tree"), []) if n.get("type") == "MdiDocumentWindow" and n.get("effectivelyVisible")]
    buttons = [n["bounds"] for f in frames for n in walk(f, []) if n.get("name") == "PART_CloseButton" and n.get("effectivelyVisible")]
    if not buttons:
        return None
    b = buttons[-1]
    w.page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
    time.sleep(1.5)
    return b


def show(w, tool):
    shown = w.res("panel.show", tool=tool)
    time.sleep(1.2)
    return shown


def reset(w, tool):
    w.res("docs.redock", all=True)
    w.res("panel.show", tool=tool)
    w.res("docs.close", all=True)
    time.sleep(1)


def load(w, query):
    w.page.goto("http://127.0.0.1:%d/%s" % (w.port, query), wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    time.sleep(5)


def scenario(w, tool):
    doc, put = setup(w, tool)
    st = state(w)
    w.shot("%s_in_sdi" % tool)
    check("%s 넣기 뒤 창에 있음(패널에는 없음)" % tool, not in_panel(st, tool), {"put": put, "state": st})

    # ① 보기 메뉴로 되찾기
    shown = show(w, tool)
    st = state(w)
    w.shot("%s_view_menu" % tool)
    check("%s ① 보기 메뉴로 패널에 돌아옴" % tool, in_panel(st, tool), {"shown": shown, "state": st})
    reset(w, tool)

    # ② 창 닫기 뒤
    doc, put = setup(w, tool)
    closed = click_close(w)
    after = state(w)
    w.shot("%s_closed" % tool)
    extra = 0
    while after["frames"] and extra < 3:  # 문서 탭을 닫아도 창이 남으면 남은 항목까지 닫는다
        click_close(w)
        after = state(w)
        extra += 1
    w.shot("%s_closed_all" % tool)
    where = in_panel(after, tool)
    shown = show(w, tool)
    back = state(w)
    w.shot("%s_closed_view_menu" % tool)
    check("%s ② 창을 닫은 뒤 보기 메뉴로 패널에 돌아옴" % tool, in_panel(back, tool), {"closed": closed, "extraClicks": extra, "afterClose": after, "inPanelAfterClose": where, "shown": shown, "back": back})
    reset(w, tool)

    # ③ 넣은 채 새로고침. ?fresh 는 저장·복원을 하지 않으므로(main.js) 저장소를 비우고 일반 주소로 다시 시작해 잰다
    w.page.evaluate("() => localStorage.clear()")
    load(w, "")
    doc, put = setup(w, tool)
    saved = w.res("layout.save")  # 자동 저장 슬롯(종료 자동 저장과 같은 성격)
    time.sleep(1)
    w.page.reload(wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    time.sleep(5)
    st = state(w)
    w.shot("%s_reload" % tool)
    check("%s ③ 새로고침 뒤 문서 복원" % tool, [d["id"] for d in st["docs"]] == [doc], {"saved": saved, "docs": st["docs"], "windows": st["windows"]})
    shown = show(w, tool)
    back = state(w)
    w.shot("%s_reload_view_menu" % tool)
    check("%s ③ 새로고침 뒤 보기 메뉴로 패널에 있음" % tool, in_panel(back, tool), {"afterReload": st, "shown": shown, "back": back})
    w.page.evaluate("() => localStorage.clear()")
    load(w, "?fresh")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=5827)
    ap.add_argument("--tools", default="Notifications,Explorer,Log")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.out)
        w.open(p)
        time.sleep(2)
        for tool in a.tools.split(","):
            scenario(w, tool)
        errors = w.errors[:20]
        w.close()
    check("X 콘솔 오류 0건", not errors, errors)
    with open(os.path.join(a.out, "panel_in_sdi.json"), "w", encoding="utf-8") as f:
        json.dump({"checks": CHECKS}, f, ensure_ascii=False, indent=1)
    failed = [c for c in CHECKS if not c["pass"]]
    print("결과 %d/%d" % (len(CHECKS) - len(failed), len(CHECKS)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
