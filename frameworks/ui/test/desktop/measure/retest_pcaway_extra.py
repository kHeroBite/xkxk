#!/usr/bin/env python3
# PC 비움 데스크톱 추가 측정(B3·F03·F06·F10·F15) — 진단판 복사본을 화면에 띄워 원격 값과 실입력으로 재고 pcaway_gate 형식 결과를 쓴다. 기동·입력 게이트·결과 기록은 retest_real_input 을 함께 쓴다
"""
사용:
  python3 -B retest_pcaway_extra.py --items B3,F06 --run-dir <bin/run/<복사본>/net10.0> --phase discovery --writer otest-d [--measure-file <perf05 값>]
  python3 -B retest_pcaway_extra.py --self-test
B3 의 perf05_web_chrome 은 웹 축 값이다. 웹 otest 값을 --measure-file 로 넘긴다. 없으면 미측정 FAIL 이다.
F15 의 두 항목은 시스템 상태를 바꿔 별도 승인 인자가 없으면 재지 않고 미측정으로 남긴다.
  --allow-topology '<누가·언제>'      display_topology.ps1 로 DISPLAY1 을 저장 없이 임시 제거한다(AC-5-06)
  --allow-motion-toggle '<누가·언제>' OS 클라이언트 영역 애니메이션을 저장 없이 끄고 끝나면 되돌린다(AC-6-10)
PERF-03·04 의 '끊김 없음' 은 숫자 기준이 정본에 없어 B2 의 프레임 기준 25ms 를 쓴다(정렬은 정본 300ms).
안전 규칙과 종료 코드는 retest_real_input.py 와 같다. 값을 만들지 않으며 못 잰 이름은 미측정 FAIL 이다.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
from retest_real_input import drag_lib  # noqa: E402
import retest_pcaway_extra_f15 as f15_mod  # noqa: E402
sys.path.insert(0, os.path.join(HERE, "..", "judge"))
import b2_commit_frames  # noqa: E402

REQ = {
    "B3": ["perf01_first_frame", "perf02_move", "perf03_grid", "perf04_gantt", "perf05_web_chrome"],
    "F03": ["hidden_panel_alert_shown", "click_reveals_panel", "zero_when_read", "visible_panel_no_alert"],
    "F06": ["doc_tab_rclick_menu", "common_items_present", "tab_not_activated_by_rclick", "split_clear_kept"],
    "F10": ["touch_injected", "corner_button_shown_on_touch", "corner_button_touch_click", "mouse_behavior_unchanged"],
    "F15": ["ac_2_06_real_idle_no_timer", "ac_5_06_real_events_one_eval", "ac_6_10_real_motion_off"],
}
SAFE_TOUCH = os.path.join(HERE, "safe_touch.ps1")
SAFE_MOVE = os.path.join(HERE, "safe_move.ps1")
FIRST_FRAME_MS, FRAME_MS, SORT_MS = 2000, 25, 300  # a9 §4 PERF-01·02, PERF-03 정렬
LAUNCHES, MOVE_REPS, DOCS_20 = 3, 30, 20
# D-23(team-lead 11-13): 판정 회차는 커밋 표본 ≥150 이 되도록 반복 수를 늘린다(30회 → 표본 34). 다른 항목의 기본값은 그대로 두고 환경 변수로만 올린다
MOVE_REPS = int(os.environ.get("PD_MOVE_REPS", MOVE_REPS))
BADGE = "shell.notifyBadge"
CORNER = "panel.corner.BottomPane.BottomLeft"
COMMON_HEADERS = ("이전 크기로", "최소화", "최대화", "기본 위치로 이동", "닫기")  # 화면설계 §7.2, retest_title_window_menu.stage_load 와 같다
UNSPLIT = "PdUnsplit"


# 성능 ---------------------------------------------------------------------
def ops_by_suffix(rig, suffix):
    rows = rig.r.call("perf.ops", op="") or []
    return [o for o in rows if str(o.get("name", "")).endswith(suffix)]


def frame_p95_move(rig):
    """20창 전체 이동의 프레임 p95. 호출 전에 문서를 열어 둔다."""
    rig.r.call("perf.reset")
    for i in range(MOVE_REPS):
        rig.r.call("docs.moveAll", dx=8 if i % 2 == 0 else -8, dy=0)
        time.sleep(0.05)
    perf = rig.r.call("perf.get") or {}
    return {"frame_p50_ms": perf.get("frame_p50_ms"), "frame_p95_ms": perf.get("frame_p95_ms"), "reps": MOVE_REPS, "docs": len(ri.doc_ids(rig)),
            "commit": b2_commit_frames.summarize(perf)}  # B3 판정은 frame_p95_ms 그대로, B2 만 commit.pass 를 쓴다


def within(value, limit):
    return value is not None and value <= limit


def b3(rig, res):
    def first():
        values = [(rig.r.call("perf.get") or {}).get("first_frame_ms")]
        for _ in range(LAUNCHES - 1):
            rig.stop()
            rig.start()
            values.append((rig.r.call("perf.get") or {}).get("first_frame_ms"))
        return {"first_frame_ms": values, "limit": FIRST_FRAME_MS}, all(within(v, FIRST_FRAME_MS) for v in values)
    res.measure("perf01_first_frame", first)

    def move():
        ri.open_docs(rig, count=DOCS_20)
        time.sleep(1.0)
        value = frame_p95_move(rig)
        return value, value["docs"] >= DOCS_20 and within(value["frame_p95_ms"], FRAME_MS)
    res.measure("perf02_move", move)
    rig.r.call("docs.close", all=True)
    time.sleep(0.5)
    rig.r.call("perf.reset")
    rig.r.call("cmd.run", command="perf-ui", _timeout=900)  # 그리드 정렬·스크롤과 간트 줌·스크롤을 같은 경로로 잰다

    def doc_ops(limits):
        rows = {name: ops_by_suffix(rig, name) for name in limits}
        value = {name: [{k: o.get(k) for k in ("name", "count", "frame_p50_ms", "frame_p95_ms")} for o in found] for name, found in rows.items()}
        ok = all(found and all(within(o.get("frame_p95_ms"), limits[name]) for o in found) for name, found in rows.items())
        return value, ok
    res.notes.append({"perf_criteria": "PERF-03·04 '끊김 없음' 은 정본에 숫자가 없다. 임시 기준 프레임 p95 %dms(team-lead 결정), 그리드 정렬은 정본 %dms" % (FRAME_MS, SORT_MS)})
    res.measure("perf03_grid", lambda: doc_ops({"grid.sort": SORT_MS, "grid.scroll": FRAME_MS}))
    res.measure("perf04_gantt", lambda: doc_ops({"gantt.zoom": FRAME_MS, "gantt.scroll": FRAME_MS}))
    if res.args.measure_file:
        with open(res.args.measure_file, encoding="utf-8") as f:
            rows = json.load(f).get("measurements") or []
        row = next((m for m in rows if m.get("name") == "perf05_web_chrome" and str(m.get("source", "")).strip() and isinstance(m.get("pass"), bool)), None)
        if row:
            res.add("perf05_web_chrome", {"value": row.get("value"), "source": row["source"]}, row["pass"])


# 알림 안내 ----------------------------------------------------------------
def badge(rig):
    node = rig.find(BADGE) or {}
    return {"visible": bool(node.get("effectivelyVisible")), "text": node.get("text"), "bounds": node.get("bounds")}


def post_notice(rig):
    """새 알림을 만드는 원격 명령이 있을 때만 쓴다. 없으면 그 사실을 값에 남긴다. 명령을 지어내지 않는다."""
    listing = json.dumps(rig.r.call("help") or {}, ensure_ascii=False)
    if '"notify.post"' not in listing and "notify.post" not in listing:
        return False
    rig.r.call("notify.post")
    time.sleep(0.5)
    return True


def f03(rig, res):
    def visible_case():
        posted = post_notice(rig)
        return {"posted": posted, "badge": badge(rig), "panel": drag_lib.panels(rig.r)[ri.NP].get("visible")}, drag_lib.panels(rig.r)[ri.NP].get("visible") and not badge(rig)["visible"]
    res.measure("visible_panel_no_alert", visible_case)
    state = {}

    def hidden_case():
        rig.r.call("panel.close", id=ri.NP)
        time.sleep(0.6)
        state["posted"] = post_notice(rig)
        state["badge"] = badge(rig)
        rig.grab(res, "f03_badge")
        return state, state["badge"]["visible"]
    res.measure("hidden_panel_alert_shown", hidden_case)

    def reveal():
        rig.click_ctl(rig.main, id=BADGE)
        time.sleep(ri.SETTLE_S)
        panel = drag_lib.panels(rig.r)[ri.NP]
        rig.grab(res, "f03_revealed")
        return {"panelVisible": panel.get("visible"), "tabs": drag_lib.tabs_of(panel)}, panel.get("visible") is True
    res.measure("click_reveals_panel", reveal)

    def read():
        now = badge(rig)
        return now, not now["visible"]
    res.measure("zero_when_read", read)


# 문서 탭 메뉴 -------------------------------------------------------------
def doc_tab_item(rig, title, doc_id=None):
    return ri.find_doc_tab(rig.r, title, doc_id)


def f06(rig, res):
    a, b, c = ri.open_docs(rig, count=3)
    rig.r.call("docs.arrange", mode="tabbed")
    time.sleep(1.2)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.5)
    titles = {d["id"]: d["title"] for d in rig.r.call("docs.list")}
    active = lambda: next((d["id"] for d in rig.r.call("docs.list") if d.get("active")), None)
    outcome = {}

    def menu():
        item, lookup = doc_tab_item(rig, titles[b], b)
        if not item:
            return {"tab": None, "tabLookup": lookup}, False
        value, ok = ri.rclick_menu(rig, res, "f06_tab_menu", *rig.point(rig.main, item["bounds"]), rig.main)
        if isinstance(value, dict):
            value["tabLookup"] = lookup
        outcome["active_after"] = active()
        return value, ok
    res.measure("doc_tab_rclick_menu", menu)
    res.add("tab_not_activated_by_rclick", {"before": a, "after": outcome.get("active_after")}, outcome.get("active_after") == a)

    def common():
        headers = [str(i.get("header")) for i in (rig.r.call("dock.menu", action="items", target="tab", doc=b) or {}).get("items", [])]
        return {"headers": headers[:14]}, all(any(h.startswith(w) for h in headers) for w in COMMON_HEADERS)
    res.measure("common_items_present", common)

    def split_clear():
        rig.r.call("split.do", target="Documents", op="Bottom", doc=c)
        time.sleep(1.0)
        items = (rig.r.call("dock.menu", action="items", target="tab", doc=c) or {}).get("items", [])
        rig.grab(res, "f06_split")
        found = [i for i in items if i.get("name") == UNSPLIT]
        return {"names": [i.get("name") for i in items if i.get("name")], "visible": [i.get("visible") for i in found]}, bool(found) and found[0].get("visible") is True
    res.measure("split_clear_kept", split_clear)


# 터치 ---------------------------------------------------------------------
def touch(rig, x, y):
    rig.check_coord(x, y)
    rig.gate(rig.main)
    done = ri.run_ps_file(SAFE_TOUCH, "-ScreenX", x, "-ScreenY", y, "-ExpectedProcessId", rig.pid, "-ForbiddenProcessId", rig.args.user_pid or 4)  # 주입 기록은 safe_touch 안에서 한다
    rig.after_input()
    time.sleep(0.5)
    return done.returncode, (done.stderr or done.stdout)[-200:]


def corner_near(rig):
    b = rig.r.call("panel.bounds")[ri.BP]
    return rig.dip_point(rig.main, b["x"] + 12, b["y"] + b["h"] - 12)


def shown(rig):
    node = rig.find(CORNER) or {}
    return bool(node.get("effectivelyVisible")), node.get("bounds")


def f10(rig, res):
    before = rig.r.call("panel.bounds")
    centre = rig.point(rig.main, rig.find("shell.dock")["bounds"])
    rig.click(*centre, no_click=True)
    time.sleep(0.4)
    far_before = shown(rig)[0]
    rc, text = touch(rig, *corner_near(rig))
    res.add("touch_injected", {"rc": rc, "out": text}, rc == 0)
    now, bounds = shown(rig)
    rig.grab(res, "f10_after_touch")
    res.add("corner_button_shown_on_touch", {"farBefore": far_before, "visible": now, "bounds": bounds}, now)

    def tap_button():
        if not now:
            return {"visible": False}, False
        code, out = touch(rig, *rig.point(rig.main, bounds))
        time.sleep(ri.SETTLE_S)
        after = rig.r.call("panel.bounds")
        moved = abs(after[ri.BP]["x"] - before[ri.LP]["x"]) <= 2 and after[ri.LP]["h"] < before[ri.LP]["h"] - 2
        return {"rc": code, "out": out, "before": before[ri.BP], "after": after[ri.BP]}, code == 0 and moved
    res.measure("corner_button_touch_click", tap_button)
    rig.r.call("panel.corner.release", corner="BottomLeft")
    time.sleep(0.5)

    def mouse():
        def real_move(x, y):
            # SetCursorPos 가 아니라 SendInput 상대 이동(safe_move.ps1)으로 옮긴다. 주입 기록은 safe_move 안에서 한다
            rig.check_coord(x, y)
            rig.gate(rig.main)
            done = ri.run_ps_file(SAFE_MOVE, "-ScreenX", x, "-ScreenY", y, "-ExpectedProcessId", rig.pid, "-ForbiddenProcessId", rig.args.user_pid or 4)
            rig.after_input()
            return done.returncode, (done.stderr or done.stdout)[-200:]

        def state():
            return rig.r.call("panel.corner.state") or {}

        def button(st):
            return next((b for b in st.get("buttons", []) if b.get("id") == CORNER), None)

        def is_shown(st):
            # UIA effectivelyVisible 은 opacity 0 이어도 참이라 쓰지 않는다. panel.corner.state 의 shown 으로 읽는다
            return bool((button(st) or {}).get("shown"))

        def dist(rect, x, y):
            dx = max(rect["x"] - x, 0, x - (rect["x"] + rect["w"]))
            dy = max(rect["y"] - y, 0, y - (rect["y"] + rect["h"]))
            return (dx * dx + dy * dy) ** 0.5

        def moves(st):
            # 이동이 앱에 들어왔는지 가르는 값. proximity.windows[].moves(MoveCount) 목록과 합
            ws = (st.get("proximity") or {}).get("windows") or []
            return {"per_window": [w.get("moves") for w in ws], "sum": sum(w.get("moves") or 0 for w in ws)}

        FAR_MIN_DIP, NEAR_DIP = 150, 30
        # far: 모든 버튼 rect 에서 FAR_MIN_DIP 이상 먼 dock 안 지점을 고른다
        st0 = state()
        rects = [b["rect"] for b in st0.get("buttons", []) if b.get("rect")]
        dock = rig.find("shell.dock")["bounds"]
        far_dip = None
        for fx, fy in ((0.5, 0.5), (0.3, 0.3), (0.7, 0.3), (0.5, 0.2), (0.7, 0.7), (0.3, 0.7)):
            x, y = dock["x"] + dock["w"] * fx, dock["y"] + dock["h"] * fy
            if all(dist(r, x, y) >= FAR_MIN_DIP for r in rects):
                far_dip = (x, y)
                break
        if far_dip is None:
            return {"error": "far 후보 없음(모든 버튼 rect 에서 %d DIP 이상 먼 지점)" % FAR_MIN_DIP, "rects": rects}, False
        far_xy = rig.dip_point(rig.main, *far_dip)
        moves_before_far = moves(st0)
        rc_far, out_far = real_move(*far_xy)
        time.sleep(0.5)
        far_st_05 = state()
        time.sleep(1.5)
        far_st_2s = state()
        # near: 이동 직전에 대상 버튼의 현재 rect 를 읽어 중심에서 NEAR_DIP 안쪽(오른쪽)으로 잡는다
        st_near_pre = state()
        moves_before_near = moves(st_near_pre)
        btn = button(st_near_pre)
        if not btn or not btn.get("rect"):
            return {"error": "대상 버튼 rect 없음", "button_id": CORNER}, False
        rect = btn["rect"]
        near_dip = (rect["x"] + rect["w"] / 2 + NEAR_DIP, rect["y"] + rect["h"] / 2)
        near_xy = rig.dip_point(rig.main, *near_dip)
        rc_near, out_near = real_move(*near_xy)
        time.sleep(0.5)
        near_st_05 = state()
        time.sleep(1.5)
        near_st_2s = state()

        def pick(st):
            b = button(st) or {}
            return {k: b.get(k) for k in ("shown", "hitTest", "visible", "rect")}
        far_05, far_2s, near_05, near_2s = (is_shown(x) for x in (far_st_05, far_st_2s, near_st_05, near_st_2s))
        value = {"far_shown_0.5s": far_05, "far_shown_2s": far_2s, "near_shown_0.5s": near_05, "near_shown_2s": near_2s,
                 "moveCount": {"before_far": moves_before_far, "after_far_0.5s": moves(far_st_05), "after_far_2s": moves(far_st_2s),
                               "before_near": moves_before_near, "after_near_0.5s": moves(near_st_05), "after_near_2s": moves(near_st_2s)},
                 "rc": [rc_far, rc_near], "out": [out_far, out_near],
                 "far_xy_dip": far_dip, "far_xy_screen": far_xy, "near_xy_dip": near_dip, "near_xy_screen": near_xy,
                 "near_distance_dip": dist(rect, *near_dip), "far_min_distance_dip": min(dist(r, *far_dip) for r in rects),
                 "button_rect_at_near": rect, "all_button_rects_at_far": rects,
                 "button_far_0.5s": pick(far_st_05), "button_far_2s": pick(far_st_2s),
                 "button_near_0.5s": pick(near_st_05), "button_near_2s": pick(near_st_2s),
                 "measure_method": "safe_move.ps1 SendInput MOUSEEVENTF_MOVE 상대 이동(다단계+보정, 클릭 없음), 판정은 panel.corner.state buttons[].shown",
                 "note": "터치 입력 뒤 마우스로 돌아왔을 때 근접 규칙(가까우면 보이고 멀면 숨음)"}
        return value, rc_far == 0 and rc_near == 0 and near_05 and not far_05 and not far_2s
    res.measure("mouse_behavior_unchanged", mouse)


ITEMS = {"B3": b3, "F03": f03, "F06": f06, "F10": f10, "F15": f15_mod.f15}


def self_test(args):
    fails = ri.self_test_names(REQ, args.session) + f15_mod.self_test()
    if not within(25, FRAME_MS) or within(25.1, FRAME_MS) or within(None, FRAME_MS):
        fails.append("상한 판정 오류")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--measure-file", help="B3 perf05_web_chrome 의 웹 축 값")
    p.add_argument("--allow-topology", help="AC-5-06 실제 표시 구성 변경 승인 근거(누가·언제)")
    p.add_argument("--allow-motion-toggle", help="AC-6-10 OS 애니메이션 임시 끄기 승인 근거(누가·언제)")
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return ri.drive(args, ITEMS, REQ, "desktop_work", os.path.abspath(__file__))


if __name__ == "__main__":
    sys.exit(main())
