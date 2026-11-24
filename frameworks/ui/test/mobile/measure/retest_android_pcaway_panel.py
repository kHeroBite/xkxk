#!/usr/bin/env python3
# Fold5 펼침 패널 메뉴 측정(F07·F09) — 패널 ▾·상단 메뉴의 하위 메뉴 사각형과 실터치, 탭 2개 패널의 탭 단위 이동 경로를 팝업 노드로 잰다
"""
사용: python3 -B retest_android_pcaway_panel.py --apk <Debug APK> --out <폴더> [--fresh] [--measure] [--doc <화면설계 03 경로>]
BACK 은 쓰지 않는다. 메뉴는 바깥 터치로 닫는다.
사각형은 OverlayPopupHost 노드로 잰다. 화면 차이는 상태 줄 FPS 글자에 오염되고, 위쪽으로 여는 하위 메뉴는 아래쪽 영역만 보면 놓친다(m2probe FALSE 의 원인).
F07 경로는 ▾ > 패널 위치 > panel.moveTab.Log > panel.moveTab.Log.Right 다. 탐색기 패널에 로그 탭을 합친 뒤 로그만 옮겨 탐색기가 남는지 본다. 항목이 없으면 tablet_tab_menu_touch 가 거짓이다(수정 전 값).
"""
import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_pcaway_lib as L  # noqa: E402

SEARCH_SCOPE = "F09 원문은 HISTORY.md 이월 후보의 '태블릿 m2probe 하위 메뉴 사각형' 이름뿐이다. 찾은 곳: HISTORY.md, session-env plans/handoff_otest-m-1.md, compact_handoff*.md, poc/ui/logs/otest-m-2·3 scripts/m2_probe.py, tablet_m2.json(패널1 로그 하위 메뉴 check=False)."


def nodes(txt):
    r = []
    for m in re.finditer(r"<node ([^>]*?)/?>", txt):
        a = m.group(1)
        b = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', a)
        if b:
            r.append({"text": (re.search(r'text="([^"]*)"', a) or [0, ""])[1], "id": (re.search(r'resource-id="([^"]*)"', a) or [0, ""])[1],
                      "cls": (re.search(r'class="([^"]*)"', a) or [0, ""])[1].split(".")[-1], "b": tuple(map(int, b.groups()))})
    return r


def popups(ns):
    out = []
    for n in ns:
        if n["cls"] == "OverlayPopupHost" and n["b"] not in out:
            out.append(n["b"])
    return out


def inside(b, box):
    return box[0] <= b[0] and box[1] <= b[1] and b[2] <= box[2] and b[3] <= box[3]


def items(ns, box):
    seen, res = set(), []
    for n in ns:
        if n["cls"] == "MenuItem" and inside(n["b"], box) and n["b"] not in seen:
            seen.add(n["b"])
            res.append({"b": n["b"], "id": n["id"], "text": n["text"]})
    return res


class Run:
    def __init__(self, c):
        self.c = c
        self.w, self.h = L.wm_size()
        self.off = c.bounds_top(c.dump(), "android:id/content")

    def on_screen(self, b):
        return 0 <= b[0] and 0 <= b[1] and b[2] <= self.w and b[3] <= self.h

    def tap(self, b):
        L.sh("input tap %d %d" % ((b[0] + b[2]) // 2, (b[1] + b[3]) // 2 + self.off))

    def dismiss(self):
        L.sh("input tap %d %d" % (self.w - 150, 80 + self.off))
        time.sleep(1)
        if popups(nodes(self.c.dump())):
            L.sh("input tap %d %d" % (self.w // 2, self.h // 2))
            time.sleep(1)

    def open_sub(self, btn, name):
        """▾ → 첫 항목(패널 위치) → 하위 메뉴. 반환 (메뉴 사각형, 메뉴 항목, 하위 사각형, 하위 항목)."""
        self.tap(btn)
        time.sleep(1.6)
        n1 = nodes(self.c.dump("%s_menu" % name))
        self.c.shot("%s_menu" % name)
        p1 = popups(n1)
        if not p1:
            return None, [], None, []
        its = items(n1, p1[-1])
        pos = [i for i in its if "위치" in i["text"]] or its
        if not pos:
            return p1[-1], its, None, []
        self.tap(pos[0]["b"])
        time.sleep(2)
        n2 = nodes(self.c.dump("%s_sub" % name))
        self.c.shot("%s_sub" % name)
        p2 = [b for b in popups(n2) if b not in p1]
        return p1[-1], its, (p2[-1] if p2 else None), (items(n2, p2[-1]) if p2 else [])


def round_panels(r, tag):
    rep = []
    btns = [n for n in nodes(r.c.dump()) if n["id"] == "PART_MenuButton"]
    for k, btn in enumerate(btns):
        m, its, s, sub = r.open_sub(btn["b"], "%s_pm%d" % (tag, k))
        rec = {"k": k, "btn": btn["b"], "menu": m, "menu_items": [i["text"] for i in its], "sub": s, "sub_items": [i["text"] for i in sub]}
        rec["in_screen"] = bool(m and s) and r.on_screen(m) and r.on_screen(s) and all(r.on_screen(i["b"]) and inside(i["b"], s) for i in sub)
        rep.append(rec)
        print(tag, json.dumps(rec, ensure_ascii=False)[:400], flush=True)
        r.dismiss()
    return rep


def tab_move(r):
    """탭 2개 패널에서 탭 단위 이동. 메뉴에 경로가 없으면 그대로 None 을 돌려준다."""
    r.c.call("panel.join", id="LeftPane", tool="Log")
    time.sleep(1.5)
    before = {p["id"]: (p["side"], [t["id"] for t in p["tabs"]]) for p in r.c.call("panel.list")}
    btn = [n for n in nodes(r.c.dump()) if n["id"] == "PART_MenuButton"][0]
    r.tap(btn["b"])
    time.sleep(1.6)
    n1 = nodes(r.c.dump("F07_menu"))
    p1 = popups(n1)
    its = items(n1, p1[-1]) if p1 else []
    res = {"before": before, "menu": [i["text"] for i in its], "path": False, "cap": r.c.shot("F07_menu")}
    pos = [i for i in its if "위치" in i["text"]]
    if not pos:
        r.dismiss()
        return res
    # 구현(PanelPositionMenu.AddTabMoveItems): ▾ > 패널 위치 > panel.moveTab.<탭 Id> > panel.moveTab.<탭 Id>.<Left|Right|Top|Bottom|Float>
    r.tap(pos[0]["b"])
    time.sleep(2)
    n2 = nodes(r.c.dump("F07_sub"))
    p2 = [b for b in popups(n2) if b not in p1]
    sub = items(n2, p2[-1]) if p2 else []
    res["sub"] = [(i["id"], i["text"]) for i in sub]
    res["cap"] = r.c.shot("F07_sub")
    hit = [i for i in sub if i["id"] == "panel.moveTab.Log"]
    if not hit:
        r.dismiss()
        return res
    res["path"] = True
    r.tap(hit[0]["b"])
    time.sleep(2)
    n3 = nodes(r.c.dump("F07_tab"))
    p3 = [b for b in popups(n3) if b not in p1 and b not in p2]
    tabsub = items(n3, p3[-1]) if p3 else []
    res["tab_sub"] = [(i["id"], i["text"]) for i in tabsub]
    res["tab_cap"] = r.c.shot("F07_tab")
    pick = [i for i in tabsub if i["id"] == "panel.moveTab.Log.Right"]
    if pick:
        r.tap(pick[0]["b"])
        time.sleep(2.5)
    res["after"] = {p["id"]: (p["side"], [t["id"] for t in p["tabs"]]) for p in r.c.call("panel.list")}
    res["after_cap"] = r.c.shot("F07_after")
    return res


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    p.add_argument("--doc")
    p.add_argument("--diag-release", action="store_true", help="진단 Release APK 는 run-as 가 안 돼 logcat 의 원격 제어 토큰으로 붙는다(back 도구와 같은 옵션)")
    a = p.parse_args()
    c = L.Ctx("tablet", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_open()
    if not L.install(a.apk, fresh=a.fresh):
        return 1
    L.sh("pm clear %s" % L.PKG)  # 앞 회차가 저장한 패널 배치를 지워 로그 패널이 아래쪽에 있게 한다
    if not L.attach(c, token_from_logcat=a.diag_release):
        return 1
    L.U.style_wait(c, "Tablet", 40)
    c.call("docs.close", all=True)
    c.call("docs.open", kind="grid")
    time.sleep(1.5)
    r = Run(c)
    rep = round_panels(r, "F09")
    c.raw["panel_menus"] = rep
    move = tab_move(r)
    c.raw["tab_move"] = move
    print("tab_move", json.dumps(move, ensure_ascii=False)[:500], flush=True)
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "f07_f09")
    if a.measure:
        src = "retest_android_pcaway_panel.py " + raw
        F09 = {
            "submenu_rect_in_screen": ([(x["sub"], x["in_screen"]) for x in rep], len(rep) >= 3 and all(x["in_screen"] for x in rep)),
            "submenu_touch_opens": ([x["sub_items"] for x in rep], len(rep) >= 3 and all(len(x["sub_items"]) >= 4 for x in rep)),
            "search_scope_recorded": (SEARCH_SCOPE, True),
        }
        L.write_measure(a.out, "F09", F09, src, [os.path.join(a.out, "F09_pm1_sub.png")])
        after = move.get("after") or {}
        left = after.get("LeftPane", ("", []))
        moved = bool(move["path"] and after) and "Log" not in left[1] and any("Log" in v[1] for k, v in after.items() if k != "LeftPane")
        stayed = moved and "Explorer" in left[1] and left[0] == move["before"]["LeftPane"][0]
        doc_ok = False
        if a.doc and os.path.isfile(a.doc):  # 문서 188행의 탭 우클릭 줄은 이 경로가 아니므로 ▾ 와 같은 줄에 있어야 인정한다
            doc_ok = any("▾" in ln and "이 탭 옮기기" in ln for ln in open(a.doc, encoding="utf-8"))
        F07 = {
            "tablet_tab_menu_touch": (move["menu"], move["path"]),
            "single_tab_moved": ({"before": move["before"], "after": after}, bool(moved)),
            "other_tabs_stay": (after.get("LeftPane"), stayed),
            "doc_updated": (a.doc, doc_ok),
        }
        L.write_measure(a.out, "F07", F07, src, [move["cap"]])
    return 0


if __name__ == "__main__":
    sys.exit(main())
