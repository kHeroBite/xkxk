#!/usr/bin/env python3
# 웹 관리형 SDI 창 합류·분할 측정(U15) — headless Chrome 실마우스로 본창 탭을 끌어 창 본문 가운데(합류)·위쪽(분할)에 놓고 탭 줄·칸·제목을 창 틀 안에서만 잰다
"""
사용: python3 -B retest_sdi_join_web.py --bundle <진단판 wwwroot> --out <결과 폴더> [--port 5824]
판정 범위는 windows.list 의 bounds 와 같은 경계의 MdiDocumentWindow 하위 노드다. inContainer 는 레이어 기준이라 쓰지 않는다. 종료 코드 0=통과, 1=실패.
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


def check(name, ok, detail=None):
    CHECKS.append({"name": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:260], flush=True)


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


def inside(b, box):
    return box["x"] - 1 <= b["x"] and box["y"] - 1 <= b["y"] and b["x"] + b["w"] <= box["x"] + box["w"] + 1 and b["y"] + b["h"] <= box["y"] + box["h"] + 1


def view(w):
    wins = [x for x in w.res("windows.list") or [] if not x.get("main")]
    if not wins:
        return {"box": None, "docs": [], "strips": [], "items": [], "docks": [], "content": [], "title": None}
    box = {k: wins[0]["bounds"][k] for k in ("x", "y", "w", "h")}
    tree = walk(w.res("tree.get", named=False, depth=60).get("tree"), [])
    frames = [n for n in tree if n.get("type") == "MdiDocumentWindow" and {k: (n.get("bounds") or {}).get(k) for k in box} == box]
    nodes = walk(frames[0], []) if frames else []
    shown = [n for n in nodes if n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0]
    title = [x.get("text") for n in nodes if n.get("name") == "PART_DocumentHeader" for x in walk(n, []) if x.get("text") and x.get("effectivelyVisible")]  # 수정 표시 '*' 는 숨어 있다
    return {
        "box": box, "docs": wins[0].get("docs") or [], "title": title[0] if title else None,
        "strips": [n["bounds"] for n in shown if n.get("type") == "DocumentTabStrip"],
        "items": [n["bounds"] for n in shown if n.get("type") == "DocumentTabStripItem"],
        "docks": [n["bounds"] for n in shown if n.get("type") == "DocumentDockControl"],
        "content": [n["bounds"] for n in nodes if n.get("name") == "PART_ContentControl"],
    }


def prepare(w):
    w.res("docs.close", all=True)
    for kind in ("grid", "gantt", "form"):
        w.res("docs.open", kind=kind)
    time.sleep(1.2)
    ids = [d["id"] for d in w.res("docs.list")]
    w.res("docs.sdi", doc=ids[0], on=True)
    time.sleep(1.2)
    w.res("docs.arrange", mode="tabbed")
    time.sleep(1.2)
    w.res("docs.activate", doc=ids[1])
    time.sleep(1.0)
    return ids, {d["id"]: d["title"] for d in w.res("docs.list")}


def drag_active_tab(w, box, to):
    """본창 문서 영역 탭 줄의 첫 탭을 끈다. 측면 패널 탭을 집지 않도록 창 밖에서 가장 위에 있는 탭 줄(메뉴 바로 아래 문서 영역)의 탭만 고른다."""
    tree = walk(w.res("tree.get", named=False, depth=120).get("tree"), [])  # 본창 문서 탭은 깊어서 60 이면 탭 줄 아래가 잘린다
    shown = [n["bounds"] for n in tree if n.get("type") == "DocumentTabStripItem" and n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0 and not inside(n["bounds"], box)]
    top = min((b["y"] for b in shown), default=None)
    tabs = sorted((b for b in shown if b["y"] == top), key=lambda b: b["x"])
    if not tabs:
        return {"skipped": "본창 문서 탭 없음"}
    x, y = tabs[0]["x"] + tabs[0]["w"] / 2, tabs[0]["y"] + tabs[0]["h"] / 2
    m = w.page.mouse
    m.move(x, y)
    m.down()
    m.move(to[0], to[1], steps=16)
    time.sleep(0.3)
    m.move(to[0] + 1, to[1], steps=2)
    time.sleep(0.3)
    m.up()
    time.sleep(1.5)
    return {"from": (x, y), "to": to}


def fill(w):
    ids, titles = prepare(w)
    one = view(w)
    check("F 합류 전 문서 1개 창은 탭 줄 없음", not one["strips"], one["strips"])
    head = 33  # 제목줄 32 + 틀 1
    box = one["box"]
    moved = drag_active_tab(w, box, (box["x"] + box["w"] / 2, box["y"] + head + (box["h"] - head) / 2))
    v = view(w)
    w.shot("fill_after")
    check("F 합류 뒤 창 안 문서 2개", len(v["docs"]) == 2, {"moved": moved, "docs": v["docs"]})
    check("F 탭 줄 1개·탭 2개", len(v["strips"]) == 1 and len(v["items"]) == 2, {"strips": v["strips"], "items": v["items"]})
    check("F 칸 나뉨 없음", len(v["docks"]) == 1, v["docks"])
    if not v["box"]:
        check("F 분리 창이 남아 있음", False, moved)
        return
    main_strips = [n["bounds"] for n in walk(w.res("tree.get", named=False, depth=60).get("tree"), []) if n.get("type") == "DocumentTabStrip" and n.get("effectivelyVisible")
                   and (n.get("bounds") or {}).get("h", 0) > 0 and not inside(n["bounds"], v["box"])]
    check("F 창 밖 탭 줄은 본창 문서 영역 1개뿐(탭 1개 측면 패널은 숨김)", len(main_strips) == 1, main_strips)
    check("F 내용이 창 경계 안", bool(v["content"]) and all(inside(b, v["box"]) for b in v["content"]), {"box": v["box"], "content": v["content"]})
    check("F 제목이 문서 제목", v["title"] in (titles[ids[0]], titles[ids[1]]), v["title"])
    w.res("docs.redock", all=True)
    time.sleep(1)


def split(w):
    ids, titles = prepare(w)
    box = view(w)["box"]
    head = 33
    moved = drag_active_tab(w, box, (box["x"] + box["w"] / 2, box["y"] + head + (box["h"] - head) / 2 - 30))  # 가운데 안내 바로 위(위쪽 안내)
    v = view(w)
    w.shot("split_after")
    check("S 문서 2개·두 칸", len(v["docs"]) == 2 and len(v["docks"]) == 2, {"moved": moved, "docs": v["docs"], "docks": v["docks"]})
    check("S 두 칸 모두 창 경계 안", bool(v["docks"]) and all(inside(b, v["box"]) for b in v["docks"]), {"box": v["box"], "docks": v["docks"]})
    check("S 칸마다 탭 줄 보임", len(v["strips"]) == 2, v["strips"])
    check("S 제목이 문서 제목", v["title"] in (titles[ids[0]], titles[ids[1]]), v["title"])
    w.res("docs.close", doc=ids[1])
    time.sleep(1.5)
    left = view(w)
    w.shot("split_close_one")
    check("S 한 문서를 닫으면 남은 문서 제목·탭 줄 없음", left["title"] == titles[ids[0]] and not left["strips"], {"title": left["title"], "strips": left["strips"]})
    w.res("docs.redock", all=True)
    time.sleep(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=5824)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    errors = []
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.out)
        w.open(p)
        w.page.set_viewport_size({"width": 1400, "height": 900})
        time.sleep(2)
        fill(w)
        split(w)
        errors = w.errors[:20]
        w.close()
    check("X 콘솔 오류 0건", not errors, errors)
    with open(os.path.join(a.out, "sdi_join.json"), "w", encoding="utf-8") as f:
        json.dump({"checks": CHECKS}, f, ensure_ascii=False, indent=1)
    failed = [c for c in CHECKS if not c["pass"]]
    print("결과 %d/%d" % (len(CHECKS) - len(failed), len(CHECKS)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
