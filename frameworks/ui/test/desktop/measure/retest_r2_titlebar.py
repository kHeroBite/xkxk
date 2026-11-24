#!/usr/bin/env python3
# 타이틀바 아이콘 배치(T1)와 제목 줄·제목 탭 높이 사양(T3)을 진단판 복사본에서 실입력·원격 값으로 재 pcaway_gate 형식 결과를 쓴다. T3 기준은 설정값(기본 38·탭 32)이다
"""
사용:
  python3 -B retest_r2_titlebar.py --items T1,T3 --run-dir <최종 bin/run/<복사본>/net10.0> --phase final --writer otest-d
  python3 -B retest_r2_titlebar.py --items T3 --strip-setting 24 ...     # 시험 앱의 '탭 줄 높이' 설정을 기본 38 에서 바꿔 둔 경우 그 값
  (--before-strip/--before-tab 는 옛 비율 규칙용으로 retest_r2_titlebar_crop.py 만 쓴다. 이 파일 T3 는 쓰지 않는다)
  python3 -B retest_r2_titlebar.py --annotate <결과.json> --capture all --read-by otest-d --finding '<판독 내용 10자 이상>'
  python3 -B retest_r2_titlebar.py --self-test
T1: 오른쪽 순서 테마(shell.btnTheme)→UI 형 4칸(shell.swUiStyle*)→최소화(shell.btnMinimize), UI 형 마지막 칸·최소화 간격 8 이하, UI 형 칸이 글자 없는 벡터 아이콘+툴팁,
    테마 클릭으로 라이트↔다크·그림 전환, 도구모음 줄(shell.btnSettings·shell.toolBar*) 없음, 좁은 폭(window.resize)에서 메뉴가 더보기(shell.mnuMore)로 접힘.
T3: 제목 줄 높이 = 설정 ShellTabStripHeight(기본 38, 19~48), 제목 탭 = round(줄 × 16/19)(기본 32), 각각 ±0.5 DIP(공통원칙 §2-1, 2026-11-11 사용자 지시로
    옛 비율 0.62~0.71 대체). 측정 이름 strip_height_before·tab_height_before 는 기대 높이, strip_ratio·tab_ratio 는 실측/기대 비율을 남긴다. 메뉴 줄(shell.titleBar) 32 불변, 제목 탭 글자 상자가 탭 안, SDI 아이콘이 줄 안. 글자 잉크 잘림은 캡처 판독(--annotate)이 본다.
안전 규칙과 종료 코드는 retest_real_input.py 와 같다. 실행은 otest 단계에서만 한다.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_title_strip as strip_lib  # noqa: E402  제목 탭·SDI 아이콘 조회와 호버 툴팁 판정을 함께 쓴다

REQ = {
    "T1": ["order_ui_then_theme_then_min", "theme_min_gap_le_8", "ui_icons_are_paths", "ui_icons_tooltips", "theme_toggle_changes_theme", "theme_glyph_switches", "toolbar_row_absent", "menu_reflow_ok_narrow"],
    "T3": ["strip_height_before", "tab_height_before", "strip_ratio", "tab_ratio", "menu_bar_32_unchanged", "tab_text_not_clipped", "sdi_icon_fits_strip"],
}
UI_BUTTONS = ["shell.swUiStyleAuto", "shell.swUiStylePc", "shell.swUiStyleTablet", "shell.swUiStyleMobile"]
THEME, MINIMIZE, MORE, MENU = "shell.btnTheme", "shell.btnMinimize", "shell.mnuMore", "shell.menu"
ORDER_IDS = [THEME] + UI_BUTTONS + [MINIMIZE]  # 왼쪽부터 기대 순서: 테마 → UI 형 4칸 → 최소화(캡션)
TITLE_BAR, STRIP, TAB_PREFIX, ICON_PREFIX = "shell.titleBar", "shell.tabStrip", "shell.tab.", "shell.sdiIcon."
RATIO_MIN, RATIO_MAX, MENU_H, GAP_MAX, TOL = 0.62, 0.71, 32, 8.0, 0.5  # RATIO_* 는 옛 규칙(28·24 → 19·16)으로 retest_r2_titlebar_crop.py 가 아직 가져다 쓴다. 이 파일 T3 는 쓰지 않는다
STRIP_DEFAULT, STRIP_MIN, STRIP_MAX = 38, 19, 48  # 제목 줄 높이 설정 ShellTabStripHeight(UiSettings.Shell.cs Default/Min/MaxShellTabStripHeight)
SPEC_RULE = "제목 줄 = ShellTabStripHeight(기본 38, 19~48), 제목 탭 = round(줄 × 16/19)(기본 32) — 공통원칙 §2-1(2026-11-11)"
NARROW_W, WIDE_W = 500, 1400  # 620 에서는 메뉴가 아직 안 접힌다(실측 2026-11-07: 560 이하에서 더보기). 500 은 더보기가 보이는 폭
TITLE_TEXT_TYPES = ("TextBlock", "AccessText", "Label")  # UI 형 칸 안에 이런 글자 요소가 있으면 글자 칸이다


# 순수 판정(자가시험 대상) -----------------------------------------------------
def order_ok(xs):
    """왼쪽부터 값이 모두 있고 엄격히 증가해야 한다."""
    return all(x is not None for x in xs) and all(a < b for a, b in zip(xs, xs[1:]))


def gap_between(left, right):
    """left 오른쪽 끝과 right 왼쪽 끝 사이(DIP). 노드는 bounds{x,w} 를 가진다."""
    if not left or not right:
        return None
    return right["x"] - (left["x"] + left["w"])


def ratio_ok(now, before):
    if not before or not now or before <= 0:
        return None, False
    r = now / before
    return round(r, 4), RATIO_MIN <= r <= RATIO_MAX


def inside(inner, outer, tol=TOL):
    return inner["y"] >= outer["y"] - tol and inner["y"] + inner["h"] <= outer["y"] + outer["h"] + tol


def vis_bounds(rig, cid):
    node = rig.find(cid) or {}
    return node["bounds"] if node.get("effectivelyVisible") and node.get("bounds") else None


def find_node(rig, cid):
    return next((n for n in strip_lib.controls(rig) if n.get("id") == cid), None)


def subtree(node):
    return ri.walk(node.get("children") or [], []) if node else []


def full_node(rig, cid):
    """이름 없는 요소(PathIcon 등)까지 담은 전체 트리에서 cid 노드를 찾는다. 이름 있는 트리(named=True)에는 아이콘이 없다."""
    tree = rig.r.call("tree.get", window=0, named=False, depth=80).get("tree")
    return next((n for n in ri.walk(tree, []) if n.get("id") == cid), None)


def theme_state(rig):
    """settings.get 의 테마 관련 값 전부(키 이름에 theme 이 든 것)를 묶는다. 키 이름은 구현에 따라 달라도 되게 이름으로 고르지 않는다."""
    doc = rig.r.call("settings.get") or {}
    return {k: v for k, v in doc.items() if "theme" in str(k).lower()}


def glyphs(rig):
    """테마 버튼 안 PathIcon 들의 보임 상태. 해·달 둘 중 하나만 보여야 하고 클릭하면 뒤바뀐다."""
    node = full_node(rig, THEME)
    return [bool(n.get("effectivelyVisible")) for n in subtree(node) if n.get("type") == "PathIcon"]


def click_ctl(rig, cid):
    node = rig.find(cid)
    if not node or not node.get("effectivelyVisible"):
        raise ri.InputRefused("보이지 않음: " + cid)
    if getattr(rig, "l0_click", False):  # 백그라운드 구동기: 앱 내장 ui.click(L0). 실제 포인터 클릭은 미검증이다
        rig.r.call("ui.click", id=cid)
        time.sleep(ri.SETTLE_S)
        return
    rig.acquire(rig.main)
    rig.click(*rig.point(rig.main, node["bounds"]), hwnd=rig.main)
    time.sleep(ri.SETTLE_S)


def hover_tooltip(rig, cid):
    """호버로 툴팁 창이 새로 떠야 한다(title_strip 의 tip_of 와 같은 방식 — 이름이 아니라 새 창으로 판정)."""
    node = rig.find(cid)
    if not node or not node.get("effectivelyVisible"):
        return {"target": None}, False
    known = {w["hwnd"] for w in rig.windows()}
    rig.click(*rig.point(rig.main, node["bounds"]), no_click=True, hwnd=rig.main)
    time.sleep(1.5)
    added = [w["cls"] for w in rig.windows() if w["hwnd"] not in known]
    return {"id": cid, "new_windows": added}, bool(added)


# T1 ------------------------------------------------------------------------
def t1(rig, res):
    ri.open_docs(rig, count=1)
    rig.r.call("ui.style", set="pc")
    time.sleep(1.5)
    rig.acquire(rig.main)
    nodes = {cid: vis_bounds(rig, cid) for cid in UI_BUTTONS + [THEME, MINIMIZE]}
    rig.grab(res, "t1_wide")
    # 2026-11-10 UI4 항목 4: 순서가 테마→UI 형 4칸→최소화로 바뀌었다. 측정 이름은 판정기(pcaway_gate T1_NAMES) 계약이라 유지하고 뜻만 바꾼다
    order = ORDER_IDS
    xs = [(nodes[c] or {}).get("x") for c in order]
    res.add("order_ui_then_theme_then_min", {"order": order, "x": dict(zip(order, xs))}, order_ok(xs))
    gap = gap_between(nodes[UI_BUTTONS[-1]], nodes[MINIMIZE])
    res.add("theme_min_gap_le_8", {"between": [UI_BUTTONS[-1], MINIMIZE], "gap": gap}, gap is not None and -TOL <= gap <= GAP_MAX)

    def paths():
        rows = {}
        for cid in UI_BUTTONS:
            kinds = [n.get("type") for n in subtree(full_node(rig, cid))]
            rows[cid] = {"path_icons": kinds.count("PathIcon"), "text_nodes": sum(kinds.count(t) for t in TITLE_TEXT_TYPES)}
        return rows, all(v["path_icons"] >= 1 and v["text_nodes"] == 0 for v in rows.values())
    res.measure("ui_icons_are_paths", paths)

    def tips():
        got = {}
        for cid in UI_BUTTONS:
            rig.acquire(rig.main)
            got[cid] = hover_tooltip(rig, cid)
        rig.grab(res, "t1_tooltip")
        return {k: v[0] for k, v in got.items()}, all(v[1] for v in got.values())
    res.measure("ui_icons_tooltips", tips)

    def toggle():
        before, glyph0 = theme_state(rig), glyphs(rig)
        px0 = rig.pixel(*rig.point(rig.main, (rig.find(TITLE_BAR) or {})["bounds"], 0.5, 0.5))
        click_ctl(rig, THEME)
        after, glyph1 = theme_state(rig), glyphs(rig)
        px1 = rig.pixel(*rig.point(rig.main, (rig.find(TITLE_BAR) or {})["bounds"], 0.5, 0.5))
        rig.grab(res, "t1_theme_toggled")
        out = {"before": before, "after": after, "glyph_before": glyph0, "glyph_after": glyph1, "pixel_before": px0, "pixel_after": px1}
        res.add("theme_glyph_switches", {"before": glyph0, "after": glyph1}, bool(glyph0) and glyph0 != glyph1 and sum(glyph0) == 1 and sum(glyph1) == 1)
        return out, before != after
    res.measure("theme_toggle_changes_theme", toggle)
    click_ctl(rig, THEME)  # 원래 테마로 되돌려 다음 측정에 영향이 없게 한다

    def toolbar():
        ids = [str(n.get("id", "")) for n in strip_lib.controls(rig) if n.get("effectivelyVisible")]
        bad = [i for i in ids if i == "shell.btnSettings" or i.lower().startswith("shell.toolbar")]
        theme, bar = vis_bounds(rig, THEME), vis_bounds(rig, TITLE_BAR)
        return {"leftover": bad, "theme": theme, "title_bar": bar}, not bad and bool(theme and bar) and inside(theme, bar)
    res.measure("toolbar_row_absent", toolbar)

    def narrow():
        rig.r.call("window.resize", w=NARROW_W, h=700)
        time.sleep(1.5)
        rig.acquire(rig.main)
        more, menu, theme = vis_bounds(rig, MORE), vis_bounds(rig, MENU), vis_bounds(rig, THEME)
        rig.grab(res, "t1_narrow")
        folded = bool(more) and bool(menu and theme) and menu["x"] + menu["w"] <= theme["x"] + TOL
        rig.r.call("window.resize", w=WIDE_W, h=ri.MAIN_H)
        time.sleep(1.5)
        wide_more = vis_bounds(rig, MORE)
        return {"narrow_more": more, "menu": menu, "theme": theme, "wide_more_visible": bool(wide_more)}, folded and not wide_more
    res.measure("menu_reflow_ok_narrow", narrow)


# T3 ------------------------------------------------------------------------
def before_heights(args):
    """before 높이: --before-strip/--before-tab 이 있으면 그 값, 아니면 discovery/T3.json 의 기록. discovery 단계는 지금 잰 값이 곧 before 다."""
    if args.before_strip and args.before_tab:
        return {"strip": args.before_strip, "tab": args.before_tab, "source": "cli"}
    path = os.path.join(ri.ROOT, "poc", "ui", "logs", "pcaway", "discovery", "T3.json")
    try:
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
    except (OSError, ValueError):
        return {"source": "부재", "path": path}
    rows = {m["name"]: m for m in doc.get("measurements", [])}
    return {"strip": (rows.get("strip_height_before") or {}).get("value", {}).get("h"), "tab": (rows.get("tab_height_before") or {}).get("value", {}).get("h"),
            "source": "discovery", "run_id": doc.get("run_id")}


def spec_heights(setting=None):
    """새 사양(공통원칙 §2-1, 2026-11-11): 제목 줄 = 설정 ShellTabStripHeight(기본 38, 19~48, 반올림), 제목 탭 = round(줄 × 16/19)(기본 32).
    setting 을 주지 않으면 기본값 38 을 쓴다. 범위 밖 값은 앱처럼 자르지 않고 거부한다(시험 인자 오타를 숨기지 않기 위해)."""
    if setting is None:
        s, source = STRIP_DEFAULT, "spec_default"
    else:
        s, source = float(setting), "cli_setting"
        if s != round(s) or not STRIP_MIN <= s <= STRIP_MAX:
            raise ValueError("--strip-setting 은 %d~%d 정수여야 한다: %r" % (STRIP_MIN, STRIP_MAX, setting))
    return {"strip": s, "tab": float(round(s * 16 / 19)), "setting": s, "source": source}


def spec_ok(now, want, tol=TOL):
    """실측 높이가 기대 높이와 tol(DIP) 안에서 같으면 합격. 비율(실측/기대)은 기록용이다."""
    if not isinstance(now, (int, float)) or not want or want <= 0:
        return None, False
    return round(now / want, 4), abs(now - want) <= tol


def height_of(rig, cid):
    node = rig.find(cid) or {}
    return (node.get("bounds") or {}).get("h")


def grab_hwnd(rig, res, tag, hwnd):
    """hwnd 창을 전경으로 올린 뒤 그 창 클라이언트만 캡처 목록에 남긴다. rig.grab 은 화면을 복사하므로 SDI 가 메인 뒤에 있으면 메인이 찍힌다(T3 캡처 오류 원인). {hwnd, title, file} 을 돌려준다."""
    rig.acquire(hwnd)
    os.makedirs(res.shots, exist_ok=True)
    text = ri.run_ps_file(ri.GRAB, "-OutDir", ri.app.win(res.shots), "-Tag", tag, "-ProcessId", rig.pid).stdout
    got = {"hwnd": hwnd, "title": (rig.win(hwnd) or {}).get("title"), "file": None}
    for m in ri.re.finditer(r"hwnd=(-?\d+) .*?file=(.+\.png)", text):
        path = ri.subprocess.check_output(["wslpath", "-u", m.group(2).strip()], text=True).strip()
        if int(m.group(1)) == hwnd:
            res.capture(path)
            got["file"] = path
        else:
            os.remove(path)  # 대상 SDI 창이 아닌 창(메인 등) 캡처는 남기지 않는다
    return got


def t3(rig, res):
    a, b = ri.open_docs(rig, count=2, title="한글 gjpq 제목 탭 시험")
    h_sdi = ri.make_sdi(rig, b)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    rig.acquire(rig.main)
    rig.grab(res, "t3_strip")
    strip_h, tab_h = height_of(rig, STRIP), height_of(rig, TAB_PREFIX + a)
    # 2026-11-11 사용자 지시로 비율 기준(0.62~0.71)은 대체됐다(공통원칙 §2-1). 기준은 '고치기 전 실측'이 아니라 설정값이다.
    # 측정 이름은 판정기 계약이라 유지한다: *_height_before = 비교 기준(설정값에서 나온 기대 높이), *_ratio = 실측/기대(1.0 근처면 합격)
    want = spec_heights(getattr(res.args, "strip_setting", None))  # retest_r2_bg.py 처럼 이 인자가 없는 구동기에서는 기본 38
    res.add("strip_height_before", {"h": want["strip"], "source": want["source"], "setting": want["setting"], "rule": SPEC_RULE}, want["strip"] > 0)
    res.add("tab_height_before", {"h": want["tab"], "source": want["source"], "setting": want["setting"], "rule": SPEC_RULE}, want["tab"] > 0)
    sr, s_ok = spec_ok(strip_h, want["strip"])
    tr, t_ok = spec_ok(tab_h, want["tab"])
    res.add("strip_ratio", {"now": strip_h, "want": want["strip"], "ratio": sr, "tol_dip": TOL, "setting": want["setting"], "source": want["source"]}, s_ok)
    res.add("tab_ratio", {"now": tab_h, "want": want["tab"], "ratio": tr, "tol_dip": TOL, "setting": want["setting"], "source": want["source"]}, t_ok)
    res.measure("menu_bar_32_unchanged", lambda: ({"h": height_of(rig, TITLE_BAR), "want": MENU_H}, abs((height_of(rig, TITLE_BAR) or 0) - MENU_H) <= TOL))

    def clipped():
        tab = find_node(rig, TAB_PREFIX + a)
        texts = [n for n in subtree(tab) if n.get("type") in TITLE_TEXT_TYPES and n.get("effectivelyVisible") and n.get("bounds")]
        box = (tab or {}).get("bounds")
        rows = [{"type": n["type"], "bounds": n["bounds"]} for n in texts]
        return {"tab": box, "texts": rows}, bool(box and texts) and all(inside(n["bounds"], box) for n in texts)
    res.measure("tab_text_not_clipped", clipped)
    rig.grab(res, "t3_tab_zoom")

    sdi_cap = grab_hwnd(rig, res, "t3_sdi", h_sdi) if h_sdi else {"hwnd": None, "title": None, "file": None}  # SDI 창을 앞으로 올려 그 창만 찍는다

    def fits():
        icon, bar = vis_bounds(rig, ICON_PREFIX + b), vis_bounds(rig, STRIP)
        return {"icon": icon, "strip": bar, "sdi_hwnd": h_sdi, "capture_hwnd": sdi_cap["hwnd"], "capture_title": sdi_cap["title"], "capture_file": sdi_cap["file"]}, bool(icon and bar) and inside(icon, bar) and icon["h"] < bar["h"]
    res.measure("sdi_icon_fits_strip", fits)


ITEMS = {"T1": t1, "T3": t3}


def self_test(args):
    fails = ri.self_test_names(REQ, args.session) + ri.self_test_gate(REQ, args.session, "desktop_work", list(REQ))
    if not order_ok([1, 2, 3]) or order_ok([1, 1, 2]) or order_ok([1, None, 3]):
        fails.append("순서 판정 오류")
    if gap_between({"x": 10, "w": 20}, {"x": 36, "w": 5}) != 6 or gap_between(None, {"x": 1, "w": 1}) is not None:
        fails.append("간격 계산 오류")
    for now, before, want in ((19, 28, True), (16, 24, True), (28, 28, False), (24, 24, False), (17, 28, False), (None, 28, False), (19, 0, False)):
        if ratio_ok(now, before)[1] != want:
            fails.append("비율 판정 오류 %s/%s" % (now, before))
    d = spec_heights()
    if (d["strip"], d["tab"], d["source"]) != (38, 32, "spec_default"):
        fails.append("기본 사양 %s" % d)
    for s, tab in ((19, 16), (38, 32), (48, 40), (24, 20)):
        if spec_heights(s)["tab"] != tab:
            fails.append("탭 높이 공식 %s → %s" % (s, spec_heights(s)["tab"]))
    for bad in (18, 49, 30.5):
        try:
            spec_heights(bad); fails.append("범위 밖 설정 통과 %s" % bad)
        except ValueError:
            pass
    for now, want, ok in ((38, 38, True), (32, 32, True), (38.4, 38, True), (19, 38, False), (28, 38, False), (16, 32, False), (None, 38, False), (38, 0, False)):
        if spec_ok(now, want)[1] != ok:
            fails.append("사양 판정 오류 %s/%s" % (now, want))
    if spec_ok(19, 38)[0] != 0.5:
        fails.append("사양 비율 기록 오류")
    if not inside({"y": 1, "h": 10}, {"y": 0, "h": 19}) or inside({"y": 1, "h": 20}, {"y": 0, "h": 19}):
        fails.append("포함 판정 오류")
    if ORDER_IDS[0] != THEME or ORDER_IDS[1:5] != UI_BUTTONS or ORDER_IDS[-1] != MINIMIZE:
        fails.append("T1 기대 순서가 테마→UI 형 4칸→최소화가 아님")
    if len(REQ["T1"]) != 8 or len(REQ["T3"]) != 7:
        fails.append("측정 이름 개수")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--before-strip", type=float, default=None, help="고치기 전 제목 줄 높이(DIP). 주면 discovery 결과 대신 쓴다")
    p.add_argument("--before-tab", type=float, default=None, help="고치기 전 제목 탭 높이(DIP)")
    p.add_argument("--strip-setting", type=float, default=None,
                   help="T3 기대값: 시험 앱의 설정 ▸ 모양 '탭 줄 높이'(ShellTabStripHeight, 19~48). 생략하면 기본 38(제목 탭 32)")
    args = p.parse_args()
    if not args.self_test and not args.annotate:
        spec_heights(args.strip_setting)  # 범위 밖 값은 측정 전에 거부한다
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return ri.drive(args, ITEMS, REQ, "desktop_work", os.path.abspath(__file__))


if __name__ == "__main__":
    sys.exit(main())
