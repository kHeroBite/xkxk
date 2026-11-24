#!/usr/bin/env python3
# Android 관리형 SDI 창 합류·분할 측정(U15) — 본창 탭을 실터치로 끌어 창 가운데(합류)·위쪽(분할)에 놓고 탭 줄·칸·제목을 창 경계 안에서만 잰다
"""
사용: python3 -B retest_sdi_join_android.py --apk <진단 Debug APK> --serial emulator-5554 --out <결과 폴더> [--scenario fill,split]
결과는 <out>/sdi_join.json. 종료 코드 0=전부 통과, 1=실패 있음.
판정 범위는 windows.list 의 bounds(메인 TopLevel 좌표)다. inContainer 는 레이어 기준이라 본창 탭 줄을 창 안으로 오인한다(거짓 PASS 실측, otest-m-2 u15_5).
준비·정리는 retest_android.prepare·cleanup 이다. 에뮬레이터 기동·종료는 호출자 몫이다.
"""
import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ui28_android as U  # noqa: E402

ra, rr = U.ra, U.rr


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


def inside(bounds, box):
    return bounds and box["x"] - 1 <= bounds["x"] and box["y"] - 1 <= bounds["y"] \
        and bounds["x"] + bounds["w"] <= box["x"] + box["w"] + 1 and bounds["y"] + bounds["h"] <= box["y"] + box["h"] + 1


def window_view(c):
    """분리 창 1개의 경계 안 노드만 모은다. 창 틀(MdiDocumentWindow) 하위만 보므로 본창 탭 줄이 섞이지 않는다."""
    wins = [w for w in (c.call("windows.list") or []) if not w.get("main")]
    if not wins:
        return {"win": None}
    box = {k: wins[0]["bounds"][k] for k in ("x", "y", "w", "h")}
    frames = [n for n in walk(c.call("tree.get", named=False, depth=60).get("tree"), []) if n.get("type") == "MdiDocumentWindow"
              and {k: (n.get("bounds") or {}).get(k) for k in ("x", "y", "w", "h")} == box]
    nodes = walk(frames[0], []) if frames else []
    visible = [n for n in nodes if n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0]
    header = [n for n in nodes if n.get("name") == "PART_DocumentHeader"]
    titles = [x.get("text") for x in walk(header, []) if x.get("text") and x.get("effectivelyVisible")]  # 수정 표시 '*' 는 숨어 있다
    return {
        "win": wins[0], "box": box, "docs": wins[0].get("docs") or [], "title": titles[0] if titles else None,
        "strips": [n["bounds"] for n in visible if n.get("type") == "DocumentTabStrip"],
        "items": [{"bounds": n["bounds"], "text": [x.get("text") for x in walk(n, []) if x.get("text")][:1]} for n in visible if n.get("type") == "DocumentTabStripItem"],
        "content": [n["bounds"] for n in nodes if n.get("name") == "PART_ContentControl"],
        "docks": [n["bounds"] for n in visible if n.get("type") == "DocumentDockControl"],
    }


def prepare_docs(c):
    c.call("docs.close", all=True)
    for kind in ("grid", "gantt", "form"):
        c.call("docs.open", kind=kind)
    time.sleep(1.5)
    ids = [d["id"] for d in c.call("docs.list")]
    c.call("docs.sdi", doc=ids[0], on=True)
    time.sleep(1.5)
    c.call("docs.arrange", mode="tabbed")
    time.sleep(1.5)
    c.call("docs.activate", doc=ids[1])
    time.sleep(1.5)
    return ids


def drag_tab_to(c, title, scale, to_dip):
    """문서 영역 탭(작업 표시줄 탭 제외)을 실터치 swipe 로 끈다. 탭 글자 위치는 uiautomator 값이다."""
    xml = c.dump("tabs")
    points = []
    for m in re.finditer(r'text="%s"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"' % re.escape(title), xml):
        x1, y1, x2, y2 = map(int, m.groups())
        points.append(((x1 + x2) // 2, (y1 + y2) // 2))
    # 같은 글자가 작업 표시줄 탭(y≈155)과 아래 상태 줄(화면 맨 아래)에도 있다. 문서 영역 탭은 그 사이 가장 위쪽이다
    height = int(re.findall(r"(\d+)x(\d+)", c.adb("shell", "wm", "size")[1])[-1][1])
    source = sorted((p for p in points if 200 < p[1] < height * 0.8), key=lambda p: p[1])
    if not source:
        return {"points": points, "skipped": "문서 영역 탭 없음"}
    x, y = round(to_dip[0] * scale), round(to_dip[1] * scale)
    c.adb("shell", "input", "swipe", str(source[0][0]), str(source[0][1]), str(x), str(y), "1500")
    time.sleep(3)
    return {"from": source[0], "to": (x, y)}


def check_common(c, tag, view, titles):
    box = view.get("box")
    c.check("%s 창 안 내용이 창 경계를 넘지 않음" % tag, bool(box) and bool(view["content"]) and all(inside(b, box) for b in view["content"]), {"box": box, "content": view.get("content")}, "PART_ContentControl ⊂ 창")
    c.check("%s 창 제목이 문서 제목" % tag, view.get("title") in titles, view.get("title"), titles)


def fill(c, scale):
    """창 본문 가운데에 놓기 — 같은 칸의 탭으로 합류한다."""
    ids = prepare_docs(c)
    titles = {d["id"]: d["title"] for d in c.call("docs.list")}
    before = window_view(c)
    box = before["box"]
    c.check("F 합류 전 문서 1개 창은 탭 줄 없음", not before["strips"], before["strips"], [])
    head = 33  # 제목줄 32 + 틀 1
    moved = drag_tab_to(c, titles[ids[1]], scale, (box["x"] + box["w"] / 2, box["y"] + head + (box["h"] - head) / 2))
    view = window_view(c)
    c.shot("fill_after")
    c.check("F 합류 뒤 창 안 문서 2개", len(view["docs"]) == 2, {"moved": moved, "docs": view["docs"]}, 2)
    c.check("F 합류 뒤 창 안 탭 줄 1개·탭 2개", len(view["strips"]) == 1 and len(view["items"]) == 2, {"strips": view["strips"], "items": view["items"]}, "1, 2")
    c.check("F 합류 뒤 칸 나뉨 없음", len(view["docks"]) == 1, view["docks"], 1)
    main_strips = [n["bounds"] for n in walk(c.call("tree.get", named=False, depth=60).get("tree"), []) if n.get("type") == "DocumentTabStrip" and n.get("effectivelyVisible")
                   and (n.get("bounds") or {}).get("h", 0) > 0 and not inside(n["bounds"], view["box"])]
    c.check("F 창 밖 탭 줄은 본창 문서 영역 1개뿐(탭 1개 측면 패널은 숨김)", len(main_strips) == 1, main_strips, 1)
    check_common(c, "F", view, [titles[ids[0]], titles[ids[1]]])
    c.call("docs.redock", all=True)
    time.sleep(1)


def split(c, scale):
    """본문 가운데보다 위(위쪽 안내)에 놓기 — 위아래 칸으로 나뉜다. 두 칸이 모두 창 안에 보여야 한다."""
    ids = prepare_docs(c)
    titles = {d["id"]: d["title"] for d in c.call("docs.list")}
    box = window_view(c)["box"]
    head = 33
    moved = drag_tab_to(c, titles[ids[1]], scale, (box["x"] + box["w"] / 2, box["y"] + head + (box["h"] - head) / 2 - 30))  # 가운데 안내 바로 위(위쪽 안내)
    view = window_view(c)
    c.shot("split_after")
    c.check("S 놓은 뒤 창 안 문서 2개", len(view["docs"]) == 2, {"moved": moved, "docs": view["docs"]}, 2)
    if len(view["docks"]) == 2:
        c.check("S 두 칸 모두 창 경계 안", all(inside(b, view["box"]) for b in view["docks"]), {"box": view["box"], "docks": view["docks"]}, "칸 ⊂ 창")
        c.check("S 칸마다 탭 줄 보임", len(view["strips"]) == 2 and all(inside(b, view["box"]) for b in view["strips"]), view["strips"], 2)
    else:
        c.check("S 분할이 일어남(놓은 위치 값 확인)", False, {"moved": moved, "docks": view["docks"]}, 2)
    check_common(c, "S", view, [titles[ids[0]], titles[ids[1]]])
    c.call("docs.close", doc=ids[1])
    time.sleep(2)
    one = window_view(c)
    c.shot("split_close_one")
    c.check("S 한 문서를 닫으면 남은 문서 제목", one.get("title") == titles[ids[0]], one.get("title"), titles[ids[0]])
    c.check("S 한 문서를 닫으면 탭 줄 없음", not one.get("strips"), one.get("strips"), [])
    c.check("S 한 문서를 닫으면 내용이 창 안", bool(one.get("content")) and all(inside(b, one["box"]) for b in one["content"]), one.get("content"), "⊂ 창")
    c.call("docs.redock", all=True)
    time.sleep(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apk", required=True)
    ap.add_argument("--serial", default="emulator-5554")
    ap.add_argument("--out", required=True)
    ap.add_argument("--scenario", default="fill,split")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    c = U.Ctx(a.serial, "tablet", "Debug", a.out)
    state = {}
    try:
        if not ra.prepare(a.serial, a.apk, state):
            print("설치·연결 실패")
            return 1
        c.r = rr.Remote()
        U.style_wait(c, "Tablet")
        time.sleep(3)
        view_width = (c.call("ui.mode") or {}).get("viewWidth") or 800
        scale = int(re.findall(r"(\d+)x(\d+)", c.adb("shell", "wm", "size")[1])[-1][0]) / view_width
        for name, run in (("fill", fill), ("split", split)):
            if name in a.scenario.split(","):
                run(c, scale)
        count, text = c.fatal_count()
        c.check("X logcat FATAL·ANR(앱) 0건", count == 0, "%d %s" % (count, text), 0)
    finally:
        ra.cleanup(a.serial, state)
        json.dump({"apk": a.apk, "checks": c.checks}, open(os.path.join(a.out, "sdi_join.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    failed = [k for k in c.checks if not k["pass"]]
    print("PASS", len(c.checks) - len(failed), "FAIL", len(failed))
    for k in failed:
        print("FAIL", k["name"], json.dumps(k["value"], ensure_ascii=False)[:300])
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
