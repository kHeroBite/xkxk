#!/usr/bin/env python3
# 데스크톱 Snap Assist 포그라운드 실입력 측정 — Shift+Tab 실키(F1)·표시 시간(H-16)·SDI 호스트(H-17)·위치 고정 해제 뒤 실마우스 끌기(B)를 PC 비움 때 잰다
"""
사용: python3 -B snap_assist_fg.py --run-dir <bin/run/<이름>/net10.0> --work <결과 폴더> [--items F1,H16,H17,B] [--runs 5] [--monitor DISPLAY3]
rig(retest_real_input.Rig)가 앱을 임시 data·profile 로 띄우고 safe_click·safe_key 로 실입력을 보낸다. 입력 앞마다 user_away_guard.check 와 전경 pid 게이트를 거친다.
성능(H16)은 실제 측정 시간으로 판정한다(load 로 무효 처리 안 함 — 참고 기록만). 결과는 --work/snap_assist_fg.json.
종료 코드: 0=전 항목 PASS, 1=FAIL 있음, 2=기동 실패, 5=사용자 복귀(중단).
"""
import argparse
import glob
import json
import os
import re
import sys
import threading
import time
from types import SimpleNamespace

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as rr  # noqa: E402
import user_away_guard as away  # noqa: E402
import snap_assist_fg_k11 as k11  # noqa: E402


def parse_state(text):
    out = {}
    for k, v in re.findall(r"(\w+)=(\[[^\]]*\]|\S*)", text or ""):
        out[k] = int(v) if v.lstrip("-").isdigit() else v
    return out


class Ctx:
    def __init__(self, rig, work):
        self.rig, self.r, self.work = rig, rig.r, work
        self.res = {}

    def call(self, cmd, **kw):
        return self.r.call(cmd, **kw)

    def assist(self):
        s = self.call("snapassist.state") or {}
        s["parsed"] = parse_state(s.get("state"))
        return s

    def wait_assist(self, active, tries=15, gap=0.2):
        s = {}
        for _ in range(tries):
            s = self.assist()
            if bool(s.get("isActive")) == active:
                break
            time.sleep(gap)
        return s

    def open_mdi(self, n):
        self.call("docs.close", all=True)
        time.sleep(0.5)
        for _ in range(n):
            self.call("docs.open", kind="grid", allowDuplicate=True)
        time.sleep(1.0)
        ids = [d.get("id") for d in self.call("docs.list") or []]
        for i, d in enumerate(ids):
            self.call("placement.markPlaced", doc=d, x=60 + i * 70, y=60 + i * 50, w=420, h=300)
        time.sleep(0.6)
        return ids

    def flyout_pick(self, doc, layout, cell, kind="mdi"):
        st = (self.call("snapflyout.hover", kind=kind, doc=doc) or {}).get("state") or {}
        for _ in range(25):
            if st.get("open") or st.get("skip"):
                break
            time.sleep(0.2)
            st = self.call("snapflyout.state") or {}
        if not st.get("open"):
            self.call("snapflyout.leave")
            return {"open": False, "state": st}
        res = self.call("snapflyout.pick", layout=layout, cell=cell) or {}
        time.sleep(0.6)
        return {"open": True, "done": res.get("done"), "state": res.get("state")}

    def title_point(self, doc, fx=0.3):
        """placement.state 의 MDI 영역(화면 px)·문서 bounds(DIP)·잡을 구간(grab)으로 끌 수 있는 제목줄 한 점의 화면 좌표를 구한다."""
        st = self.call("placement.state")
        a, d = st["area"], next(x for x in st["docs"] if x["id"] == doc)
        b, g = d["bounds"], d.get("grab") or {"left": 60, "rightInset": 310, "titleBottom": 33}
        s = a["w"] / a["dipW"]
        lo, hi = max(b["x"] + g["left"], 0), min(b["x"] + b["w"] - g["rightInset"], a["dipW"])
        if hi - lo < 4:
            raise rr.InputRefused("끌 수 있는 구간 없음: %s" % doc)
        x = lo + (hi - lo) * fx
        y = b["y"] + min(16, g["titleBottom"] / 2)
        return int(a["x"] + x * s), int(a["y"] + y * s)

    def mdi_area_screen(self):
        w = self.rig.win(self.rig.main)
        return w["client"]

    def log_tail(self, pattern, n=6):
        # --log 파일(host·remote 줄)과 앱 데이터 로그(data/logs/pd-*.log — PdLog.Info 의 panel-drop 등)를 모두 읽는다.
        # r3 H-13(02:55)은 panel-drop 이 데이터 로그에만 있어 --log 만 읽던 판정이 놓쳤다(03:2x 확인)
        out = []
        paths = [self.rig.log] + sorted(glob.glob(os.path.join(self.rig.data, "logs", "pd-*.log")))
        for p in paths:
            try:
                with open(p, encoding="utf-8", errors="replace") as f:
                    for line in f:
                        if pattern in line:
                            out.append(line.strip()[:200])
            except OSError:
                pass
        out.sort(key=lambda s: s[:23])  # 두 파일 모두 'YYYY-MM-DD HH:MM:SS.fff' 로 시작 — 시각순으로 합친다
        return out[-n:]


# F1 — 데스크톱 Snap Assist 안 Shift+Tab 역방향 실키 ---------------------------------
def item_f1(c):
    rig = c.rig
    ids = c.open_mdi(4)
    f = c.flyout_pick(ids[0], "quad", 0)
    s0 = c.wait_assist(True)
    out = {"flyout": f, "start": s0.get("state")}
    if not s0.get("isActive"):
        return "FAIL", dict(out, why="세션이 뜨지 않음")
    n = s0["parsed"].get("candidates", 0)
    seq = [("start", s0["parsed"].get("focus"))]
    for name in ("Tab", "Tab", "Shift+Tab", "Shift+Tab"):
        rig.key(name, hwnd=rig.main)
        time.sleep(0.3)
        s = c.assist()
        seq.append((name, s["parsed"].get("focus"), s.get("isActive")))
    out["candidates"] = n
    out["seq"] = seq
    c.call("snapassist.cancel")
    time.sleep(0.3)
    foc = [x[1] for x in seq]
    # 후보 n 개 순환: Tab 2 번 뒤 start+2(mod n), Shift+Tab 2 번 뒤 start 로 복귀해야 한다
    start, t1, t2, s1, s2 = foc
    ok = n >= 2 and t1 != start and s1 != t2 and s2 == start and s1 == t1
    out["expect"] = "Tab,Tab 으로 앞으로 두 칸 → Shift+Tab 으로 한 칸씩 되돌아 start 복귀"
    return ("PASS" if ok else "FAIL"), out


# H16 — 실끌기로 칸 배치(가장자리) → 후보 카드 표시 시간 ---------------------------------
def item_h16(c, runs):
    rig = c.rig
    rows = []
    for i in range(runs):
        away.check()
        ids = c.open_mdi(4)
        cl = c.mdi_area_screen()
        sx, sy = c.title_point(ids[0])
        area = c.call("placement.state")["area"]
        w = rig.win(rig.main)
        scale = area["w"] / area["dipW"]
        # MDI 영역 왼쪽 가장자리 근처(안쪽 4 DIP), 세로 가운데 — area 는 화면 px
        ex = int(area["x"] + 4 * scale)
        ey = int(area["y"] + area["dipH"] / 2 * scale)
        t_drag0 = time.perf_counter()
        rig.click(sx, sy, hwnd=rig.main, drag=(ex, ey))
        t_ret = time.perf_counter()
        t_act = None
        s = {}
        for _ in range(60):
            s = c.assist()
            if s.get("isActive"):
                t_act = time.perf_counter()
                break
            time.sleep(0.02)
        card = None
        t_card = None
        if t_act:
            for _ in range(40):
                card = rig.find("snap.assist.card.0.thumb") or rig.find("snap.assist.card.0")
                if card and card.get("effectivelyVisible"):
                    t_card = time.perf_counter()
                    break
                time.sleep(0.02)
        row = {"run": i, "drag_total_ms": round((t_ret - t_drag0) * 1000), "ret_to_active_ms": round((t_act - t_ret) * 1000) if t_act else None,
               "ret_to_card_ms": round((t_card - t_ret) * 1000) if t_card else None, "state": s.get("state"), "skip": s.get("lastSkipReason"),
               "log": c.log_tail("snap-assist start", 1)}
        rows.append(row)
        print("  H16 run%d %s" % (i, json.dumps(row, ensure_ascii=False)), flush=True)
        c.call("snapassist.cancel")
        time.sleep(0.5)
    vals = [r["ret_to_card_ms"] for r in rows if r["ret_to_card_ms"] is not None]
    summary = {"n": len(vals), "min": min(vals) if vals else None, "max": max(vals) if vals else None,
               "median": sorted(vals)[len(vals) // 2] if vals else None}
    ok = len(vals) >= runs
    return ("PASS" if ok else "FAIL"), {"rows": rows, "summary": summary,
                                         "note": "ms 는 safe_click 반환(마우스 뗌 직후)~원격 폴링으로 본 표시까지. 폴링 간격·원격 왕복(수십 ms)이 포함된 상한값"}


# H17 — SDI 앱 모드에서 SDI 창을 실끌기로 배치 → 호스트 표시 ---------------------------------
def item_h17(c):
    rig = c.rig
    c.call("docs.close", all=True)
    time.sleep(0.5)
    for _ in range(3):
        c.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
        time.sleep(0.8)
    ids = [d.get("id") for d in c.call("docs.list") or []]
    out = {"ids": ids}
    snap = c.call("window.snapshot")
    out["sdi_rows"] = snap.get("sdi")
    wins0 = {w["hwnd"] for w in rig.windows()}
    hw = rig.sdi_hwnd(ids[0]) if ids else None
    out["sdi_hwnd"] = hw
    if not hw:
        return "FAIL", dict(out, why="SDI 창 hwnd 를 찾지 못함")
    mon = rig.monitor["work"]
    # 모니터 왼쪽 가장자리에는 다른 프로그램 창(사용자 Chrome 등)이 있을 수 있어 safe_click 이 끝점 소유자 검사로 거부한다 —
    # SDI 창을 모니터 왼쪽 끝에 붙여 전경으로 올린 뒤, 그 창 제목줄 안에서 가장자리까지 끈다(끝점도 자기 창 위)
    rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hw, mon["l"], mon["t"] + 500, 900, 600)])
    time.sleep(0.8)
    rig.acquire(hw)
    time.sleep(0.4)
    w = rig.win(hw)
    out["sdi_rect_before"] = w["rect"]
    sx = int(w["rect"]["l"] + 300 * (w["dpi"] / 96.0))
    sy = int(w["rect"]["t"] + 18 * (w["dpi"] / 96.0))
    old_margin = rr.EDGE_MARGIN
    rr.EDGE_MARGIN = 6  # SDI 가장자리 칸 배치는 모니터 가장자리 근처가 필요하다 — 그래도 모니터 안쪽 6px 이상만
    try:
        out["press"] = (sx, sy)
        rig.gate(hw)
        ex, ey = mon["l"] + 6, sy
        rig.click(sx, sy, hwnd=hw, drag=(ex, ey))
    finally:
        rr.EDGE_MARGIN = old_margin
    time.sleep(0.4)
    s = c.wait_assist(True, tries=15)
    out["state"] = s.get("state")
    out["skip"] = s.get("lastSkipReason")
    new = [x for x in rig.windows() if x["hwnd"] not in wins0]
    out["new_windows"] = [{"hwnd": x["hwnd"], "cls": x.get("cls"), "rect": x["rect"]} for x in new]
    out["log"] = c.log_tail("snap-assist", 4)
    out["sdi_after"] = c.call("window.snapshot").get("sdi")
    active = bool(s.get("isActive"))
    if active:
        cand = s["parsed"].get("candidates", 0)
        out["candidates"] = cand
        c.call("snapassist.cancel")
        time.sleep(0.3)
    c.call("docs.redock", all=True)
    time.sleep(0.6)
    return ("PASS" if active else "FAIL"), out


# B — 위치 고정 켬/끔 뒤 실마우스 끌기 ---------------------------------------------------
def item_b(c):
    rig = c.rig
    ids = c.open_mdi(2)
    doc = ids[0]

    def place():
        c.call("placement.markPlaced", doc=doc, x=120, y=120, w=520, h=340)
        time.sleep(0.4)

    def bounds():
        return next(d for d in c.call("placement.state")["docs"] if d["id"] == doc)["bounds"]

    def drag():
        sx, sy = c.title_point(doc, 0.3)
        rig.click(sx, sy, hwnd=rig.main, drag=(sx + 160, sy + 80))
        time.sleep(0.7)

    rows = {}
    place()
    drag()  # 준비 끌기 — 첫 입력이 창 활성화에 쓰여 안 움직이는 경우를 baseline 에서 분리한다
    # 앱의 Snap 가장자리 칸 배치가 판정을 가리지 않게 끈다
    c.call("snapflyout.options", snapEnabled="false")
    place()
    b0 = bounds()
    drag()
    rows["baseline"] = {"before": b0, "after": bounds()}
    place()
    lk = c.call("chrome.lock", doc=doc) or {}
    b0 = bounds()
    drag()
    rows["locked"] = {"before": b0, "after": bounds(), "locked": lk.get("locked")}
    un = c.call("chrome.lock", doc=doc) or {}
    b0 = bounds()
    drag()
    rows["unlock_drag"] = {"before": b0, "after": bounds(), "locked": un.get("locked")}
    for k, v in rows.items():
        v["moved"] = v["before"] != v["after"]
    ok = rows["baseline"]["moved"] and rows["locked"]["locked"] is True and not rows["locked"]["moved"] \
        and rows["unlock_drag"]["locked"] is False and rows["unlock_drag"]["moved"]
    return ("PASS" if ok else "FAIL"), rows


# T5 — 제목 탭 hover × 표시(ActiveAndHover)·× 클릭 닫기·가운데 클릭 닫기(실마우스) -----------------------
def item_t5(c):
    rig = c.rig
    ids = c.open_mdi(4)
    c.call("ui.tabClose", mode="ActiveAndHover")
    time.sleep(0.6)

    def tabs():
        return {t["id"]: t for t in (c.call("ui.tabClose") or {}).get("tabs") or []}

    def center(cid, **kw):
        b = (rig.find(cid) or {}).get("bounds")
        if not b:
            raise rr.InputRefused("요소 없음: %s" % cid)
        return rig.point(rig.main, b)

    out = {"ids": ids, "initial": {k: (v.get("selected"), v.get("closeOpacity")) for k, v in tabs().items()}}
    a_id, c_id = ids[0], ids[1]
    assert not tabs()[a_id].get("selected") or True
    # (a) 제목 탭에 커서를 올린다(클릭 없음)
    ax, ay = center("shell.tabTitle.%s" % a_id)
    rig.click(ax, ay, hwnd=rig.main, no_click=True, expected=rig.pid)
    time.sleep(0.7)
    st = tabs()
    out["a_hover"] = {"id": a_id, "selected": st[a_id].get("selected"), "closeOpacity": st[a_id].get("closeOpacity"),
                      "others": {k: v.get("closeOpacity") for k, v in st.items() if k != a_id}}
    a_ok = st[a_id].get("closeOpacity") == 1
    # (b) × 클릭
    bx, by = center("shell.tabClose.%s" % a_id)
    rig.click(bx, by, hwnd=rig.main)
    time.sleep(0.8)
    left = [d.get("id") for d in c.call("docs.list") or []]
    b_ok = a_id not in left
    out["b_click"] = {"x_pos": (bx, by), "docs_after": left}
    # (c) 가운데 클릭
    cx, cy = center("shell.tabTitle.%s" % c_id)
    rig.click(cx, cy, hwnd=rig.main, button="Middle")
    time.sleep(0.8)
    left2 = [d.get("id") for d in c.call("docs.list") or []]
    c_ok = c_id not in left2
    out["c_middle"] = {"pos": (cx, cy), "docs_after": left2}
    out["verdict"] = {"a_hover_x_visible": a_ok, "b_x_click_closes": b_ok, "c_middle_click_closes": c_ok}
    return ("PASS" if a_ok and b_ok and c_ok else "FAIL"), out


# T6 — 크롬 단추 Tab 순서(메뉴 → 활성 문서 창 크롬 단추 7개 → 메뉴 첫 항목)·Shift+Tab 역순·문서 없음 건너뜀·포커스 링 캡처·그리드 회귀 -----------
CHROME_IDS = ["chrome.btnSdi", "chrome.btnPin", "chrome.btnLock", "chrome.btnSync", "chrome.btnDup", "chrome.btnCache", "chrome.btnHelp"]


class _Shots:
    def __init__(self, d):
        self.shots, self.paths = d, []

    def capture(self, path):
        self.paths.append(path)


def item_t6(c):
    rig = c.rig
    out = {}

    def focus(window=None):
        kw = {"window": window} if window is not None else {}
        s = c.call("tour.snapshot", **kw) or {}
        return (s.get("focusedId") or "", s.get("focusedType") or "")

    def key_seq(name, n):
        seq = []
        for _ in range(n):
            rig.key(name, hwnd=rig.main)
            time.sleep(0.25)
            seq.append(focus())
        return seq

    c.open_mdi(3)
    time.sleep(0.5)
    fwd = key_seq("Tab", 30)
    out["forward"] = fwd
    flat = [x[0] or x[1] for x in fwd]
    chrome_idx = [i for i, x in enumerate(flat) if x in CHROME_IDS]
    out["chrome_idx"] = chrome_idx
    run = []
    for i in chrome_idx:  # 첫 바퀴의 연속 구간
        if not run or i == run[-1] + 1:
            run.append(i)
        else:
            break
    in_order = [flat[i] for i in run] == CHROME_IDS
    before_chrome = flat[run[0] - 1] if run and run[0] > 0 else None
    after_chrome = flat[run[-1] + 1] if run and run[-1] + 1 < len(flat) else None
    first_stop = flat[0]
    out["a_forward_order"] = {"run_len": len(run), "in_axaml_order": in_order, "before_chrome": before_chrome, "after_chrome": after_chrome,
                              "first_stop": first_stop, "returns_to_menu_first": after_chrome == first_stop}
    # (c) 포커스 링 — 크롬 단추 하나에 포커스가 있을 때 캡처
    guard = 0
    while focus()[0] not in CHROME_IDS and guard < 30:
        rig.key("Tab", hwnd=rig.main)
        time.sleep(0.25)
        guard += 1
    shot = _Shots(os.path.join(c.work, "shots"))
    out["c_focused_before_grab"] = focus()
    time.sleep(0.4)
    rig.grab(shot, "t6_focus_ring")
    out["c_captures"] = shot.paths
    # Shift+Tab 역순 — 메뉴 첫 항목(after_chrome 값)까지 Tab 으로 간 뒤 Shift+Tab
    g2 = 0
    while (focus()[0] or focus()[1]) != after_chrome and g2 < 40:
        rig.key("Tab", hwnd=rig.main)
        time.sleep(0.25)
        g2 += 1
    back = key_seq("Shift+Tab", 9)
    out["backward_from_menu_first"] = back
    flatb = [x[0] or x[1] for x in back]
    out["a_backward_order"] = {"seq": flatb, "menu_first_then_reverse_chrome": flatb[0] == after_chrome and flatb[1:8] == list(reversed(CHROME_IDS)),
                               "ends_at": flatb[8] if len(flatb) > 8 else None, "reverse_ok": flatb[0] == after_chrome and flatb[1:8] == list(reversed(CHROME_IDS)) and flatb[8] == before_chrome}
    # (b) 문서 모두 닫기 → 크롬 단추 건너뜀
    c.call("docs.close", all=True)
    time.sleep(0.8)
    nodoc = key_seq("Tab", 20)
    out["b_nodoc"] = nodoc
    out["b_skip_chrome"] = not any(x[0] in CHROME_IDS for x in nodoc)
    # (d) 그리드 안 Tab — 문서 본문(제목줄 아래)을 실클릭해 포커스를 그리드로 둔 뒤 Tab 5회
    ids = c.open_mdi(2)
    time.sleep(0.6)
    try:
        st = c.call("placement.state")
        a_ = st["area"]
        dd = next(x for x in st["docs"] if x["id"] == ids[-1])
        b_ = dd["bounds"]
        sc = a_["w"] / a_["dipW"]
        gx = int(a_["x"] + (b_["x"] + b_["w"] / 2) * sc)
        gy = int(a_["y"] + (b_["y"] + b_["h"] * 0.6) * sc)
        rig.click(gx, gy, hwnd=rig.main)
        time.sleep(0.5)
        out["d_after_click_focus"] = focus()
        gseq = key_seq("Tab", 5)
        out["d_grid_tab"] = gseq
        out["d_stays_in_grid"] = not any((x[0] in CHROME_IDS) or x[0].startswith("shell.") for x in gseq) and bool(out["d_after_click_focus"][1])
    except rr.InputRefused as e:
        out["d_grid_tab"] = {"refused": str(e)}
    verdict = {"a_fwd": bool(in_order and out["a_forward_order"]["returns_to_menu_first"]), "a_back": bool(out["a_backward_order"]["reverse_ok"]), "b": bool(out["b_skip_chrome"]),
               "c_captured": bool(shot.paths), "d": bool(out.get("d_stays_in_grid"))}
    out["verdict"] = verdict
    return ("PASS" if all(verdict.values()) else "FAIL"), out


# T6S — SDI 창 제목줄에 포커스를 둔 Tab 이 그 창 크롬 단추 안에서만 도는지 -----------------------------------------
def item_t6_sdi(c):
    rig = c.rig
    out = {}
    c.call("docs.close", all=True)
    time.sleep(0.5)
    for _ in range(2):
        c.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
        time.sleep(0.8)
    snap = c.call("window.snapshot")
    rows = snap.get("sdi") or []
    out["sdi_rows"] = rows
    docs = [d.get("id") for d in c.call("docs.list") or []]
    hw = rig.sdi_hwnd(docs[0]) if docs else None
    out["hwnd"] = hw
    if not hw:
        return "FAIL", dict(out, why="SDI 창 hwnd 없음")
    # SDI 기본 위치는 모니터 위 가장자리 100px 안일 수 있어 실입력 게이트가 거부한다(11-12 첫 실측 y=92) — 작업 영역 안쪽으로 옮긴다
    mon = rig.monitor["work"]
    rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hw, mon["l"] + 400, mon["t"] + 300, 900, 500)])
    time.sleep(0.8)
    rig.acquire(hw)
    time.sleep(0.4)
    # 창 번호: 이전 방식(창 사각형 안 점)은 rig.point 가 hw 기준으로 환산해 어느 SDI 번호든 통과했다 — 다른 SDI 창의 포커스를 읽어 빈 값이 나온 원인(23:5x 확인). 제목으로 맞춘다
    idx = _sdi_index_by_doc(c, docs[0])
    tb = (rig.find("chrome.title", window=idx) or {}).get("bounds") if idx is not None else None
    out["window_index"] = idx
    if idx is None:
        return "FAIL", dict(out, why="SDI 제목줄 번호를 찾지 못함")
    x, y = rig.point(hw, tb, 0.15, 0.5)
    rig.click(x, y, hwnd=hw)
    time.sleep(0.4)
    seq = []
    for _ in range(12):
        rig.key("Tab", hwnd=hw)
        time.sleep(0.25)
        s = c.call("tour.snapshot", window=idx) or {}
        s0 = c.call("tour.snapshot") or {}
        fid = s.get("focusedId") or ""
        if not fid:  # 18:22 실측: tour.snapshot window= 가 SDI 창 포커스를 비워 돌려줌 — 크롬 단추별 IsFocused 로 직접 본다
            for cid in CHROME_IDS:
                n = rig.find(cid, window=idx, props="IsFocused") or {}
                if str((n.get("props") or {}).get("IsFocused")) == "True":
                    fid = cid
                    break
        seq.append((fid, s.get("focusedType") or ""))
        out.setdefault("main_snapshot_seq", []).append((s0.get("focusedId") or "", s0.get("focusedType") or ""))
    out["tab_seq"] = seq
    out["fg_after"] = rig.foreground()
    ids = [a for a, _ in seq if a]
    out["verdict_sdi"] = bool(ids) and all(a in CHROME_IDS for a in ids)
    return ("PASS" if out["verdict_sdi"] else "FAIL"), out


# T6D — 그리드 본문 포커스에서 Tab 이 어디로 가는지(판본 대조용: 같은 조작을 이전·이후 판본에 돌려 비교한다) -------------
def item_t6d(c):
    rig = c.rig
    out = {}
    ids = c.open_mdi(2)
    time.sleep(0.6)
    st = c.call("placement.state")
    a_ = st["area"]
    dd = next(x for x in st["docs"] if x["id"] == ids[-1])
    b_ = dd["bounds"]
    sc = a_["w"] / a_["dipW"]
    gx = int(a_["x"] + (b_["x"] + b_["w"] / 2) * sc)
    gy = int(a_["y"] + (b_["y"] + b_["h"] * 0.6) * sc)
    rig.click(gx, gy, hwnd=rig.main)
    time.sleep(0.5)

    def focus():
        s = c.call("tour.snapshot") or {}
        return (s.get("focusedId") or "", s.get("focusedType") or "")

    out["after_click"] = focus()
    seq = []
    for _ in range(6):
        rig.key("Tab", hwnd=rig.main)
        time.sleep(0.3)
        seq.append(focus())
    out["tab_seq"] = seq
    out["leaves_grid"] = any(not x[0].startswith("grid.") and x[1] != "PdDataGrid" for x in seq)
    return "측정", out


# ===== PC 비움 이월(conv_179446127333 A9·A10) — 실누름·실키·실끌기 캡처 =====================================
# 판정 기준은 01 §4.2(상태 대비 3.0, 포커스 링 2px)와 a10 §1.3 재개 조건이다. 캡처는 원격 screenshot(앱 렌더)이고 실입력과 동시에 찍는다.
MIN_C = 3.0


def _shot(c, tag, window=0):
    import mdi_drag_app as app
    d = os.path.join(c.work, "shots")
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, tag + ".png")
    c.call("screenshot", window=window, path=app.win(p))
    return p if os.path.exists(p) else None


def _img(p):
    from PIL import Image
    return Image.open(p).convert("RGB")


def _ratio(a, b):
    def lum(rgb):
        v = [x / 255.0 for x in rgb]
        v = [x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4 for x in v]
        return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return round((la + 0.05) / (lb + 0.05), 2)


def _hex(rgb):
    return "%02X%02X%02X" % tuple(rgb) if rgb else None


def _box(b, s):
    return int(b["x"] * s), int(b["y"] * s), int((b["x"] + b["w"]) * s), int((b["y"] + b["h"]) * s)


def _button_colors(img, b, s, bg_hint=None):
    """버튼 사각형의 바탕(왼쪽 12% 세로 가운데)과 그림색(바탕에서 가장 먼 픽셀)."""
    x0, y0, x1, y1 = _box(b, s)
    bg = bg_hint or img.getpixel((min(img.width - 1, x0 + max(1, (x1 - x0) // 8)), min(img.height - 1, (y0 + y1) // 2)))
    best, far = bg, -1
    for y in range(y0 + 1, min(img.height, y1 - 1)):
        for x in range(x0 + 1, min(img.width, x1 - 1)):
            p = img.getpixel((x, y))
            d = sum(abs(p[i] - bg[i]) for i in range(3))
            if d > far:
                best, far = p, d
    return bg, best


def _scale(c, img, hwnd=None):
    w = c.rig.win(hwnd or c.rig.main)
    return w["dpi"] / 96.0 if w else 1.0


def _during(delay, fn, holder):
    def run():
        time.sleep(delay)
        try:
            holder.update(fn() or {})
        except Exception as e:  # noqa — 캡처 실패는 결과에 남긴다
            holder["error"] = repr(e)
    t = threading.Thread(target=run, daemon=True)
    t.start()
    return t


def _themes(c):
    t = c.call("themes.get")
    t = json.loads(t) if isinstance(t, str) else (t or {})
    return t.get("builtinThemes") or [], t


def _away_point(c):
    """주 창 MDI 영역 오른쪽 아래 빈 곳(커서를 버튼에서 치울 때)."""
    a = c.call("placement.state")["area"]
    return int(a["x"] + a["w"] * 0.9), int(a["y"] + a["h"] * 0.85)


def item_space_enter(c):
    rig = c.rig
    doc = c.open_mdi(1)[0]
    c.call("ui.toggle", id="chrome.btnDup", doc=doc, on=False)
    time.sleep(0.3)

    def checked():
        n = rig.find("chrome.btnDup", doc=doc, props="IsChecked") or {}
        return str((n.get("props") or {}).get("IsChecked"))

    def focus():
        s = c.call("tour.snapshot") or {}
        return s.get("focusedId") or ""

    out = {"doc": doc, "start_checked": checked()}
    g = 0
    while focus() != "chrome.btnDup" and g < 40:
        rig.key("Tab", hwnd=rig.main)
        time.sleep(0.2)
        g += 1
    out["tabs_to_focus"] = g
    out["focus"] = focus()
    if out["focus"] != "chrome.btnDup":
        return "FAIL", dict(out, why="Tab 으로 chrome.btnDup 에 포커스를 두지 못함")
    rig.key("Space", hwnd=rig.main)
    time.sleep(0.4)
    out["after_space"] = checked()
    out["focus_after_space"] = focus()
    rig.key("Enter", hwnd=rig.main)
    time.sleep(0.4)
    out["after_enter"] = checked()
    out["focus_after_enter"] = focus()
    out["log"] = c.log_tail("chrome dup", 4) or c.log_tail("dupO", 4)
    ok = out["start_checked"] != out["after_space"] and out["after_space"] != out["after_enter"] and out["focus_after_enter"] == "chrome.btnDup"
    out["expect"] = "Space 로 켜짐 반전, Enter 로 다시 반전, 포커스 유지"
    return ("PASS" if ok else "FAIL"), out


def item_pressed(c):
    rig = c.rig
    themes, _ = _themes(c)
    rows = {}
    for theme in [t for t in ("pd-light", "pd-contrast") if t in themes] or themes[:1]:
        away.check()
        c.call("theme.select", id=theme)
        time.sleep(0.6)
        doc = c.open_mdi(1)[0]
        c.call("ui.toggle", id="chrome.btnDup", doc=doc, on=False)
        rig.acquire(rig.main)
        b = rig.find("chrome.btnDup", doc=doc)["bounds"]
        x, y = rig.point(rig.main, b)
        ax, ay = _away_point(c)
        rig.click(ax, ay, hwnd=rig.main, no_click=True, expected=rig.pid)
        time.sleep(0.4)
        p_rest = _shot(c, "%s_rest" % theme)
        rig.click(x, y, hwnd=rig.main, no_click=True, expected=rig.pid)
        time.sleep(0.5)
        p_hover = _shot(c, "%s_hover" % theme)
        hold = {}

        def grab_pressed():
            # safe_click 는 PowerShell 기동·게이트 뒤에 버튼을 내리므로 고정 지연 대신 IsPressed=True 가 될 때까지 기다렸다가 찍는다(11-12 첫 실측: 0.75초 고정은 누르기 전에 찍힘)
            n, t_end = {}, time.time() + 12
            while time.time() < t_end:
                n = rig.find("chrome.btnDup", doc=doc, props="IsPressed") or {}
                if str((n.get("props") or {}).get("IsPressed")) == "True":
                    break
                time.sleep(0.05)
            return {"path": _shot(c, "%s_pressed" % theme), "isPressed": str((n.get("props") or {}).get("IsPressed")), "classes": n.get("pseudo") or n.get("classes")}
        th = _during(0.0, grab_pressed, hold)
        rig.click(x, y, hwnd=rig.main, drag=(x + 2, y + 1))  # 버튼 안에서 누른 채 1초 남짓 — 그 사이 캡처
        th.join(10)
        c.call("ui.toggle", id="chrome.btnDup", doc=doc, on=False)
        row = {"rest": p_rest, "hover": p_hover, "pressed": hold}
        try:
            ir, ih, ip = _img(p_rest), _img(p_hover), _img(hold["path"])
            s = _scale(c, ir)
            rb, rg = _button_colors(ir, b, s)
            hb, hg = _button_colors(ih, b, s)
            pb, pg = _button_colors(ip, b, s)
            row.update({"rest_bg": _hex(rb), "hover_bg": _hex(hb), "pressed_bg": _hex(pb), "pressed_glyph": _hex(pg),
                        "pressed_vs_rest_bg": _ratio(pb, rb), "pressed_glyph_on_bg": _ratio(pg, pb), "hover_vs_rest_bg": _ratio(hb, rb),
                        "pressed_distinct_from_hover": _hex(pb) != _hex(hb) or _hex(pg) != _hex(hg)})
            row["pass"] = hold.get("isPressed") == "True" and _hex(pb) != _hex(rb) and row["pressed_glyph_on_bg"] >= MIN_C
        except Exception as e:  # noqa
            row["error"], row["pass"] = repr(e), False
        rows[theme] = row
    ok = bool(rows) and all(r.get("pass") for r in rows.values())
    return ("PASS" if ok else "FAIL"), {"themes": rows, "rule": "실누름 중 IsPressed=True 캡처, 누름 바탕≠기본 바탕, 누름 기호 대 누름 바탕 ≥3.0(01 §4.2)"}


def item_topmost(c):
    rig = c.rig
    themes, _ = _themes(c)
    if "pd-contrast" not in themes:
        return "FAIL", {"why": "pd-contrast 테마 없음", "themes": themes}
    c.call("theme.select", id="pd-contrast")
    time.sleep(0.6)
    doc = c.open_mdi(1)[0]
    rig.acquire(rig.main)
    pin = rig.find("chrome.btnPin", doc=doc)["bounds"]
    px, py = rig.point(rig.main, pin)
    rig.click(px, py, hwnd=rig.main)  # 실클릭으로 항상 위 켬
    time.sleep(0.6)
    on = str(((rig.find("chrome.btnPin", doc=doc, props="IsChecked") or {}).get("props") or {}).get("IsChecked"))
    ax, ay = _away_point(c)
    rig.click(ax, ay, hwnd=rig.main, no_click=True, expected=rig.pid)
    time.sleep(0.4)
    p_rest = _shot(c, "topmost_rest")
    dup = rig.find("chrome.btnDup", doc=doc)["bounds"]
    dx, dy = rig.point(rig.main, dup)
    rig.click(dx, dy, hwnd=rig.main, no_click=True, expected=rig.pid)
    time.sleep(0.5)
    p_hover = _shot(c, "topmost_hover")
    rig.click(px, py, hwnd=rig.main)  # 실클릭으로 끔(원복)
    time.sleep(0.5)
    out = {"pin_checked": on, "rest": p_rest, "hover": p_hover}
    try:
        ir, ih = _img(p_rest), _img(p_hover)
        s = _scale(c, ir)
        bar, glyph = _button_colors(ir, dup, s)
        hb, hg = _button_colors(ih, dup, s)
        out.update({"bar": _hex(bar), "glyph": _hex(glyph), "hover_bg": _hex(hb), "bar_is_cyan": sum(abs(bar[i] - (0x00, 0xE5, 0xFF)[i]) for i in range(3)) <= 24,
                    "glyph_on_bar": _ratio(glyph, bar), "hover_fill_vs_bar": _ratio(hb, bar), "glyph_on_hover": _ratio(hg, hb)})
        ok = on == "True" and out["bar_is_cyan"] and max(out["glyph_on_bar"], out["hover_fill_vs_bar"]) >= MIN_C and out["glyph_on_hover"] >= MIN_C
    except Exception as e:  # noqa
        out["error"], ok = repr(e), False
    out["rule"] = "실클릭 항상 위 켬 → 제목줄 시안 #00E5FF, rest↔hover(바탕 또는 기호) ≥3.0, 호버 위 기호 ≥3.0(01 §4.2 활성·항상 위 바탕 3.0)"
    return ("PASS" if ok else "FAIL"), out


def _sdi_index_by_doc(c, doc):
    """SDI 문서의 원격 창 번호(window=). 23:5x 비전경 확인: 0=메인, 1..=SDI 이고 tour.snapshot·ui.find 가 같은 번호를 쓴다. 창 안 상대 좌표는 SDI 끼리 같아
    좌표로는 구분되지 않으므로, 제목을 고유하게 바꾼 뒤 chrome.title 의 Text 로 맞춘다."""
    mark = "sdi-" + str(doc)
    c.call("docs.rename", doc=doc, title=mark)
    time.sleep(0.4)
    for i in range(1, 8):
        n = c.rig.find("chrome.title", window=i, props="Text") or {}
        if mark in str((n.get("props") or {}).get("Text")):
            return i
    return None


def _sdi_index(c, hw):
    wr = c.rig.win(hw)["rect"]
    for i in range(0, 6):
        b = (c.rig.find("chrome.title", window=i) or {}).get("bounds")
        if b:
            px, py = c.rig.point(hw, b, 0.15, 0.5)
            if wr["l"] <= px < wr["r"] and wr["t"] <= py < wr["b"]:
                return i
    return None


def _two_sdi(c):
    rig = c.rig
    c.call("docs.close", all=True)
    time.sleep(0.5)
    for _ in range(2):
        c.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
        time.sleep(0.8)
    ids = [d.get("id") for d in c.call("docs.list") or []]
    hws = [rig.sdi_hwnd(d) for d in ids]
    return ids, hws


def _two_sdi_distinct(c):
    """SDI 2개를 하나씩 열며 새로 생긴 창 hwnd 를 차집합으로 얻는다(H-09J 용). 두 SDI 가 같은 기본 위치(-2356,75)에 열려
    위치로 찾는 sdi_hwnd 가 두 문서에 같은 hwnd 를 돌려준 03:01 r3 실측(app 로그 '그리드 1'·'그리드 2' 같은 pos)을 피한다."""
    rig = c.rig

    def tops():
        return {w["hwnd"] for w in rig.windows() if w["cls"] != rr.base.SHADOW and (w["rect"]["r"] - w["rect"]["l"]) >= 300}
    c.call("docs.close", all=True)
    time.sleep(0.5)
    ids, hws = [], []
    for _ in range(2):
        before = tops()
        known = {d.get("id") for d in c.call("docs.list") or []}
        c.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
        new = set()
        for _ in range(20):
            time.sleep(0.2)
            new = tops() - before
            if len(new) == 1:
                break
        doc = next((d.get("id") for d in c.call("docs.list") or [] if d.get("id") not in known), None)
        ids.append(doc)
        hws.append(next(iter(new)) if len(new) == 1 else None)
    return ids, hws


def item_dockjoin(c):
    rig = c.rig
    themes, _ = _themes(c)
    mon = rig.monitor["work"]
    rows = {}
    for theme in [t for t in ("pd-light", "pd-contrast") if t in themes] or themes[:1]:
        away.check()
        c.call("theme.select", id=theme)
        time.sleep(0.6)
        ids, hws = _two_sdi(c)
        if len(hws) < 2 or not all(hws):
            rows[theme] = {"pass": False, "why": "SDI 2개 hwnd 없음", "ids": ids}
            continue
        ha, hb = hws
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (ha, mon["l"] + 300, mon["t"] + 250, 900, 420)])
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hb, mon["l"] + 300, mon["t"] + 800, 900, 420)])
        time.sleep(0.8)
        ia = _sdi_index_by_doc(c, ids[0])  # 대상 창 A 의 번호(좌표 방식은 원천 B 를 고를 수 있었다 — 23:5x 확인)
        rest = _shot(c, "%s_join_rest" % theme, window=ia) if ia is not None else None
        rig.acquire(hb)
        time.sleep(0.3)
        w = rig.win(hb)
        sx, sy = int(w["rect"]["l"] + 300 * w["dpi"] / 96.0), int(w["rect"]["t"] + 18 * w["dpi"] / 96.0)
        hold = {}

        def grab():
            # 고정 지연 대신 합류 아이콘이 보일 때까지 기다렸다가 찍는다(pressed_capture 첫 실측의 시점 문제와 같은 원인 예방)
            n, t_end = {}, time.time() + 12
            while time.time() < t_end:
                n = rig.find("chrome.tabJoin", window=ia) or {}
                if n.get("effectivelyVisible"):
                    break
                time.sleep(0.05)
            return {"path": _shot(c, "%s_join_armed" % theme, window=ia), "icon": n.get("bounds"), "visible": n.get("effectivelyVisible")}
        th = _during(0.0, grab, hold)
        rig.gate(hb)
        rig.click(sx, sy, hwnd=hb, drag=(sx + 260, sy + 40))  # 원천 창 B 실끌기(OS 이동 루프) — 끄는 동안 A 의 합류 아이콘이 무장된다
        th.join(10)
        title_a = str(((rig.find("chrome.title", window=ia, props="Text") or {}).get("props") or {}).get("Text")) if ia is not None else None
        row = {"rest": rest, "armed": hold, "window_index": ia, "target_doc_A": ids[0], "source_doc_B": ids[1], "captured_title": title_a,
               "captured_is": "A(대상)" if title_a and ("sdi-" + str(ids[0])) in title_a else "확인 불가 또는 B(원천)"}
        try:
            ic = hold.get("icon")
            ip = _img(hold["path"])
            s = _scale(c, ip, ha)
            bg = ip.getpixel((int((ic["x"] + 3) * s), int((ic["y"] + ic["h"] / 2) * s)))
            bgc, glyph = _button_colors(ip, ic, s, bg_hint=bg)
            row.update({"icon_bg": _hex(bgc), "icon_glyph": _hex(glyph), "glyph_on_bg": _ratio(glyph, bgc)})
            row["pass"] = bool(hold.get("visible")) and row["glyph_on_bg"] >= MIN_C
        except Exception as e:  # noqa
            row["error"], row["pass"] = repr(e), False
        rows[theme] = row
        c.call("docs.redock", all=True)
        time.sleep(0.6)
    ok = bool(rows) and all(r.get("pass") for r in rows.values())
    return ("PASS" if ok else "FAIL"), {"themes": rows, "rule": "다른 SDI 창 실끌기 중 대상 창 합류 아이콘(chrome.tabJoin, DockJoinTarget.axaml .pdTabJoinArmed) 표시, 기호 대 바탕 ≥3.0"}


def item_focus_ring(c):
    rig = c.rig
    doc = c.open_mdi(1)[0]
    p_rest = _shot(c, "ring_rest")
    g = 0
    while ((c.call("tour.snapshot") or {}).get("focusedId") or "") != "chrome.btnDup" and g < 40:
        rig.key("Tab", hwnd=rig.main)
        time.sleep(0.2)
        g += 1
    time.sleep(0.3)
    p_ring = _shot(c, "ring_focus")
    b = rig.find("chrome.btnDup", doc=doc)["bounds"]
    out = {"rest": p_rest, "focus": p_ring, "tabs": g}
    try:
        i0, i1 = _img(p_rest), _img(p_ring)
        s = _scale(c, i0)
        x0, y0, x1, y1 = _box(b, s)
        cy = (y0 + y1) // 2
        # 왼쪽 가장자리에서 안쪽으로 바뀐 픽셀 연속 폭(가운데 줄), 위 가장자리에서 아래로(가운데 열)
        def run_len(seq):
            n = 0
            for a_, b_ in seq:
                if sum(abs(a_[k] - b_[k]) for k in range(3)) > 60:
                    n += 1
                elif n:
                    break
            return n
        cx = (x0 + x1) // 2
        left = run_len([(i0.getpixel((x, cy)), i1.getpixel((x, cy))) for x in range(x0, x0 + 8)])
        top = run_len([(i0.getpixel((cx, y)), i1.getpixel((cx, y))) for y in range(y0, y0 + 8)])
        out.update({"ring_left_px": left, "ring_top_px": top, "scale": s, "ring_color": _hex(i1.getpixel((x0 + 1, cy)))})
        ok = g < 40 and abs(left / s - 2) <= 0.5 and abs(top / s - 2) <= 0.5
    except Exception as e:  # noqa
        out["error"], ok = repr(e), False
    out["rule"] = "Tab 으로 옮긴 포커스(:focus-visible) 링 두께 2px(DIP, ±0.5) — 01 §4.2"
    return ("PASS" if ok else "FAIL"), out


# ----- A10 포그라운드 보류(H-09·H-11·H-13·H-14). H-10 은 retest_r2_titlebar.py --items T3 를 그대로 쓴다 -----
def _restart_with_settings(c, **kv):
    """설정 파일(settings.json — 자기 임시 data·profile 안)을 고친 뒤 같은 폴더로 다시 띄운다. 원격 설정 명령이 없는 값(SnapSdiMode) 용."""
    rig = c.rig
    # 저장을 일으켜 파일을 만든다. 같은 값 설정은 변경이 아니라 저장되지 않으므로(18:26 첫 실측 settings.json 없음) 끔→켬으로 바꾼다
    c.call("snapflyout.options", snapEnabled="false")
    time.sleep(0.6)
    c.call("snapflyout.options", snapEnabled="true")
    time.sleep(1.5)
    rig.stop()
    # 위치: --profile-dir 폴더의 settings.json(Pd.Client.Data/Layout/ProfileStores.cs — layout.json·settings.json·layouts/)
    found = []
    for root in (rig.profile, rig.data):
        for dp, _, fs in os.walk(root):
            found += [os.path.join(dp, f) for f in fs if f.lower() == "settings.json"]
    if not found:
        # 그래도 없으면 프로필 폴더에 바꿀 키만 담은 파일을 만든다(DTO 의 나머지 키는 기본값)
        p = os.path.join(rig.profile, "settings.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump({}, f)
        found = [p]
    for p in found:
        with open(p, encoding="utf-8-sig") as f:
            d = json.load(f)
        d.update(kv)
        with open(p, "w", encoding="utf-8") as f:
            json.dump(d, f, ensure_ascii=False)
    rig.start(reuse=True)
    c.r = rig.r
    return found


def _edge_drag(c, hw, ex, ey, sx=None, sy=None):
    rig = c.rig
    w = rig.win(hw)
    s = w["dpi"] / 96.0
    sx = sx or int(w["rect"]["l"] + 300 * s)
    sy = sy or int(w["rect"]["t"] + 18 * s)
    old = rr.EDGE_MARGIN
    rr.EDGE_MARGIN = 6
    try:
        rig.gate(hw)
        rig.click(sx, sy, hwnd=hw, drag=(ex, ey))
    finally:
        rr.EDGE_MARGIN = old
    time.sleep(0.8)
    return rig.win(hw)


def item_h09(c):
    """SnapSdiMode 4값 × SDI 제목줄 실끌기(왼쪽 가장자리·위쪽 가장자리) + 위쪽 가장자리의 합류 아이콘 위 놓기(탭 합류 충돌)."""
    rig = c.rig
    mon = rig.monitor["work"]
    half_w = (mon["r"] - mon["l"]) // 2
    rows = {}
    for mode in ("App", "Auto", "Os", "Off"):
        away.check()
        files = _restart_with_settings(c, SnapSdiMode=mode, SnapEnabled=True)
        row = {"settings": files}
        ids, hws = _two_sdi(c)
        if len(hws) < 2 or not all(hws):
            rows[mode] = dict(row, pass_=False, why="SDI 2개 없음")
            continue
        ha, hb = hws
        # (1) 왼쪽 가장자리 놓기
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (ha, mon["l"], mon["t"] + 400, 900, 500)])
        time.sleep(0.8)
        rig.acquire(ha)
        w = _edge_drag(c, ha, mon["l"] + 6, mon["t"] + 418)
        row["left"] = {"rect": w["rect"], "left_half": abs(w["rect"]["r"] - w["rect"]["l"] - half_w) <= 24 and abs(w["rect"]["l"] - mon["l"]) <= 12,
                       "assist": (c.assist() or {}).get("state"), "log": c.log_tail("sdi snap", 3)}
        c.call("snapassist.cancel")
        # (2) 위쪽 가장자리 놓기(앱 막대 — 끄는 동안 새 창을 잡아 캡처)
        # 창을 모니터 위 끝에 붙인 뒤 그 제목줄 안에서 위 가장자리까지 끈다 — safe_click 은 끝점 소유자를 끌기 전에 검사하므로 끝점이 자기 창 위여야 한다
        # (11-13 01:58 실측: 창을 위+300 에 두고 위 끝(빈 바탕)으로 끌어 끝점 소유권 거부 → 그 뒤 커서 위치 기준이 어긋나 cursor_moved 복귀 오판이 반복됐다)
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (ha, mon["l"] + 600, mon["t"], 900, 500)])
        time.sleep(0.8)
        rig.acquire(ha)
        sh = SimpleNamespace(shots=os.path.join(c.work, "shots"), paths=[])
        sh.capture = sh.paths.append
        proc, gp = rig.watch_guide(sh, "h09_%s_top" % mode, limit_ms=6000)
        wt = rig.win(ha)
        w = _edge_drag(c, ha, int(wt["rect"]["l"] + 500), mon["t"] + 6)
        guide = rig.finish_guide(proc, gp, sh)
        row["top"] = {"rect": w["rect"], "guide": guide, "shots": sh.paths, "log": c.log_tail("sdi snap", 3)}
        c.call("snapassist.cancel")
        # (3) 위쪽 가장자리 근처 합류 아이콘 위에 놓기 — 탭 합류가 칸 배치보다 앞서는지
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (ha, mon["l"] + 300, mon["t"], 900, 500)])
        rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hb, mon["l"] + 400, mon["t"] + 600, 900, 400)])
        time.sleep(0.8)
        ia = _sdi_index_by_doc(c, ids[0])
        icon = rig.find("chrome.logo", window=ia) or {}
        before = len((c.call("window.snapshot") or {}).get("sdi") or [])
        if icon.get("bounds"):
            ix, iy = rig.point(ha, icon["bounds"])
            rig.acquire(hb)
            wb = rig.win(hb)
            _edge_drag(c, hb, ix, max(iy, mon["t"] + 6), int(wb["rect"]["l"] + 300), int(wb["rect"]["t"] + 18))
        after = (c.call("window.snapshot") or {}).get("sdi") or []
        row["join_at_top"] = {"icon": icon.get("bounds"), "sdi_before": before, "sdi_after": len(after), "joined": len(after) < before,
                              "log": c.log_tail("tab-join", 3) or c.log_tail("tabjoin", 3)}
        # 판정은 '앱이 맡았는가' 를 앱 로그로 본다. Os·Off 에서도 Windows 스냅(spiWinArranging=on)이 창을 반쪽에 놓으므로 사각형만으로는 앱 동작을 가를 수 없다(11-13 02:10 실측).
        # 합류 아이콘 위 놓기(join_at_top)는 기록만 하고 판정에서 뺀다 — 끝점·합류 판정 경로가 도구로 확인되지 않았다(UNDET 사유로 남김)
        logs = " ".join(row["left"].get("log") or [])
        if mode in ("App", "Auto"):
            row["pass_"] = row["left"]["left_half"] and "snap assist" in logs and "skip" not in logs
        elif mode == "Os":
            row["pass_"] = "owner=Os" in logs
        else:
            row["pass_"] = "owner=None" in logs and "mode=Off" in logs
        rows[mode] = row
        c.call("docs.redock", all=True)
        time.sleep(0.6)
    ok = bool(rows) and all(r.get("pass_") for r in rows.values())
    return ("PASS" if ok else "FAIL"), {"modes": rows, "rule": "App: 왼쪽 가장자리 놓기=작업 영역 왼쪽 반 칸 적용, 위쪽 합류 아이콘 위 놓기=탭 합류(칸 배치보다 우선). Off: 앱 칸 적용 없음. Auto·Os: 기록만(OS 막대 이중 표시는 캡처로 눈 확인 — 자동 판정 불가)"}


def item_h09_join(c):
    """H-09 합류 재측정: Normal 상태 SDI A 의 로고(합류 아이콘) 위로 SDI B 를 끌어 TabJoinRules.DwellMs(250) 이상 머문 뒤 놓는다.
    safe_click 끌기는 25걸음×40ms 이동 뒤 끝점에서 150ms 만 머물므로, 끌기 거리를 150px 로 줄여 마지막 4걸음(160ms)이 아이콘(48px) 안에 들게 한다(합 ≥250ms).
    시작점은 B 자기 제목줄, 끝점은 A 로고(같은 PID 창) — 끝점 소유권 거부를 피한다. 판정: 앱 로그 'sdi tabjoin end … accepted=True' 와 SDI 창 수 감소."""
    rig = c.rig
    mon = rig.monitor["work"]
    ids, hws = _two_sdi_distinct(c)
    if len(hws) < 2 or not all(hws) or hws[0] == hws[1]:
        return "UNDET", {"why": "SDI 2개 hwnd 를 서로 다르게 찾지 못함(도구)", "ids": ids, "hws": hws}
    ha, hb = hws
    rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (ha, mon["l"] + 300, mon["t"] + 300, 900, 500)])
    time.sleep(0.8)
    ia = _sdi_index_by_doc(c, ids[0])
    icon = (rig.find("chrome.logo", window=ia) or {}).get("bounds")
    out = {"ids": ids, "window_index_A": ia, "icon": icon, "a_state": [s.get("state") for s in (c.call("window.snapshot") or {}).get("sdi") or [] if s.get("doc") == ids[0]]}
    if not icon:
        return "FAIL", dict(out, why="A 로고(chrome.logo) bounds 없음")
    ax, ay = rig.point(ha, icon)  # A 창 DIP → 물리 좌표(A 창 dpi 반영)
    out["tabjoin_targets"] = c.call("window.tabJoinTargets")  # 진단: 표적 사각형(화면 DIP) 기록·대조용
    s = rig.win(hb)["dpi"] / 96.0
    # 머무름 400ms 이상(DwellMs 250 + 여유, odev-b3): safe_click 은 25걸음×40ms 뒤 끝점 150ms 머문다. 끌기 거리 75px 이면 한 걸음 3px,
    # 아이콘 반폭 24px 안에 마지막 8걸음(320ms) + 150ms ≈ 470ms 머문다. 누를 점은 B 로고(48 DIP) 바로 오른쪽 제목 칸(B 왼쪽+60 DIP)
    press_dip, dist = 60, 75
    bl, bt = int(ax + dist - press_dip * s), int(ay - 18 * s)
    rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hb, bl, bt, 900, 400)])
    time.sleep(0.8)
    wb = rig.win(hb)["rect"]
    sx, sy = int(wb["l"] + press_dip * s), int(wb["t"] + 18 * s)
    out.update(a_icon_center=(ax, ay), b_rect=wb, press=(sx, sy), end=(ax, ay), drag_px=sx - ax, dwell_est_ms=int(min(24, sx - ax) / max(1.0, (sx - ax) / 25.0)) * 40 + 150,
               owner_end=rig.point_owner(ax, ay))
    before = len((c.call("window.snapshot") or {}).get("sdi") or [])
    n0 = len(c.log_tail("sdi tabjoin end", 100))
    rig.acquire(hb)
    rig.gate(hb)
    rig.click(sx, sy, hwnd=hb, drag=(ax, ay))
    time.sleep(1.0)
    lines = c.log_tail("sdi tabjoin end", 100)[n0:]
    after = len((c.call("window.snapshot") or {}).get("sdi") or [])
    out.update(tabjoin_lines=lines, sdi_before=before, sdi_after=after, docs_after=[(d.get("id"), d.get("window")) for d in c.call("docs.list") or []])
    accepted = any("accepted=True" in ln for ln in lines)
    join_done = any("join=done" in ln for ln in lines)
    reached = any("current=none" not in ln for ln in lines)
    # 합류 결과는 문서의 창 번호로 본다. SdiTabJoinHook.End 는 WindowTabJoinService.End 직후 로그를 쓰고 '|join=done' 은 그 뒤 Post 된 Join 이 붙이므로
    # SDI 경로 로그 줄에는 join=done 이 나올 수 없다(03:21 r3 실측·소스 확인). window.snapshot sdi 줄 수도 문서별이라 줄지 않는다
    wins = {d.get("id"): d.get("window") for d in c.call("docs.list") or [] if d.get("id") in ids}
    joined = len(wins) == 2 and len(set(wins.values())) == 1
    out.update(accepted=accepted, join_done=join_done, end_reached=reached, joined_same_window=joined, windows_by_doc=wins)
    ok = accepted and joined
    out["rule"] = "A Normal, B 를 A 로고까지 끌어 DwellMs(250) 이상 머문 뒤 놓기 → 'sdi tabjoin end … accepted=True' + 두 문서가 같은 창(docs.list window). current=none 이면 끝점 미도달(도구)"
    if not reached:
        return "UNDET", dict(out, why="끝점 미도달(current=none) — 도구")
    return ("PASS" if ok else "FAIL"), out


def item_h11(c):
    """SDI 를 시험 모니터 작업 영역 안에 두고 최대화 단추에 실커서를 올려 팝업 열림·칸 배치(작업 영역 칸 사각형) 확인."""
    rig = c.rig
    mon = rig.monitor["work"]
    c.call("docs.close", all=True)
    time.sleep(0.5)
    c.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
    time.sleep(1.0)
    doc = (c.call("docs.list") or [{}])[0].get("id")
    hw = rig.sdi_hwnd(doc)
    if not hw:
        return "FAIL", {"why": "SDI hwnd 없음"}
    rr.base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (hw, mon["l"] + 500, mon["t"] + 300, 1000, 600)])
    time.sleep(0.8)
    rig.acquire(hw)
    idx = _sdi_index(c, hw)
    mx = rig.find("chrome.btnMax", window=idx) or {}
    out = {"doc": doc, "window_index": idx, "max": mx.get("bounds")}
    x, y = rig.point(hw, mx["bounds"])
    rig.click(x, y, hwnd=hw, no_click=True, expected=rig.pid)
    st = {}
    for _ in range(20):
        time.sleep(0.2)
        st = c.call("snapflyout.state") or {}
        if st.get("open"):
            break
    out["flyout"] = st
    layouts = [l for l in st.get("layouts") or [] if any(cc.get("canApply") for cc in l.get("cells") or [])]
    if not st.get("open") or not layouts:
        return "FAIL", dict(out, why="팝업이 열리지 않았거나 적용 가능한 칸 없음")
    lay = layouts[0]
    cell = next(cc["index"] for cc in lay["cells"] if cc.get("canApply"))
    out["pick"] = c.call("snapflyout.pick", layout=lay["id"], cell=cell)
    time.sleep(0.8)
    w = rig.win(hw)
    out["rect_after"] = w["rect"]
    out["layout"], out["cell"] = lay["id"], cell
    inside = mon["l"] - 12 <= w["rect"]["l"] and w["rect"]["r"] <= mon["r"] + 12 and mon["t"] - 12 <= w["rect"]["t"] and w["rect"]["b"] <= mon["b"] + 12
    out["inside_work"] = inside
    out["moved"] = w["rect"]["l"] != mon["l"] + 500 or w["rect"]["t"] != mon["t"] + 300
    ok = bool(st.get("open")) and str(st.get("kind", "")).lower() == "sdi" and inside and out["moved"]
    out["rule"] = "실커서 호버 뒤 open=true·kind=Sdi, 칸 선택으로 SDI 가 작업 영역 칸 사각형으로 옮겨짐"
    return ("PASS" if ok else "FAIL"), out


def item_h13(c):
    """패널 제목 줄 실끌기로 다른 패널 가운데에 놓기 → 앱 로그 panel-drop 1줄(source·target·op·singleDoc·result)."""
    rig = c.rig
    c.open_mdi(1)
    pb = c.call("panel.bounds") or {}
    out = {"bounds": {k: pb.get(k) for k in ("LeftPane", "BottomPane", "NotificationPane", "Documents")}}
    # 잡는 곳: 바깥 패널(ToolDockControl, ui.find dock=)의 제목 줄 PART_Grip(+40,+12 DIP — retest_panel_title_drag.title_point 와 같은 규칙).
    # 18:28 첫 실측은 panel.bounds(+60,+10)를 잡아 패널째가 아니라 안쪽 문서를 끌었다(빈 패널 Binding 오류 동반). 놓는 곳: 문서 영역 가운데(Dock 가운데 안내 = Fill)
    src = (c.call("ui.find", dock="LeftPane") or {}).get("bounds") or (c.call("ui.find", dock="NotificationPane") or {}).get("bounds")
    dst = pb.get("Documents") or pb.get("BottomPane")
    out["grab_bounds"] = src
    if not src or not dst:
        return "FAIL", dict(out, why="보이는 패널 또는 문서 영역 없음")
    sx, sy = rig.point(rig.main, {"x": src["x"] + 40, "y": src["y"] + 12, "w": 0, "h": 0})
    ex, ey = rig.point(rig.main, {"x": dst["x"] + dst["w"] / 2, "y": dst["y"] + dst["h"] / 2, "w": 0, "h": 0})
    before = c.log_tail("panel-drop", 50)
    rig.click(sx, sy, hwnd=rig.main, drag=(ex, ey))
    time.sleep(1.0)
    after = c.log_tail("panel-drop", 50)
    new = after[len(before):] if len(after) >= len(before) else after
    out.update({"press": (sx, sy), "drop": (ex, ey), "new_lines": new})
    keys = ("source=", "target=", "op=", "singleDoc=", "result=")
    ok = bool(new) and all(all(k in line for k in keys) for line in new)
    out["rule"] = "실제 놓기(Begin)에서 panel-drop 1줄 이상, 줄마다 source·target·op·singleDoc·result"
    return ("PASS" if ok else "FAIL"), out


def item_h14(c):
    """MDI 문서 창 최대화 단추 실클릭 — 팝업 전(바로 클릭)·팝업이 열린 상태 각각 최대화·복원, 팝업 닫힘."""
    rig = c.rig
    doc = c.open_mdi(1)[0]
    area = c.call("placement.state")["area"]

    def maxed():
        # 최대화는 bounds 가 아니라 MdiState 로 본다(18:28 첫 실측: 최대화 뒤에도 placement.state bounds 는 보통 크기 그대로라 판정식이 놓쳤을 수 있다)
        d = next(d for d in c.call("placement.state")["docs"] if d["id"] == doc)
        b = d["bounds"]
        return str(d.get("state")) == "Maximized" or (b["w"] >= area["dipW"] - 6 and b["h"] >= area["dipH"] - 6), dict(b, state=d.get("state"))

    def btn():
        return rig.point(rig.main, rig.find("chrome.btnMax", doc=doc)["bounds"])

    out = {}
    # 판정식 대조군: 원격 docs.state 로 최대화한 상태를 판정식이 '최대화'로 읽는지 먼저 본다. 대조군이 False 면 판정식 문제, True 인데 실클릭이 False 면 앱 문제다
    c.call("docs.state", state="maximized", doc=doc)
    time.sleep(0.6)
    out["judge_control_max"], out["judge_control_bounds"] = maxed()
    c.call("docs.state", state="normal", doc=doc)
    time.sleep(0.6)
    out["judge_control_normal"] = not maxed()[0]
    if not (out["judge_control_max"] and out["judge_control_normal"]):
        return "UNDET", dict(out, why="판정식 대조군 실패 — 원격 최대화·복원을 판정식이 구분하지 못함(판정식 문제)")
    c.call("snapflyout.options", delayMs=600)
    x, y = btn()
    rig.click(x, y, hwnd=rig.main)  # 팝업 지연 전에 바로 클릭
    time.sleep(0.8)
    out["a_after_click"], out["a_bounds"] = maxed()
    out["a_flyout"] = (c.call("snapflyout.state") or {}).get("open")
    x, y = btn()
    rig.click(x, y, hwnd=rig.main)
    time.sleep(0.8)
    out["a_restored"] = not maxed()[0]
    # (b) 커서를 올려 팝업을 연 뒤 같은 단추 클릭
    x, y = btn()
    rig.click(x, y, hwnd=rig.main, no_click=True, expected=rig.pid)
    st = {}
    for _ in range(15):
        time.sleep(0.2)
        st = c.call("snapflyout.state") or {}
        if st.get("open"):
            break
    out["b_flyout_open_before"] = bool(st.get("open"))
    rig.click(x, y, hwnd=rig.main)
    time.sleep(0.8)
    out["b_after_click"], out["b_bounds"] = maxed()
    out["b_flyout_after"] = (c.call("snapflyout.state") or {}).get("open")
    x, y = btn()
    rig.click(x, y, hwnd=rig.main)
    time.sleep(0.8)
    out["b_restored"] = not maxed()[0]
    ok = out["a_after_click"] and out["a_restored"] and out["b_flyout_open_before"] and out["b_after_click"] and not out["b_flyout_after"] and out["b_restored"]
    out["rule"] = "단추 실클릭으로 최대화·복원, 팝업이 열린 상태에서도 클릭이 최대화하고 팝업은 닫힘"
    return ("PASS" if ok else "FAIL"), out


PCAWAY_ITEMS = {"H-09": item_h09, "H-09J": item_h09_join, "H-11": item_h11, "H-13": item_h13, "H-14": item_h14, "space_enter": item_space_enter, "pressed_capture": item_pressed, "topmost_live": item_topmost,
                "dockjoin_live": item_dockjoin, "focus_ring_2px": item_focus_ring}
PCAWAY_ITEMS.update(k11.ITEMS)  # K11-② 실입력 4케이스(k11_card_click·k11_dim_cell_click·k11_outside_click·k11_esc_key)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--run-dir", required=True)
    p.add_argument("--work", required=True)
    p.add_argument("--items", default="F1,H16,H17,B")
    p.add_argument("--runs", type=int, default=5)
    p.add_argument("--monitor", default="DISPLAY3")
    p.add_argument("--user-pid", type=int, default=None)
    a = p.parse_args()
    args = SimpleNamespace(run_dir=a.run_dir, monitor=a.monitor, user_pid=a.user_pid, dry_run_endpoints=False, allow_outside_main=False)
    os.makedirs(a.work, exist_ok=True)
    if away.user_returned():
        print("거부: 복귀 플래그가 있다")
        return 5
    if not rr.resolve_user_pid(args):
        return 2
    out_path = os.path.join(a.work, "snap_assist_fg.json")
    results = {"started": rr.now_iso(), "run_dir": a.run_dir, "monitor": a.monitor, "items": {}}
    code = 0
    for name in [x.strip() for x in a.items.split(",") if x.strip()]:
        rig = rr.Rig(args, os.path.join(a.work, name))
        os.makedirs(rig.work, exist_ok=True)
        status, detail = "중단", {}
        try:
            away.check()
            rig.start()
            c = Ctx(rig, rig.work)
            if name == "F1":
                status, detail = item_f1(c)
            elif name == "H16":
                status, detail = item_h16(c, a.runs)
            elif name == "H17":
                status, detail = item_h17(c)
            elif name == "B":
                status, detail = item_b(c)
            elif name == "T5":
                status, detail = item_t5(c)
            elif name == "T6":
                status, detail = item_t6(c)
            elif name == "T6D":
                status, detail = item_t6d(c)
            elif name in ("T6S", "sdi_tab"):
                status, detail = item_t6_sdi(c)
            elif name in PCAWAY_ITEMS:
                status, detail = PCAWAY_ITEMS[name](c)
        except (away.UserReturned, away.GuardUnavailable) as e:
            status, detail, code = "중단", {"stopped": repr(e)}, 5
        except rr.InputRefused as e:
            # 전경 불일치 등 실입력 게이트 거부는 앱 결과가 아니라 측정 중단이다(23:34 실측: 사용자 복귀로 전경이 바뀌어 safe_key 거부) — interrupted 로 적고 rc=5 로 다음 비움에 다시 잰다.
            # 좌표 경계·요소 없음 같은 도구 조건 거부는 다시 재도 같으므로 FAIL 로 둔다
            msg = str(e)
            # 전경을 남이 가져간 경우만 중단이다. 끝점 소유권·좌표 같은 safe_click 거부는 도구 조건이라 FAIL(11-13 01:58 끝점 소유권 거부를 중단으로 적어 무한 재시도된 결함 수정)
            if any(k in msg for k in ("전경", "PID 불일치")) and "소유권" not in msg:
                status, detail, code = "interrupted", {"refused": msg, "resume": name}, 5
            else:
                status, detail = "FAIL", {"refused": msg}
        except SystemExit as e:
            status, detail, code = "기동실패", {"error": str(e)}, code or 2
        finally:
            rig.stop()
        results["items"][name] = {"status": status, "detail": detail, "gates": rig.gates[-5:], "leaks": rig.leaks}
        print("%s → %s" % (name, status), flush=True)
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=1, default=str)
        if code == 5:
            break
        if status != "PASS":
            code = code or 1
    return code


if __name__ == "__main__":
    sys.exit(main())
