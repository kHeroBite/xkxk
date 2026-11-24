#!/usr/bin/env python3
# 웹 Snap Assist(11 문서 §3.9, 계획 §7) 측정·판정 — headless Chrome 실마우스로 MDI·관리형 창을 끌기 막대 칸·모서리에 놓아 후보 카드 표시 → 카드 클릭 → 다음 칸 → Esc·바깥 클릭 끝, 후보 0·옵션 끔 미표시를 동작 기준으로 본다
"""사용: python3 -B w24_snap_assist.py --bundle <진단판 wwwroot> --out <결과 폴더(커밋 폴더 밖)> [--port 5827] | --self-test
w23_tab_join_web.py 와 같은 구조다 — web/measure/ui28_web.Web(127.0.0.1 정적 서버 + Playwright headless Chromium, 사용자 PC 입력·화면과 무관)을 그대로 쓴다.
실마우스로 막대 칸을 못 찾으면 모서리 끌기로, 끌기 진입이 안 되면 진단 명령(snapflyout.hover/pick·snapassist.*)으로 판정하고
결과 JSON 의 paths 에 'L0' 를 남긴다(실포인터 실패 사실 기록). 시간(ms) 판정은 하지 않는다. 종료 코드 0=통과, 1=실패, 3=실행 거부.
썸네일은 snapassist.state 의 thumb 필드·snapassist.thumbdump(웹은 저장 없이 줄만)·카드 칸 좌표(snap.assist.card.N.thumb·.label)로,
웹 폴백(대체 카드)은 snapassist.thumbfail reason=error(진단 강제 실패)로, 위치 고정 해제 뒤 끌기는 chrome.lock + 실마우스 제목줄 끌기로 본다.
결과: <out>/w24_snap_assist.json 과 캡처(png). --out 은 세션 work 폴더 등 커밋 대상 밖이어야 한다."""
import argparse
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "common"))  # D-TESTDIR: test/common/_paths.py → web/measure 가 경로에 붙는다
import _paths  # noqa: E402
_paths.add()

CHECKS, PATHS = [], {}
HEAD = 16  # 제목줄 가운데(제목줄 32)
USER_PORTS = (5180, 5220)
TOL = 0.5


def check(name, ok, detail=None):
    CHECKS.append({"name": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False, default=str)[:300], flush=True)


# 순수 함수(자가시험 대상) ------------------------------------------------------
def parse_state(text):
    """SnapAssistSession.State 1줄을 사전으로 바꾼다(desktop/snap_assist_remote.py 와 같은 규칙)."""
    out = {}
    for k, v in re.findall(r"(\w+)=(\[[^\]]*\]|\S*)", text or ""):
        if v.startswith("["):
            out[k] = [int(x) for x in v[1:-1].split(",") if x.strip().lstrip("-").isdigit()]
        elif v in ("True", "False"):
            out[k] = v == "True"
        elif v.lstrip("-").isdigit():
            out[k] = int(v)
        else:
            out[k] = v
    return out


def same_rect(a, b, tol=TOL):
    return bool(a and b) and all(abs(a[k] - b[k]) <= tol for k in ("x", "y", "w", "h"))


def moved(before, after, ignore=()):
    return [d for d in after if d not in ignore and not same_rect(before.get(d), after.get(d))]


def center(r, dx=0, dy=0):
    return (r["x"] + r["w"] / 2 + dx, r["y"] + r["h"] / 2 + dy)


def shift(r, ox, oy):
    return {"x": r["x"] + ox, "y": r["y"] + oy, "w": r["w"], "h": r["h"]}


def outcome_cell(text):
    """MdiSnapDrag/ManagedSnapDrag.LastOutcome('…|zone=BarCell|layout=quad|cell=1|…') → (zone, layout, cell)."""
    m = dict(re.findall(r"(zone|layout|cell)=([^|]*)", text or ""))
    cell = m.get("cell")
    return m.get("zone"), (None if m.get("layout") in (None, "-") else m.get("layout")), (int(cell) if cell and cell.lstrip("-").isdigit() else None)


def scan_points(bar, cols=12, rows=2):
    """막대 사각형 안 오른쪽부터 왼쪽으로 훑을 점(칸 종류가 여럿이면 뒤쪽에 칸이 많은 종류가 온다는 가정 — 어느 칸이든 max 가 아니면 받는다)."""
    return [(bar["x"] + bar["w"] * (c + 0.5) / cols, bar["y"] + bar["h"] * (r + 0.5) / rows) for c in reversed(range(cols)) for r in range(rows)]


# 앱 조회 ----------------------------------------------------------------------
def assist(w):
    s = w.res("snapassist.state") or {}
    s["parsed"] = parse_state(s.get("state"))
    return s


def wait_assist(w, active, tries=15):
    s = {}
    for _ in range(tries):
        s = assist(w)
        if bool(s.get("isActive")) == active:
            break
        time.sleep(0.2)
    return s


def pstate(w):
    return w.res("placement.state") or {}


def area(w):
    return pstate(w).get("area") or {"x": 0, "y": 0}


def mdi_bounds(w):
    return {d.get("id"): d.get("bounds") for d in pstate(w).get("docs") or []}


def mdi_page(w, doc):
    a, b = area(w), mdi_bounds(w).get(doc)
    return shift(b, a.get("x", 0), a.get("y", 0)) if b else None


def find(w, ctl):
    return (w.res("ui.find", id=ctl) or {}).get("bounds")


def open_mdi(w, n):
    w.res("snapassist.cancel")
    w.res("docs.close", all=True)
    for _ in range(n):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    ids = [d["id"] for d in w.res("docs.list") or []]
    for i, d in enumerate(ids):  # 겹치지 않게 펼친다(제목줄 누름점이 다른 창에 가리지 않게)
        w.res("placement.markPlaced", doc=d, x=200 + i * 70, y=150 + i * 60, w=300, h=220)
    time.sleep(0.8)
    return ids


def press_drag(w, start, path, probe=None):
    """누름 → path 점들을 차례로 지나며 이동(점마다 probe(점) 가 참이면 그 자리에서 놓음) → 놓기. 놓은 점을 돌려준다."""
    m = w.page.mouse
    m.move(*start)
    m.down()
    last, hit = start, None
    for pt in path:
        for i in range(1, 9):
            m.move(last[0] + (pt[0] - last[0]) * i / 8, last[1] + (pt[1] - last[1]) * i / 8)
            time.sleep(0.015)
        last = pt
        time.sleep(0.12)
        if probe and probe(pt):
            hit = pt
            break
    m.up()
    time.sleep(1.0)
    return hit


def enter_bar(w, doc):
    """실마우스로 문서 창 제목줄을 MDI 영역 위쪽 가운데로 끌어 막대를 띄우고, 막대 안을 훑어 BarCell 판정 자리에서 놓는다."""
    r, a = mdi_page(w, doc), area(w)
    if not r:
        return None
    start = (r["x"] + 60, r["y"] + HEAD)
    top = (a["x"] + a["w"] / 2, a["y"] + 6)
    seen = {"bar": None}

    def probe(pt):
        if seen["bar"] is None:
            seen["bar"] = find(w, "snap.bar")
            if seen["bar"]:
                return False
        live = (w.res("snapcell.lastdrag") or {}).get("zone")
        return live == "BarCell" and pt != top

    def path():
        yield top
        for _ in range(3):  # 막대가 뜰 때까지 위쪽에서 잠시 떨기
            yield (top[0] + 1, top[1])
            if seen["bar"]:
                break
        for pt in scan_points(seen["bar"]) if seen["bar"] else []:
            yield pt

    hit = press_drag(w, start, path(), probe)
    zone, layout, cell = outcome_cell((w.res("snapcell.lastdrag") or {}).get("lastOutcome"))
    return {"bar": seen["bar"], "hit": hit, "zone": zone, "layout": layout, "cell": cell}


def enter_corner(w, doc):
    """실마우스로 제목줄을 MDI 영역 왼쪽 아래 모서리로 끌어 놓는다(모서리 → quad, SnapAssistOnEdge 기본 켬)."""
    r, a = mdi_page(w, doc), area(w)
    if not r:
        return None
    press_drag(w, (r["x"] + 60, r["y"] + HEAD), [(a["x"] + a["w"] / 3, a["y"] + a["h"] - 40), (a["x"] + 3, a["y"] + a["h"] - 3)])
    zone, layout, cell = outcome_cell((w.res("snapcell.lastdrag") or {}).get("lastOutcome"))
    return {"zone": zone, "layout": layout, "cell": cell}


def enter_l0(w, doc, layout="quad", cell=0, kind="mdi"):
    """진단 명령 진입(팝업 원격 — SnapFlyoutHover.Pick 과 같은 경로)."""
    st = (w.res("snapflyout.hover", kind=kind, doc=doc) or {}).get("state") or {}
    for _ in range(25):
        if st.get("open") or st.get("skip"):
            break
        time.sleep(0.2)
        st = w.res("snapflyout.state") or {}
    if not st.get("open"):
        w.res("snapflyout.leave")
        return {"open": False, "state": st}
    res = w.res("snapflyout.pick", layout=layout, cell=cell) or {}
    time.sleep(0.8)
    return {"open": True, "done": res.get("done")}


def start_session(w, doc, item):
    """막대 실마우스 → 모서리 실마우스 → L0 순서로 세션을 연다. 쓴 경로를 PATHS[item] 에 남긴다."""
    e = enter_bar(w, doc)
    s = wait_assist(w, True)
    if s.get("isActive"):
        PATHS[item] = "real-bar"
        return s, e
    w.res("snapassist.cancel")
    e2 = enter_corner(w, doc)
    s = wait_assist(w, True)
    if s.get("isActive"):
        PATHS[item] = "real-corner"
        return s, {"bar": e, "corner": e2}
    PATHS[item] = "L0"
    print("  ⚠️ 실마우스 진입 실패 → L0 snapflyout 진입 (%s)" % item, flush=True)
    e3 = enter_l0(w, doc)
    return wait_assist(w, True), {"bar": e, "corner": e2, "l0": e3}


def click_card(w, index=0):
    b = find(w, "snap.assist.card.%d" % index)
    if not b:
        return None
    w.page.mouse.click(*center(b))
    time.sleep(0.8)
    return b


def set_assist_option(w, on):
    w.res("sys.open", view="Settings")
    time.sleep(0.8)
    w.res("settings.window", category="Window")
    time.sleep(0.6)
    w.res("ui.toggle", id="settings.chkSnapAssist", on=on)
    time.sleep(0.3)
    w.res("ui.click", id="settings.btnClose")
    time.sleep(0.5)
    return (assist(w).get("options") or {}).get("enabled")


# 시나리오 ---------------------------------------------------------------------
def mdi_flow(w):
    ids = open_mdi(w, 4)
    s, how = start_session(w, ids[0], "show")
    p = s["parsed"]
    shot = w.shot("w24_show")
    check("S1 칸에 놓기 → 세션 표시(Mdi·남은 칸 ≥1·후보 ≥1)", s.get("isActive") and p.get("scope") == "Mdi" and (p.get("candidates") or 0) >= 1,
          {"path": PATHS.get("show"), "enter": how, "state": s.get("state")})
    card0 = find(w, "snap.assist.card.0")
    check("S1 후보 카드 표시(snap.assist.card.0)", bool(card0), {"card0": card0, "zone": find(w, "snap.assist.zone.%s" % p.get("activeCell")), "shot": bool(shot)})
    if not (s.get("isActive") and card0):
        return
    layout, cell1, placed = p.get("layout"), p.get("activeCell"), [ids[0]]
    b0 = mdi_bounds(w)
    click_card(w, 0)
    s2 = assist(w)
    p2 = s2["parsed"]
    mv = moved(b0, mdi_bounds(w), placed)
    PATHS["card"] = "real"
    check("S2 카드 실클릭 → 그 창 1개 배치·활성 칸이 다음 칸으로(또는 남은 칸 없음으로 끝)",
          len(mv) == 1 and (cell1 in (p2.get("filled") or []) or not s2.get("isActive")),
          {"moved": mv, "before": s.get("state"), "after": s2.get("state")})
    picked = mv[0] if len(mv) == 1 else None
    if s2.get("isActive"):
        b1 = mdi_bounds(w)
        w.page.keyboard.press("Escape")
        time.sleep(0.6)
        e = assist(w)
        check("S3 Esc → 세션 끝(lastEnd=esc)·고른 창·나머지 창 그대로", not e.get("isActive") and e.get("lastEnd") == "esc" and not moved(b1, mdi_bounds(w)),
              {"end": e.get("state"), "lastEnd": e.get("lastEnd"), "moved": moved(b1, mdi_bounds(w))})
    if picked and layout and cell1 is not None:
        before = mdi_bounds(w).get(picked)
        w.res("snapcell.apply", kind="mdi", doc=picked, layout=layout, cell=cell1)
        time.sleep(0.5)
        after = mdi_bounds(w).get(picked)
        check("S2 고른 창이 그 칸 사각형에 놓임(같은 칸 재적용 bounds 불변 ±%.1f)" % TOL, same_rect(before, after),
              {"doc": picked, "layout": layout, "cell": cell1, "before": before, "after": after})


def outside_click(w):
    ids = open_mdi(w, 4)
    s, _ = start_session(w, ids[0], "outside")
    if not s.get("isActive"):
        check("S4 세션 표시", False, s.get("state"))
        return
    placed = mdi_page(w, ids[0])
    pt = center(placed) if placed else (20, 20)  # 배치한 창 안 = 카드·흐린 칸 밖
    b = mdi_bounds(w)
    w.page.mouse.click(*pt)
    time.sleep(0.6)
    e = assist(w)
    check("S4 바깥(배치한 창) 클릭 → 세션 끝(lastEnd=outside)·배치 되돌림 없음", not e.get("isActive") and e.get("lastEnd") == "outside" and not moved(b, mdi_bounds(w)),
          {"click": pt, "end": e.get("state"), "lastEnd": e.get("lastEnd")})


def zone_switch(w):
    ids = open_mdi(w, 4)
    s, _ = start_session(w, ids[0], "zone")
    p = s["parsed"]
    if not s.get("isActive") or (p.get("cells") or 0) < 3:
        check("S5 남은 칸 2개 이상 세션", False, s.get("state"))
        w.res("snapassist.cancel")
        return
    other = next((c for c in range(p["cells"]) if c not in (p.get("filled") or []) and c != p.get("activeCell")), None)
    z = w.res("snapassist.zone", cell=other) or {}
    PATHS["zone"] = "L0"  # 흐린 칸은 Render 로 그려 AutomationId 가 없다 — 칸 바꾸기는 진단 명령으로 본다
    check("S5 칸 바꾸기(snapassist.zone) → 활성 칸 이동·그 칸 컨테이너(snap.assist.zone.N) 표시",
          z.get("done") and parse_state(z.get("state")).get("activeCell") == other and bool(find(w, "snap.assist.zone.%d" % other)), z.get("state"))
    w.page.keyboard.press("Escape")
    time.sleep(0.5)


def flyout_entry(w):
    ids = open_mdi(w, 3)
    st = (w.res("snapflyout.hover", kind="mdi", doc=ids[0]) or {}).get("state") or {}
    for _ in range(25):
        if st.get("open") or st.get("skip"):
            break
        time.sleep(0.2)
        st = w.res("snapflyout.state") or {}
    cellb = find(w, "snap.flyout.half.0")
    PATHS["flyout"] = "L0-hover+real-click"  # 호버 지연 시작은 진단 명령, 칸 누름은 실마우스
    if cellb:
        w.page.mouse.click(*center(cellb))
        time.sleep(1.0)
    s = wait_assist(w, True)
    check("S6 최대화 단추 팝업 half 칸 클릭 → 세션 표시(layout=half·활성 1)", s.get("isActive") and s["parsed"].get("layout") == "half" and s["parsed"].get("activeCell") == 1,
          {"popupOpen": st.get("open"), "cell": cellb, "state": s.get("state")})
    w.res("snapassist.cancel")


def no_candidates(w):
    ids = open_mdi(w, 1)
    r = enter_corner(w, ids[0])
    s = wait_assist(w, False, tries=5)
    check("S7 후보 0개(문서 1개) → 세션 미표시(skip=no-candidates)·카드 없음",
          not s.get("isActive") and s.get("lastSkipReason") == "no-candidates" and not find(w, "snap.assist"), {"enter": r, "state": s.get("state")})


def disabled(w):
    ids = open_mdi(w, 3)
    eff = set_assist_option(w, False)
    try:
        r = enter_corner(w, ids[0])
        s = wait_assist(w, False, tries=5)
        check("S8 SnapAssistEnabled=false → 배치만·세션 미표시(skip=disabled)", eff is False and not s.get("isActive") and s.get("lastSkipReason") == "disabled"
              and not find(w, "snap.assist"), {"enabled": eff, "enter": r, "state": s.get("state")})
    finally:
        back = set_assist_option(w, True)
        check("S8 옵션 원복(켬)", back is True, back)


def managed_flow(w):
    """관리형 창(웹 SDI) — 제목줄을 모서리로 끌어 Managed 세션 → 카드 실클릭."""
    w.res("snapassist.cancel")
    w.res("docs.close", all=True)
    for _ in range(3):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    ids = [d["id"] for d in w.res("docs.list") or []]
    for d in ids:
        w.res("docs.sdi", doc=d, on=True)
        time.sleep(1.0)
    wins = [x for x in w.res("windows.list") or [] if not x.get("main")]
    src = next((x["bounds"] for x in wins if ids and ids[0] in (x.get("docs") or [])), None)
    if not src:
        check("S9 관리형 창 3개 준비", False, wins)
        return
    vw, vh = w.viewport
    press_drag(w, (src["x"] + 200, src["y"] + HEAD), [(vw / 3, vh - 60), (3, vh - 3)])
    s = wait_assist(w, True)
    PATHS["managed"] = "real-corner"
    if not s.get("isActive"):
        PATHS["managed"] = "L0"
        enter_l0(w, ids[0], kind="sdi")
        s = wait_assist(w, True)
    check("S9 관리형 창 모서리 놓기 → 세션 표시(scope=Managed)", s.get("isActive") and s["parsed"].get("scope") == "Managed",
          {"path": PATHS["managed"], "outcome": (w.res("snapcell.lastdrag") or {}).get("managed"), "state": s.get("state")})
    if s.get("isActive"):
        w.shot("w24_managed")
        c = click_card(w, 0)
        s2 = assist(w)
        check("S9 관리형 카드 실클릭 → 칸 채움 또는 끝", bool(c) and (len(s2["parsed"].get("filled") or []) >= 2 or not s2.get("isActive")), s2.get("state"))
        w.res("snapassist.cancel")
    w.res("docs.redock", all=True)
    time.sleep(1)


def thumb_statuses(text):
    """상태 1줄 thumb 필드('thumb=[0:bitmap,1:fallback:error]'·'thumb=off') → 후보 순서 결과 목록('off' 는 문자열, 없으면 None)."""
    m = re.search(r"\bthumb=(\[[^\]]*\]|\S+)", text or "")
    if not m:
        return None
    v = m.group(1)
    if not v.startswith("["):
        return v
    return [item.split(":", 1)[1] if ":" in item else item for item in v[1:-1].split(",") if item]


def full_cover(line, cover=0.9):
    """thumbdump 1줄에서 원래 크기 비트맵의 불투명 범위가 가로·세로 cover 이상인가(창 전체가 담겼는가)."""
    f = re.search(r"\bfull=(\d+)x(\d+)", line or "")
    c = re.search(r"\bfullContent=\d+,\d+,(\d+)x(\d+)", line or "")
    return bool(f and c) and int(c.group(1)) >= int(f.group(1)) * cover and int(c.group(2)) >= int(f.group(2)) * cover


def card_geom(w, i=0):
    return find(w, "snap.assist.card.%d.thumb" % i), find(w, "snap.assist.card.%d.label" % i)


def thumbs(w):
    """웹 썸네일: 찍히면 thumb=bitmap 이고 창 전체가 담겨야 한다. 웹 렌더가 비어 실패하면 fallback:<사유>(대체 카드)로 기록한다 — 둘 다 이름표는 칸 아래."""
    ids = open_mdi(w, 3)
    s, how = start_session(w, ids[0], "thumbs")
    sts = thumb_statuses(s.get("state"))
    w.shot("w24_thumbs")
    ok = s.get("isActive") and isinstance(sts, list) and sts and all(x == "bitmap" or x.startswith("fallback:") for x in sts)
    dump = (w.res("snapassist.thumbdump") or {}).get("lines") or []
    bitmap_ok = all(full_cover(dump[i]) for i, x in enumerate(sts or []) if x == "bitmap" and i < len(dump))
    check("T1 웹 후보 썸네일 결과 = bitmap(창 전체 담김) 또는 fallback:<사유>",
          ok and bitmap_ok, {"path": PATHS.get("thumbs"), "state": s.get("state"), "thumbs": s.get("thumbs"), "dump": dump})
    th, lb = card_geom(w)
    check("T2 웹 이름표가 썸네일 칸 아래", bool(th and lb) and lb["y"] >= th["y"] + th["h"] - TOL, {"thumb": th, "label": lb})
    w.res("snapassist.cancel")


def thumb_fallback(w):
    """웹 폴백 경로: 진단 강제 실패(reason=error) → 첫 후보 fallback:error, 나머지는 같은 세션 안 web-fallback. 대체 카드 실클릭으로 고르기 동작."""
    w.res("snapassist.thumbfail", reason="error")
    try:
        ids = open_mdi(w, 3)
        s, _ = start_session(w, ids[0], "fallback")
        sts = thumb_statuses(s.get("state"))
        w.shot("w24_fallback")
        check("F1 웹 강제 실패 → thumb=[0:fallback:error, 나머지 fallback:web-fallback]",
              s.get("isActive") and isinstance(sts, list) and len(sts) >= 2 and sts[0] == "fallback:error"
              and all(x == "fallback:web-fallback" for x in sts[1:]), s.get("state"))
        th, lb = card_geom(w)
        note = w.res("ui.find", id="snap.assist.card.0.note", props="Text") or {}
        text = ((note.get("props") or {}).get("Text") or "").strip()
        check("F2 웹 대체 카드(그림 칸 + 아래 이름표 + 보조 표기 글자)", bool(th and lb) and lb["y"] >= th["y"] + th["h"] - TOL and bool(text),
              {"thumb": th, "label": lb, "note": text})
        if s.get("isActive"):
            b0 = mdi_bounds(w)
            c = click_card(w, 0)
            check("F3 웹 대체 카드 실클릭 → 그 창 배치", bool(c) and len(moved(b0, mdi_bounds(w), [ids[0]])) == 1, {"card": c})
        w.res("snapassist.cancel")
    finally:
        off = w.res("snapassist.thumbfail", reason="") or {}
        check("F4 진단 강제 실패 해제", off.get("thumbFail") is None, off)


def drag_title(w, r, dx=120, dy=80):
    """제목줄(왼쪽 60, 위 16) 실마우스 끌기."""
    start = (r["x"] + 60, r["y"] + HEAD)
    press_drag(w, start, [(start[0] + dx / 2, start[1] + dy / 2), (start[0] + dx, start[1] + dy)])


def can_drag(w, doc):
    """chrome.state 의 canDrag(없으면 None) — 기록만 하고 판정하지 않는다(odev-1 요청).
    주의: 이 값은 문서 모델 CanDrag(d.CanDrag)이다. 위치 고정은 창 쪽 IsDragEnabled(로컬 값)로 끌기를 막으므로
    고정 중에도 True 가 정상이다(B 수정 뒤 모델에 false 가 역기록되지 않음). 고정 여부는 locked, 실제 끌기 가능은 bounds 변화로 본다."""
    return (w.res("chrome.state", doc=doc) or {}).get("canDrag")


def managed_bounds(w, doc):
    return next((x.get("bounds") for x in w.res("windows.list") or [] if not x.get("main") and doc in (x.get("docs") or [])), None)


def lock_unlock_drag(w):
    """위치 고정 켬 → 끌기 막힘 → 끔 → 바로(<500ms) 끌기 이동 → 2초 뒤 끌기 이동. MDI(K1~K3) + 관리형 창(K4~K6). odev-1 과 합의한 절차(AC-104).
    해제 직후 끌기가 실패해도 2초 뒤 끌기까지 진행해 결과를 남긴다. 판정은 bounds 변화만(시간 판정 없음)."""
    ids = open_mdi(w, 2)
    doc = ids[0]
    lk = w.res("chrome.lock", doc=doc) or {}
    cd_locked = can_drag(w, doc)
    b0 = mdi_bounds(w).get(doc)
    drag_title(w, mdi_page(w, doc))
    b1 = mdi_bounds(w).get(doc)
    check("K1 웹 MDI 위치 고정 중 끌기 → 그대로", lk.get("locked") is True and same_rect(b0, b1),
          {"lock": lk.get("locked"), "canDrag": cd_locked, "before": b0, "after": b1})
    un = w.res("chrome.lock", doc=doc) or {}
    cd_unlocked = can_drag(w, doc)
    drag_title(w, mdi_page(w, doc))  # 해제 직후(대기 없음)
    b2 = mdi_bounds(w).get(doc)
    check("K2 웹 MDI 고정 해제 직후 끌기 → 이동", un.get("locked") is False and not same_rect(b1, b2),
          {"lock": un.get("locked"), "canDrag": cd_unlocked, "before": b1, "after": b2})
    time.sleep(2.0)
    drag_title(w, mdi_page(w, doc), dx=-100, dy=-60)
    b3 = mdi_bounds(w).get(doc)
    check("K3 웹 MDI 고정 해제 2초 뒤 끌기 → 이동", not same_rect(b2, b3), {"canDrag": can_drag(w, doc), "before": b2, "after": b3})

    # 관리형 창(웹 SDI — ManagedSnapDrag 경로)
    w.res("docs.sdi", doc=doc, on=True)
    time.sleep(1.0)
    m0 = managed_bounds(w, doc)
    if not m0:
        check("K4 웹 관리형 창 준비", False, w.res("windows.list"))
        w.res("docs.redock", all=True)
        return
    lk = w.res("chrome.lock", doc=doc) or {}
    drag_title(w, m0)
    m1 = managed_bounds(w, doc)
    check("K4 웹 관리형 창 고정 중 끌기 → 그대로", lk.get("locked") is True and same_rect(m0, m1),
          {"lock": lk.get("locked"), "canDrag": can_drag(w, doc), "before": m0, "after": m1})
    un = w.res("chrome.lock", doc=doc) or {}
    drag_title(w, m1)
    m2 = managed_bounds(w, doc)
    check("K5 웹 관리형 창 고정 해제 직후 끌기 → 이동", un.get("locked") is False and not same_rect(m1, m2),
          {"lock": un.get("locked"), "canDrag": can_drag(w, doc), "before": m1, "after": m2})
    time.sleep(2.0)
    drag_title(w, m2, dx=-100, dy=-60)
    m3 = managed_bounds(w, doc)
    check("K6 웹 관리형 창 고정 해제 2초 뒤 끌기 → 이동", not same_rect(m2, m3), {"before": m2, "after": m3})
    w.res("docs.redock", all=True)
    time.sleep(1.0)


def hidden_end(w):
    """탭 숨김(visibilitychange hidden) → 세션 끝(lastEnd=focus). 헤드리스라 visibilityState 를 덮어쓰고 같은 이벤트를 보낸다."""
    ids = open_mdi(w, 4)
    s, _ = start_session(w, ids[0], "hidden")
    if not s.get("isActive"):
        check("S12 세션 표시(탭 숨김 시험 준비)", False, s.get("state"))
        return
    w.page.evaluate("() => { Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' }); document.dispatchEvent(new Event('visibilitychange')); }")
    time.sleep(0.6)
    e = assist(w)
    check("S12 탭 숨김(hidden) → 세션 끝(lastEnd=focus)", not e.get("isActive") and e.get("lastEnd") == "focus", {"end": e.get("state"), "lastEnd": e.get("lastEnd")})
    w.page.evaluate("() => { delete document.visibilityState; document.dispatchEvent(new Event('visibilitychange')); }")


def _bleed_lib():
    """글자 비침 계산은 데스크톱 원격 시험(desktop/snap_assist_remote.py)의 순수 함수를 그대로 쓴다(같은 판정 규칙, 11 문서 §3.9)."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("snap_assist_remote", os.path.join(HERE, "..", "desktop", "snap_assist_remote.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


BLEED = {}
UI_MODE = None
UI_SET = {}


def make_dpr_web(base, dpr):
    """--dpr: headless 에서 기기 배율(예 Fold5 Override density 360 → 2.25)을 흉내 낸다. 뷰포트는 --viewport 로 준다."""
    class DprWeb(base):
        def open(self, p):
            orig = p.chromium.launch

            def launch(*a, **k):
                b = orig(*a, **k)
                np_ = b.new_page
                b.new_page = lambda **kw: np_(device_scale_factor=dpr, **kw)
                return b
            p.chromium.launch = launch
            try:
                super().open(p)
            finally:
                p.chromium.launch = orig
    return DprWeb


def make_cdp_web(base, cdp, port, out):
    """--cdp: adb forward 로 노출한 기기(Fold5) Chrome devtools 에 붙는 Web. 번들은 PC 정적 서버 + 기기에서 `adb reverse tcp:<port> tcp:<port>` 로 연다(호출자 몫).
    새 탭만 열고 닫으며 사용자 탭·Chrome 설정은 건드리지 않는다. 뷰포트는 기기 그대로(변경 안 함)."""
    class CdpWeb(base):
        capture = "raw CDP Page.captureScreenshot (device Chrome %s)" % cdp

        def _tidy_chrome(self):
            """연결이 멈추면 기기 Chrome 에 about:blank 를 새로 열어 상태를 가볍게 한다(앱 com.pd.poc 는 건드리지 않는다). adb 는 환경변수 ADB, 기기는 연결된 첫 장치."""
            import subprocess
            adb = os.environ.get("ADB")
            if not adb:
                return
            out = subprocess.run([adb, "devices"], capture_output=True, timeout=30).stdout.decode("utf-8", "replace").replace("\r", "")
            ser = [ln.split()[0] for ln in out.splitlines()[1:] if ln.strip().endswith("device")]
            if ser:
                subprocess.run([adb, "-s", ser[0], "shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", "about:blank", "-n", "com.android.chrome/com.google.android.apps.chrome.Main"], capture_output=True, timeout=30)
            time.sleep(3)

        def _diag(self, err):
            """goto 실패 근거: Chrome 탭 목록(URL)과 기기 adb reverse 상태."""
            import subprocess
            info = {"error": str(err)[:120], "stale_closed": getattr(self, "stale_closed", None), "tabs": []}
            try:
                info["tabs"] = [pg.url[:80] for pg in self.browser.contexts[0].pages]
            except Exception as e:  # noqa: BLE001
                info["tabs_error"] = str(e)[:80]
            adb = os.environ.get("ADB")
            if adb:
                try:
                    info["reverse"] = subprocess.run([adb, "reverse", "--list"], capture_output=True, timeout=30).stdout.decode("utf-8", "replace").replace("\r", "").strip()
                except Exception as e:  # noqa: BLE001
                    info["reverse_error"] = str(e)[:80]
            return info

        def open(self, p):
            # 연결이 60초 안에 안 되면 Chrome 을 정리하고 1회 재시도, 그래도 안 되면 connect_timeout(측정 무효)
            try:
                self.browser = p.chromium.connect_over_cdp(cdp, timeout=60000)
            except Exception:  # noqa: BLE001
                self._tidy_chrome()
                try:
                    self.browser = p.chromium.connect_over_cdp(cdp, timeout=60000)
                except Exception as e2:  # noqa: BLE001
                    raise RuntimeError("connect_timeout: " + str(e2)[:150])
            ctx = self.browser.contexts[0]
            origin = "http://127.0.0.1:%d" % self.port
            stale = [pg for pg in ctx.pages if pg.url.startswith(origin)]  # 앞선 실패 실행이 남긴 같은 출처 탭(단일 인스턴스 가드·메모리 점유 의심)을 먼저 닫는다
            for pg in stale:
                try:
                    pg.close()
                except Exception:  # noqa: BLE001
                    pass
            self.stale_closed = len(stale)
            self.page = ctx.new_page()
            self.page.on("console", lambda m: self.errors.append(m.text[:300]) if m.type == "error" else None)
            self.page.on("pageerror", lambda e: self.errors.append("pageerror: " + str(e)[:300]))
            for attempt in (1, 2):  # domcontentloaded 30초, 실패 시 1회 재시도, 그래도 안 되면 goto_timeout(측정 무효, 탭 목록·reverse 상태를 근거로 남긴다)
                try:
                    self.page.goto(origin + "/?fresh", wait_until="domcontentloaded", timeout=30000)
                    break
                except Exception as e:  # noqa: BLE001
                    if attempt == 2:
                        raise RuntimeError("goto_timeout: " + json.dumps(self._diag(e), ensure_ascii=False, default=str)[:900])
            self.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
            for _ in range(60):
                if self.call("status").get("ok"):
                    break
                time.sleep(1)

        def shot(self, name):
            """Playwright page.screenshot 은 기기 Chrome 에서 화면 크기 변경을 일으켜 Snap Assist 세션이 lastEnd=resize 로 끝났다(2026-11-13 실측). 원시 CDP Page.captureScreenshot 으로 찍는다."""
            import base64
            import io as _io
            from PIL import Image as _Image
            if getattr(self, "_cs", None) is None:
                self._cs = self.page.context.new_cdp_session(self.page)
            r = self._cs.send("Page.captureScreenshot", {"format": "png", "fromSurface": True})
            data = base64.b64decode(r["data"])
            with open(os.path.join(self.shots, name + ".png"), "wb") as f:
                f.write(data)
            return _Image.open(_io.BytesIO(data)).convert("RGB")

        def close(self):
            try:
                if getattr(self, "page", None):
                    self.page.close()  # 우리가 연 탭만 닫는다(browser.close 는 기기 Chrome 을 끌 수 있어 부르지 않는다)
            finally:
                self.httpd.shutdown()
    return CdpWeb


def bleed(w):
    """글자 비침: 뒤에 그리드 2개를 MDI 영역 전체(max 칸)에 깔고 half 0칸 배치 → 남은 칸 판·카드 패딩·틈 화소가 기대 표면색과 거리 > 8 인 비율 ≤ 0.001(테마 3종)."""
    lib = _bleed_lib()
    if UI_MODE:
        UI_SET.update(w.call("ui.style", set=UI_MODE.lower()))  # 좁은 폭(펼친 폴드 805dp)은 자동이 DOCK·NAV 라 Snap Assist 가 해당 없음 — PC형 강제로 MDI 를 만든다(11 문서: 같은 기기 PC형 강제에서는 떴다)
        time.sleep(1.5)
    ids = open_mdi(w, 3)
    for d in ids[1:]:
        w.res("snapcell.apply", kind="mdi", doc=d, layout="max", cell=0)
        time.sleep(0.5)
    for theme in BLEED_THEMES:
        w.res("theme.select", id=theme)
        time.sleep(1.0)
        base_img = w.shot("w24_bleed_base_%s" % theme)  # 오버레이 전 기준 캡처 — 그려졌는지 확인용
        for _ in range(8):  # 테마 전환 뒤 기기 Chrome 이 다시 그리는 중이면 기준이 흔들린다 — 연속 두 캡처가 같아질 때까지 기다린다
            time.sleep(0.8)
            nxt = w.shot("w24_bleed_base_%s" % theme)
            from PIL import ImageChops as _IC
            moved = sum(1 for v in _IC.difference(base_img, nxt).convert("L").point(lambda v: 255 if v > 8 else 0).getdata() if v)
            base_img = nxt
            if moved < 0.002 * base_img.width * base_img.height:
                break
        l0 = enter_l0(w, ids[0], "half", 0)
        s = wait_assist(w, True)
        time.sleep(0.8)
        res = {"theme": theme, "state": s.get("state"), "l0": l0, "ui_mode": w.res("ui.style"), "ui_set": dict(UI_SET), "docs": len(ids)}  # 진입 실패 진단용(팝업 열림 여부·UI 형·문서 수)
        try:
            picker = find(w, "snap.assist.zone.%s" % s["parsed"].get("activeCell"))
            cards = []
            for i in range(16):
                c = find(w, "snap.assist.card.%d" % i)
                if not c:
                    break
                th, lb = card_geom(w, i)
                cards.append({"card": c, "thumb": th, "label": lb})
            surface = lib.parse_color(((w.res("ui.find", id="snap.assist.card.0", props="Background") or {}).get("props") or {}).get("Background"))
            shot = os.path.join(w.shots, "w24_bleed_%s.png" % theme)
            img = w.shot("w24_bleed_%s" % theme)
            inner = w.page.viewport_size["width"] if w.page.viewport_size else w.page.evaluate("window.innerWidth")  # 원격 Chrome(--cdp)은 viewport_size 가 없어 innerWidth(CSS px)로 배율을 구한다
            scale = img.width / inner
            res.update({"capture": getattr(w, "capture", "headless playwright page.screenshot"), "shot": shot, "picker": picker, "cards": len(cards), "surface": "%02X%02X%02X" % surface if surface else None, "scale": round(scale, 3)})
            if picker and cards and surface:
                box = (int(picker["x"] * scale), int(picker["y"] * scale), int((picker["x"] + picker["w"]) * scale), int((picker["y"] + picker["h"]) * scale))
                from PIL import ImageChops

                def painted_of(im):
                    dm = ImageChops.difference(base_img.crop(box), im.crop(box)).convert("L").point(lambda v: 255 if v > 8 else 0)
                    return sum(1 for v in dm.getdata() if v) / max(1, dm.width * dm.height)
                painted, t_poll = painted_of(img), time.time()
                while painted < 0.05 and time.time() - t_poll < 10:  # 기기 Chrome 은 오버레이를 늦게 그린다 — 그려질 때까지 1초 간격으로 최대 10초 다시 캡처한다
                    time.sleep(1.0)
                    img = w.shot("w24_bleed_%s" % theme)
                    painted = painted_of(img)
                res["overlay_changed"] = round(painted, 3)
                res["overlay_wait_s"] = round(time.time() - t_poll, 1)
                res["state_after"] = (w.res("snapassist.state") or {}).get("state")  # 캡처 뒤에도 세션이 살아 있는지(중간에 취소됐는지) 남긴다
                if painted < 0.05:  # 상태는 active 인데 화면에 오버레이가 안 그려진 캡처 — 비침 비율이 의미 없으므로 측정 무효로 남긴다(기기 Chrome 에서 실측)
                    res["error"] = "overlay_not_painted(기준 캡처 대비 변화 %.1f%%)" % (painted * 100)
                else:
                    res.update(lib.bleed_measure(img, scale, picker, cards, surface))
        except Exception as e:  # noqa: BLE001 — 측정 실패는 FAIL 로 남긴다
            res["error"] = str(e)[:200]
        BLEED[theme] = res
        check("B1 웹 글자 비침 비율 ≤ 0.001(%s, 거리 > 8)" % theme, res.get("ratio") is not None and res["ratio"] <= 0.001,
              {k: res.get(k) for k in ("ratio", "bad", "total", "by_area", "corner", "bbox", "surface", "scale", "shot", "error")})
        w.res("snapassist.cancel")
        time.sleep(0.5)
    w.res("theme.select", id="pd-light")


BLEED_THEMES = ("pd-light", "pd-dark", "pd-contrast")
STEPS = (mdi_flow, outside_click, zone_switch, flyout_entry, no_candidates, disabled, managed_flow, thumbs, thumb_fallback, lock_unlock_drag, hidden_end, bleed)


def self_test():
    B0 = type("B0", (), {"open": lambda s, p: 0})
    cases = [
        ("상태 1줄 해석", parse_state("active=True scope=Managed layout=quad cells=4 filled=[2] skipped=[] activeCell=0 candidates=3 thumb=off lastEnd=")
         == {"active": True, "scope": "Managed", "layout": "quad", "cells": 4, "filled": [2], "skipped": [], "activeCell": 0, "candidates": 3, "thumb": "off", "lastEnd": ""}),
        ("끝 판정 해석", parse_state("active=False lastEnd=outside lastSkip=") == {"active": False, "lastEnd": "outside", "lastSkip": ""}),
        ("놓기 결과 해석", outcome_cell("drop:source=d1|zone=BarCell|layout=quad|cell=1|target=0,0,1,1|cancelled=False") == ("BarCell", "quad", 1)),
        ("놓기 결과 max 없음", outcome_cell("drop:source=d1|zone=Corner|layout=-|cell=-1|target=x") == ("Corner", None, -1)),
        ("놓기 결과 빈 값", outcome_cell(None) == (None, None, None)),
        ("막대 훑기 점 수·범위", len(scan_points({"x": 10, "y": 0, "w": 120, "h": 20})) == 24
         and all(10 <= x <= 130 and 0 <= y <= 20 for x, y in scan_points({"x": 10, "y": 0, "w": 120, "h": 20}))),
        ("움직인 창", moved({"a": {"x": 0, "y": 0, "w": 1, "h": 1}}, {"a": {"x": 2, "y": 0, "w": 1, "h": 1}}) == ["a"]),
        ("shift·center", center(shift({"x": 0, "y": 0, "w": 20, "h": 20}, 5, 7)) == (15, 17)),
        ("thumb 필드 해석", thumb_statuses("active=True thumb=[0:fallback:error,1:fallback:web-fallback] lastEnd=") == ["fallback:error", "fallback:web-fallback"]
         and thumb_statuses("thumb=off") == "off" and thumb_statuses("active=False") is None),
        ("thumbdump 창 전체 판정", full_cover("0 full=320x240 fullContent=0,0,320x240 thumb=160x120")
         and not full_cover("0 full=320x240 fullContent=0,0,80x60") and not full_cover(None)),
        ("--cdp 원격 Web 클래스(상속·캡처 방식 표기)", (lambda c: c.__name__ == "CdpWeb" and "device Chrome http://x" in c.capture and c.open is not B0.open)(make_cdp_web(B0, "http://x", 1, "/tmp"))),
        ("글자 비침 판정(데스크톱 공용 함수 자가시험)", _bleed_lib().bleed_self_test() == []),
    ]
    for n, ok in cases:
        print(("PASS " if ok else "FAIL ") + n)
    return 0 if all(ok for _, ok in cases) else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bundle")
    ap.add_argument("--out", help="결과 폴더(세션 work 폴더 등 — poc/ui/test 아래 금지)")
    ap.add_argument("--port", type=int, default=5827)
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--steps", help="쉼표로 고른 묶음만(예 bleed — 글자 비침 대조군·현행 비교). 생략하면 전부")
    ap.add_argument("--cdp", help="기기 Chrome devtools 주소(예 http://127.0.0.1:9222, adb forward). 주면 headless 대신 그 기기 Chrome 에서 돈다(Fold5 실기 증거). 기기에서 `adb reverse tcp:<--port> tcp:<--port>` 필요")
    ap.add_argument("--viewport", help="headless 뷰포트 WxH(CSS px, 기본 1400x900). 예 Fold5 펼침 805x820")
    ap.add_argument("--dpr", type=float, help="headless 기기 배율(예 2.25)")
    ap.add_argument("--ui-mode", choices=["Pc", "Tablet", "Mobile", "Auto"], help="bleed 단계 전에 UI 형을 강제한다(예 Fold5 펼침 805dp 는 Pc). 결과 bleed.<테마>.ui_mode 에 남는다")
    a = ap.parse_args()
    global UI_MODE
    UI_MODE = a.ui_mode
    if a.self_test:
        return self_test()
    if not (a.bundle and a.out):
        ap.error("--bundle 과 --out 이 필요하다")
    out = os.path.abspath(a.out)
    if "/poc/ui/test/" in out.replace("\\", "/") + "/" or "logs/ui28" in out.replace("\\", "/"):
        print("거부: 결과는 커밋 폴더(poc/ui/test)·logs/ui28 밖에 쓴다: %s" % out)
        return 3
    if a.port in USER_PORTS:
        print("거부: 사용자 포트 %d" % a.port)
        return 3
    from playwright.sync_api import sync_playwright
    from ui28_web import Web
    os.makedirs(out, exist_ok=True)
    errors = ["브라우저 열기 실패"]
    with sync_playwright() as p:
        vw, vh = (int(x) for x in a.viewport.lower().split("x")) if a.viewport else (1400, 900)
        base = make_dpr_web(Web, a.dpr) if a.dpr else Web
        w = (make_cdp_web(Web, a.cdp, a.port, out) if a.cdp else base)(a.bundle, a.port, out, viewport=(vw, vh))
        try:
            try:
                w.open(p)
            except RuntimeError as e:
                if not str(e).startswith(("connect_timeout", "goto_timeout")):
                    raise
                for th in BLEED_THEMES:  # 연결 실패는 측정 무효로 남기고 넘어간다(FAIL 아님)
                    BLEED[th] = {"theme": th, "error": str(e)}
                check("A17 기기 Chrome 연결·페이지 열기(connect_timeout·goto_timeout, 측정 무효)", False, str(e))
                w = None
            if w is not None and not a.cdp:
                w.page.set_viewport_size({"width": vw, "height": vh})
            time.sleep(2)
            picked = set(x.strip() for x in (a.steps or "").split(",") if x.strip())
            for step in [s for s in STEPS if (not picked or s.__name__ in picked) and w is not None]:
                try:
                    step(w)
                except Exception as e:  # noqa: BLE001 — 한 묶음이 죽어도 나머지 항목은 본다
                    check("예외 " + step.__name__, False, str(e)[:200])
                    w.res("snapassist.cancel")
            errors = w.errors[:20] if w is not None else []
        finally:
            if w is not None:
                w.close()
    check("X 콘솔 오류 0건", not errors, errors)
    with open(os.path.join(out, "w24_snap_assist.json"), "w", encoding="utf-8") as f:
        json.dump({"checks": CHECKS, "paths": PATHS, "bleed": BLEED}, f, ensure_ascii=False, indent=1, default=str)
    failed = [c for c in CHECKS if not c["pass"]]
    print("결과 %d/%d  경로 %s" % (len(CHECKS) - len(failed), len(CHECKS), PATHS))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
