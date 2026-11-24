#!/usr/bin/env python3
# 제목 줄 2차 묶음 측정(E1~E5) — SDI 아이콘·제목 탭 너비·줄 높이·태블릿형 중복 탭·SDI 활성 색을 진단판 복사본에서 실입력과 원격 값으로 재 pcaway_gate 형식 결과를 쓴다. 구현 전 판본이면 FAIL 이 정상이며 그 값이 before 기록이다
"""
사용:
  python3 -B retest_title_strip.py --items E1,E2,E3,E4,E5 --run-dir <bin/run/<복사본>/net10.0> --phase discovery --writer otest-d
  python3 -B retest_title_strip.py --annotate <결과.json> --capture all --read-by otest-d --finding '<판독 내용 10자 이상>'
  python3 -B retest_title_strip.py --self-test
E5 의 before_fix_reproduced: discovery 는 활성 색이 실제로 틀리면 통과(재현 기록)다. final 은 discovery/E5.json 의 재현 값을 읽어 통과 여부를 정한다. 값을 고쳐 쓰지 않는다.
기준(정본 plan/09_화면설계/01_화면설계_공통원칙.md §2-1): 제목 줄 28, 제목 탭 24, 탭 폭 72~240, 메뉴 띠 32 그대로, 아이콘 AutomationId shell.sdiIcon.{id}.
안전 규칙과 종료 코드는 retest_real_input.py 와 같다.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_product_launch as product  # noqa: E402  UIA 이름 조회 스크립트를 함께 쓴다

REQ = {
    "E1": ["sdi_absent_from_tabs", "icon_count_eq_sdi", "tooltip_title", "click_activates", "click_restores_minimized", "icon_context_menu"],
    "E2": ["tab_natural_le_240", "tab_shrink_common_cap", "tab_min_72_then_scroll", "ellipsis_tooltip"],
    "E3": ["strip_height_28", "tab_height_24", "menu_bar_32_unchanged"],
    "E4": ["dock_tabs_hidden", "dock_icons_only", "dock_strip_hidden_no_sdi", "mdi_tabs_kept", "document_tabs_kept"],
    "E5": ["icon_click_os_foreground_sdi", "sdi_caption_active_color", "main_caption_inactive_color", "before_fix_reproduced"],
}
STRIP, MENU = "shell.tabStrip", "shell.titleBar"  # 메뉴 줄 32 는 MainTitleBar 의 Bar(Height=32)다. shell.menu 는 그 안의 메뉴 컨트롤이다
ICON_PREFIX = "shell.sdiIcon."
TAB_PREFIX = "shell.tab."
STRIP_H, TAB_H, MENU_H = 28, 24, 32
TAB_MIN, TAB_MAX = 72, 240
TAB_GAP, CROWD_W = 2, 1000  # 제목 탭 사이 간격(MainView TitleTabPanel Spacing). 공통 상한 시험은 창 폭 1000 에서 한다(1400 에서는 12탭이 안 줄어도 들어간다)
SIZE_TOL = 0.5
LONG_TITLE = "아주 긴 제목으로 탭 말줄임과 툴팁을 확인하는 시험 문서 이름 0123456789 ABCDEFGHIJ"
MANY, TOO_MANY = 12, 40


def controls(rig, window=0):
    return ri.walk(rig.r.call("tree.get", window=window, named=True, depth=60).get("tree"), [])


def by_prefix(rig, prefix):
    return [n for n in controls(rig) if str(n.get("id", "")).startswith(prefix) and n.get("effectivelyVisible")]


def tabs(rig):
    """shell.tabClose.{id} 는 shell.tab 접두가 아니다(shell.tabC…). 그래도 정확히 문서 id 로 끝나는 것만 탭으로 본다."""
    ids = set(ri.doc_ids(rig))
    return [n for n in by_prefix(rig, TAB_PREFIX) if n["id"][len(TAB_PREFIX):] in ids]


def icons(rig):
    return by_prefix(rig, ICON_PREFIX)


def widths(rows):
    return [n["bounds"]["w"] for n in rows]


def item_widths(rig):
    """탭 id(shell.tab.<doc>) → 그 탭을 담은 ListBoxItem 의 폭. shell.tab.<doc> 노드는 제목 Button 뿐이라 닫기 열(약 16 DIP)이 빠지므로 기준(72~240)은 항목 폭으로 읽는다."""
    ids = {TAB_PREFIX + d for d in ri.doc_ids(rig)}
    tree = rig.r.call("tree.get", window=0, named=False, depth=80).get("tree")
    out = {}

    def visit(node, item):
        if node.get("type") == "ListBoxItem":
            item = node
        nid = str(node.get("id", ""))
        if item is not None and nid in ids and node.get("effectivelyVisible") and item.get("effectivelyVisible") and item.get("bounds"):
            out[nid] = item["bounds"]["w"]
        for c in node.get("children") or []:
            visit(c, item)
    for root in (tree if isinstance(tree, list) else [tree]):
        visit(root, None)
    return out


def setup_mix(rig):
    """MDI 2 + SDI 2."""
    a, b, c, d = ri.open_docs(rig, count=4)
    h1 = ri.make_sdi(rig, c)
    h2 = ri.make_sdi(rig, d, w=420, h=300, x=rig.win(rig.main)["rect"]["r"] - 980)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    rig.acquire(rig.main)
    return a, b, c, d, h1, h2


def click_icon(rig, doc, button="Left"):
    """아이콘이 없는 구현 전 판본은 SDI 의 제목 탭을 누른다(재현 값 수집용). 어느 쪽인지 값에 남긴다."""
    icon = rig.find(ICON_PREFIX + doc)
    kind = "icon"
    if not icon or not icon.get("effectivelyVisible"):
        icon, kind = rig.find(TAB_PREFIX + doc), "tab"
    if not icon or not icon.get("effectivelyVisible"):
        raise ri.InputRefused("아이콘·탭이 모두 없음: %s" % doc)
    pt = rig.point(rig.main, icon["bounds"])
    rig.click(*pt, button=button, hwnd=rig.main)
    return kind, pt


def count_named(rig, title):
    """UIA 로 그 이름을 가진 요소 수. 제목은 다른 곳에도 있으므로 호버 전후 증가로 판단한다."""
    text = ri.ps_encoded(product.UIA_NAMES_PS % {"pid": rig.pid, "names": "'%s'" % title.replace("'", "''")}, timeout=120)
    rows = json.loads(text) if text else []
    return len(rows) if isinstance(rows, list) else 1


def tip_of(rig, cid, title):
    """실제 호버로 툴팁을 띄워 잰다. ToolTip.Tip 첨부 속성은 원격 ui.find props 로 읽는다는 보장이 없어 쓰지 않는다."""
    node = rig.find(cid)
    if not node or not node.get("effectivelyVisible"):
        return {"target": None}, False
    before, known = count_named(rig, title), {w["hwnd"] for w in rig.windows()}
    # 전경을 가져온 직후 첫 호버는 포인터 진입이 잡히지 않아 툴팁이 안 뜨는 일이 있었다(otest-9 11-07 E1 Grid-3, Grid-4 는 뜸).
    # 그러면 커서를 제목 끌기 영역으로 뺐다가 다시 올려 1회 더 본다. 기준(이름 수 증가 + 새 팝업 창)은 그대로다
    for attempt in (1, 2):
        if attempt == 2:
            away_pt = rig.find("shell.titleDragArea")
            if not away_pt:
                break
            rig.click(*rig.point(rig.main, away_pt["bounds"]), no_click=True)
            time.sleep(0.8)
        rig.click(*rig.point(rig.main, node["bounds"]), no_click=True)
        time.sleep(1.5)
        during, added = count_named(rig, title), [w["cls"] for w in rig.windows() if w["hwnd"] not in known]
        if during > before and added:
            break
    return {"named_before": before, "named_during": during, "new_windows": added, "attempts": attempt}, during > before and bool(added)


def e1(rig, res):
    a, b, c, d, h1, h2 = setup_mix(rig)
    names = {x["id"]: x["title"] for x in rig.r.call("docs.list")}
    tab_ids = [n["id"] for n in tabs(rig)]
    res.add("sdi_absent_from_tabs", {"tabs": tab_ids, "sdi": [c, d]}, TAB_PREFIX + c not in tab_ids and TAB_PREFIX + d not in tab_ids and TAB_PREFIX + a in tab_ids)
    row = [n["id"] for n in icons(rig)]
    res.add("icon_count_eq_sdi", {"icons": row, "sdi": [c, d]}, sorted(row) == sorted(ICON_PREFIX + x for x in (c, d)))
    rig.grab(res, "e1_mix")

    def tips():
        rig.acquire(rig.main)
        got = {x: tip_of(rig, ICON_PREFIX + x, names[x]) for x in (c, d)}
        rig.grab(res, "e1_tooltip")
        return {x: v[0] for x, v in got.items()}, all(v[1] for v in got.values())
    res.measure("tooltip_title", tips)

    def activates():
        rig.acquire(rig.main)
        kind, pt = click_icon(rig, c)
        time.sleep(ri.SETTLE_S)
        fg = rig.foreground()
        active = next((x["id"] for x in rig.r.call("docs.list") if x.get("active")), None)
        return {"via": kind, "at": pt, "fg": fg, "want": h1, "active": active}, kind == "icon" and fg["hwnd"] == h1 and active == c
    res.measure("click_activates", activates)

    def restores():
        rig.r.call("docs.state", doc=d, state="minimized")
        time.sleep(ri.SETTLE_S)
        before = (rig.win(h2) or {}).get("iconic")
        rig.acquire(rig.main)
        kind, pt = click_icon(rig, d)
        time.sleep(ri.SETTLE_S)
        after = (rig.win(h2) or {}).get("iconic")
        rig.grab(res, "e1_restored")
        return {"via": kind, "before": before, "after": after}, kind == "icon" and before is True and after is False
    res.measure("click_restores_minimized", restores)

    def menu():
        rig.acquire(rig.main)
        icon = rig.find(ICON_PREFIX + c)
        if not icon or not icon.get("effectivelyVisible"):
            return {"icon": None}, False
        # ContextMenu 는 아이콘 Button 이 아니라 아이콘 줄 ItemsControl(shell.sdiIcons)에 붙어 있다(MainView.axaml). 우클릭 위치는 그대로 아이콘이다
        return ri.rclick_menu(rig, res, "e1_icon_menu", *rig.point(rig.main, icon["bounds"]), rig.main, "shell.sdiIcons")
    res.measure("icon_context_menu", menu)


def e2(rig, res):
    a, b = ri.open_docs(rig, count=2)
    long_id = ri.open_docs(rig, title=LONG_TITLE)[0]
    time.sleep(0.5)
    first = item_widths(rig)
    rig.grab(res, "e2_few")
    values = list(first.values())
    res.add("tab_natural_le_240", first, len(values) == 3 and max(values) <= TAB_MAX + SIZE_TOL and first.get(TAB_PREFIX + long_id, 0) > first.get(TAB_PREFIX + a, TAB_MAX))

    def tip():
        got, shown = tip_of(rig, TAB_PREFIX + long_id, LONG_TITLE)
        node = rig.find(TAB_PREFIX + long_id) or {}
        return {"tip": got, "width": node.get("bounds", {}).get("w")}, shown and node.get("bounds", {}).get("w", 999) <= TAB_MAX + SIZE_TOL
    res.measure("ellipsis_tooltip", tip)

    rig.r.call("window.resize", w=CROWD_W, h=ri.MAIN_H)
    time.sleep(1.5)
    ri.open_docs(rig, count=MANY - 3)
    time.sleep(0.6)
    strip = rig.find(STRIP) or {}
    many = list(item_widths(rig).values())
    rig.grab(res, "e2_many")
    cap = max(many) if many else 0
    within = sum(many) + TAB_GAP * max(0, len(many) - 1) <= strip.get("bounds", {}).get("w", 0) + SIZE_TOL
    res.add("tab_shrink_common_cap", {"widths": many, "strip": strip.get("bounds")}, len(many) == MANY and TAB_MIN - SIZE_TOL <= cap < first.get(TAB_PREFIX + long_id, 0) and within)

    ri.open_docs(rig, count=TOO_MANY - MANY)
    time.sleep(0.8)
    crowded = list(item_widths(rig).values())
    strip_w = (rig.find(STRIP) or {}).get("bounds", {}).get("w", 0)
    # 가로 스크롤: ui.scroll 은 세로 오프셋만 움직여 쓸 수 없다. 첫 문서를 활성화하면 첫 탭이 보이고, 마지막 문서를 활성화하면 ListBox 가 선택 항목을 보이게 가로로 스크롤해
    # 첫 탭이 줄 왼쪽 밖으로 나가고 마지막 탭이 줄 안에 들어와야 한다(탭 노드 x 는 줄 기준 DIP)
    order = ri.doc_ids(rig)

    def tab_x(doc):
        node = rig.find(TAB_PREFIX + doc) or {}
        return (node.get("bounds") or {}).get("x"), (node.get("bounds") or {}).get("w")
    strip_x = ((rig.find(STRIP) or {}).get("bounds") or {}).get("x", 0)
    rig.r.call("docs.activate", doc=order[0])
    time.sleep(0.8)
    first_at_start, _ = tab_x(order[0])
    rig.r.call("docs.activate", doc=order[-1])
    time.sleep(1.0)
    first_after, _ = tab_x(order[0])
    last_x, last_w = tab_x(order[-1])
    rig.grab(res, "e2_crowded")
    scroll = {"first_x_when_first_active": first_at_start, "first_x_when_last_active": first_after, "last_x": last_x, "last_w": last_w, "strip_x": strip_x, "strip_w": strip_w, "method": "활성화로 선택 항목 보이기(ListBox 자동 스크롤) 뒤 탭 x 변화"}
    moved = None not in (first_at_start, first_after, last_x, last_w) and first_after < first_at_start - 1 and first_after < strip_x - SIZE_TOL and last_x >= strip_x - SIZE_TOL and last_x + last_w <= strip_x + strip_w + SIZE_TOL
    res.add("tab_min_72_then_scroll", {"min": min(crowded) if crowded else None, "count": len(crowded), "total": sum(crowded), "strip": strip_w, "scroll": scroll},
            bool(crowded) and min(crowded) >= TAB_MIN - SIZE_TOL and sum(crowded) > strip_w > 0 and moved)


def e3(rig, res):
    a = ri.open_docs(rig, count=2)[0]
    time.sleep(0.4)

    def height(cid, want):
        node = rig.find(cid) or {}
        h = node.get("bounds", {}).get("h")
        return {"id": cid, "h": h, "want": want}, h is not None and abs(h - want) <= SIZE_TOL
    res.measure("strip_height_28", lambda: height(STRIP, STRIP_H))
    res.measure("tab_height_24", lambda: height(TAB_PREFIX + a, TAB_H))
    res.measure("menu_bar_32_unchanged", lambda: height(MENU, MENU_H))
    rig.grab(res, "e3_strip")


def set_style(rig, style):
    rig.r.call("ui.style", set=style)
    time.sleep(1.5)
    return (rig.r.call("ui.style") or {}).get("effective")


def doc_area_tabs(rig):
    nodes = ri.walk(rig.r.call("tree.get", window=0, named=False, depth=80).get("tree"), [])  # 60 은 DocumentTabStripItem 보다 얕다
    return [n for n in nodes if n.get("type") == "DocumentTabStripItem" and n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0]


def e4(rig, res):
    a, b, c, d, h1, h2 = setup_mix(rig)
    effective = set_style(rig, "tablet")
    rows, marks = tabs(rig), icons(rig)
    strip = rig.find(STRIP) or {}  # 제목 탭 목록(ListBox). 태블릿형에서는 숨겨지는 것이 정상이다
    icon_row = rig.find("shell.sdiIcons") or {}  # 독립 창 아이콘 줄. 태블릿형에서 이것만 보여야 한다
    rig.grab(res, "e4_dock_sdi")
    res.add("dock_tabs_hidden", {"effective": effective, "tabs": [n["id"] for n in rows]}, not rows)
    res.add("dock_icons_only", {"icons": [n["id"] for n in marks], "iconRowVisible": icon_row.get("effectivelyVisible"), "tabListVisible": strip.get("effectivelyVisible")},
            bool(icon_row.get("effectivelyVisible")) and not strip.get("effectivelyVisible") and sorted(n["id"] for n in marks) == sorted(ICON_PREFIX + x for x in (c, d)))
    res.add("document_tabs_kept", {"count": len(doc_area_tabs(rig))}, len(doc_area_tabs(rig)) >= 1)
    rig.r.call("docs.close", doc=c)
    rig.r.call("docs.close", doc=d)
    time.sleep(ri.SETTLE_S)
    gone = rig.find(STRIP) or {}
    gone_row = rig.find("shell.sdiIcons") or {}
    rig.grab(res, "e4_dock_nosdi")
    res.add("dock_strip_hidden_no_sdi", {"strip": gone.get("effectivelyVisible"), "iconRow": gone_row.get("effectivelyVisible"), "icons": [n["id"] for n in icons(rig)]},
            not gone.get("effectivelyVisible") and not gone_row.get("effectivelyVisible") and not icons(rig))
    effective = set_style(rig, "pc")
    kept = [n["id"] for n in tabs(rig)]
    res.add("mdi_tabs_kept", {"effective": effective, "tabs": kept}, TAB_PREFIX + a in kept and TAB_PREFIX + b in kept)


def active_state(rig):
    return rig.r.call("sim.activeState") or {}


def frame_flags(rig, title_main):
    state = active_state(rig)
    wins = state.get("windows") or []
    # frameActive 는 pdSdiFrame(SDI HostWindow 전용)에서 나와 메인 창은 항상 null 이다. 메인은 isActive(창 IsActive, MainTitleBar 의 inactive 클래스 원천)로 읽는다
    main_win = next((w for w in wins if w.get("sdi") is False), None) or next((w for w in wins if w.get("title") == title_main), None) or {}
    return {"main": main_win.get("frameActive"), "main_active": main_win.get("isActive"), "others": [w.get("frameActive") for w in wins if w is not main_win], "raw": wins}


def caption(rig, cid, hwnd, **scope):
    node = rig.r.call("ui.find", id=cid, props="Background", **scope) or {}
    if not node.get("bounds"):
        return {"background": None, "pixel": None}
    x, y = rig.point(hwnd, node["bounds"], 0.5, 0.5)
    return {"background": (node.get("props") or {}).get("Background"), "pixel": rig.pixel(x, y), "at": (x, y)}


def before_value(args):
    path = os.path.join(ri.ROOT, "poc", "ui", "logs", "pcaway", "discovery", "E5.json")
    try:
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
    except (OSError, ValueError):
        return {"discovery": "부재", "path": path}, False
    row = next((m for m in doc.get("measurements", []) if m["name"] == "before_fix_reproduced"), None)
    return {"discovery_run": doc.get("run_id"), "value": (row or {}).get("value")}, bool(row and row.get("pass"))


def e5(rig, res):
    a, b = ri.open_docs(rig, count=2)
    hwnd = ri.make_sdi(rig, b)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    rig.acquire(rig.main)
    time.sleep(0.5)
    main_title = rig.r.call("windows.list")[0].get("title")
    base_main, base_sdi = caption(rig, "shell.titleDragArea", rig.main), caption(rig, "chrome.title", hwnd, doc=b)
    rig.grab(res, "e5_before_click")
    outcome = {}

    def click():
        kind, pt = click_icon(rig, b)
        time.sleep(ri.SETTLE_S)
        fg = rig.foreground()
        outcome.update(fg=fg, via=kind)
        return {"via": kind, "at": pt, "fg": fg, "want": hwnd}, fg["hwnd"] == hwnd
    res.measure("icon_click_os_foreground_sdi", click)
    now_main, now_sdi = caption(rig, "shell.titleDragArea", rig.main), caption(rig, "chrome.title", hwnd, doc=b)
    flags = frame_flags(rig, main_title)
    rig.grab(res, "e5_after_click")
    sdi_ok = any(v is True for v in flags["others"]) and (now_sdi["background"] != base_sdi["background"] or now_sdi["pixel"] != base_sdi["pixel"])
    changed = now_main["background"] != base_main["background"] or now_main["pixel"] != base_main["pixel"]
    same_as_sdi_inactive = now_main["pixel"] is not None and now_main["pixel"] == base_sdi["pixel"]  # SDI 가 비활성이던 클릭 전 픽셀 = 비활성 기준 색
    main_ok = flags["main_active"] is False and changed and same_as_sdi_inactive
    res.add("sdi_caption_active_color", {"before": base_sdi, "after": now_sdi, "frame": flags["others"]}, sdi_ok)
    res.add("main_caption_inactive_color", {"before": base_main, "after": now_main, "frame": flags["main"], "main_isActive": flags["main_active"], "pixel_changed": changed,
                                            "sdi_inactive_baseline_pixel": base_sdi["pixel"], "same_as_sdi_inactive": same_as_sdi_inactive}, main_ok)
    if res.args.phase == "discovery":
        reproduced = not (sdi_ok and main_ok and outcome.get("fg", {}).get("hwnd") == hwnd)
        res.add("before_fix_reproduced", {"reproduced": reproduced, "via": outcome.get("via")}, reproduced)
    else:
        res.measure("before_fix_reproduced", lambda: before_value(res.args))


ITEMS = {"E1": e1, "E2": e2, "E3": e3, "E4": e4, "E5": e5}


def self_test(args):
    fails = ri.self_test_names(REQ, args.session)
    # E1~E5 판정기는 소스 파일도 요구하므로 왕복은 결과 필드만 본다: REQ 이름이 비어 있지 않고 값이 정본 수치와 같아야 한다
    if (STRIP_H, TAB_H, MENU_H, TAB_MIN, TAB_MAX) != (28, 24, 32, 72, 240):
        fails.append("정본 수치와 다름")

    class FakeRig:
        def __init__(self, ids, nodes):
            self.ids, self.nodes = ids, nodes
            self.r = self

        def call(self, cmd, **kw):
            return {"tree": self.nodes} if cmd == "tree.get" else [{"id": i, "title": i} for i in self.ids]
    fake = FakeRig(["a", "b"], [{"id": "shell.tab.a", "effectivelyVisible": True, "children": [{"id": "shell.tabClose.a", "effectivelyVisible": True}]},
                                {"id": "shell.tab.b", "effectivelyVisible": False}, {"id": "shell.sdiIcon.b", "effectivelyVisible": True}])
    if [n["id"] for n in tabs(fake)] != ["shell.tab.a"]:
        fails.append("탭 판정 오류")
    if [n["id"] for n in icons(fake)] != ["shell.sdiIcon.b"]:
        fails.append("아이콘 판정 오류")
    probe = argparse.Namespace()
    value, ok = before_value(probe)
    if ok and not value.get("value"):
        fails.append("before 값 없이 통과")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return ri.drive(args, ITEMS, REQ, "desktop_work", os.path.abspath(__file__))


if __name__ == "__main__":
    sys.exit(main())
