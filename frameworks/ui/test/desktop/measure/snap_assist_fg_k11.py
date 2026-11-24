# 스냅 배치(Snap Assist) 실입력 4케이스 — 카드 실클릭·흐린 칸 실클릭·바깥 실클릭·Esc 실키를 PC 비움 때 판정한다(K11-②, TODO-654)
"""snap_assist_fg.py 의 PCAWAY_ITEMS 에 등록되는 케이스 모듈(직접 실행하지 않는다).

실입력은 rig.click·rig.key 만 쓴다. 입력마다 rig 가 user_away_guard.check 와 전경 pid·hwnd 게이트를 거치고,
대상은 rig 가 띄운 자기 PID 의 창뿐이다(사용자 Pd 창·포트 5180·5220 은 건드리지 않는다).
판정은 화면이 아니라 원격 상태(snapassist.state·placement.state)로만 한다. 기대 결과는
snap_assist_remote.py 가 snapassist.pick·zone·cancel 로 본 것과 같다(그 모듈의 parse_state·moved 를 재사용).
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import snap_assist_remote as sar  # noqa: E402  원격 대체 시험의 판정 함수를 그대로 쓴다(새 규칙을 만들지 않는다)

CASE_NAMES = {
    "card_click": "k11_card_click",
    "dim_cell_click": "k11_dim_cell_click",
    "outside_click": "k11_outside_click",
    "esc_key": "k11_esc_key",
}


def _fix(s):
    """실행기 Ctx.assist 의 parsed 는 목록 값(filled=[0])을 문자열로 둔다 — 원격 대체 시험의 parse_state 로 state 원문을 다시 파싱해 int·목록으로 바꾼다."""
    s = s or {}
    s["parsed"] = sar.parse_state(s.get("state"))
    return s


def _assist(c):
    return _fix(c.assist())


def _safe(fn):
    """케이스 예외를 FAIL·오류 사유로 돌려준다(InputRefused·UserReturned 등 실행기가 처리하는 중단 예외는 그대로 올린다)."""
    def run(c):
        try:
            return fn(c)
        except Exception as e:  # noqa: BLE001
            if type(e).__name__ in ("InputRefused", "UserReturned", "GuardUnavailable"):
                raise
            return "FAIL", {"error": "%s: %s" % (type(e).__name__, e)}
    run.__name__ = fn.__name__
    run.__doc__ = fn.__doc__
    return run


def _bounds(c):
    return {d.get("id"): d.get("bounds") for d in (c.call("placement.state") or {}).get("docs") or []}


def _open_session(c):
    """F1 과 같은 진입 — MDI 문서 4개, 최대화 단추 팝업으로 quad 0칸 배치, 후보 카드 세션 대기. (문서 id, 세션 상태, 진입 결과)"""
    ids = c.open_mdi(4)
    f = c.flyout_pick(ids[0], "quad", 0)
    s = _fix(c.wait_assist(True))
    return ids, s, f


def _end(c):
    """세션이 남아 있으면 원격으로 닫는다(원격 호출은 실입력이 아니다) — 다음 케이스에 영향이 가지 않게."""
    if _assist(c).get("isActive"):
        c.call("snapassist.cancel")
        time.sleep(0.3)


def _click_id(c, cid):
    """ui.find 로 찾은 요소 중앙을 실클릭한다. 못 찾으면 rig.InputRefused(도구 조건 → FAIL)."""
    return c.rig.click_ctl(c.rig.main, cid=cid)


# 칸 비율(x, y, w, h) — 근거 SnapLayoutCatalog.cs:37-52(칸 번호 순서). 칸 사각형은 SnapAssistSession.cs:140-146 이 ctx.CellTarget 으로 영역(Area)에 이 비율을 곱해 만든다(칸 사이 간격은 중앙 판정에 영향 없음).
_T = 1.0 / 3
_RATIOS = {
    "max": [(0, 0, 1, 1)],
    "half": [(0, 0, .5, 1), (.5, 0, .5, 1)],
    "twothird": [(0, 0, 2 * _T, 1), (2 * _T, 0, _T, 1)],
    "thirds": [(0, 0, _T, 1), (_T, 0, _T, 1), (2 * _T, 0, _T, 1)],
    "leftsplit": [(0, 0, .5, 1), (.5, 0, .5, .5), (.5, .5, .5, .5)],
    "quad": [(0, 0, .5, .5), (.5, 0, .5, .5), (0, .5, .5, .5), (.5, .5, .5, .5)],
}


def _cell_frac(parsed, cell):
    """상태의 layout 으로 칸의 영역 대비 비율(x, y, w, h). 모르는 배치·칸이면 None."""
    r = _RATIOS.get(str(parsed.get("layout")))
    return r[cell] if r and 0 <= cell < len(r) else None


def _cell_center_px(c, parsed, cell):
    """칸 중앙의 화면 px. 흐린 칸은 SnapAssistLayer.cs:25 주석대로 그림만 있고 자동화 요소가 없어 ID 로 못 찾으므로 영역·배치로 계산한다."""
    f = _cell_frac(parsed, cell)
    if f is None:
        return None
    a = c.call("placement.state")["area"]
    return int(a["x"] + a["w"] * (f[0] + f[2] / 2)), int(a["y"] + a["h"] * (f[1] + f[3] / 2))


def _zone_rects(c, parsed, cells):
    """주어진 칸들의 DIP 사각형(영역 기준) 목록 — _outside_point 의 avoid 와 같은 좌표계."""
    a = c.call("placement.state")["area"]
    dw, dh = a["dipW"], a["h"] / (a["w"] / a["dipW"])
    out = []
    for i in cells:
        f = _cell_frac(parsed, i)
        if f:
            out.append({"x": dw * f[0], "y": dh * f[1], "w": dw * f[2], "h": dh * f[3]})
    return out


def _outside_point(c, avoid):
    """카드·흐린 칸 밖의 MDI 영역 점(화면 px). 첫 칸(채운 칸, 좌상단 4분면) 중앙을 기본으로 하고 avoid 사각형(화면 px 환산 전 DIP)과 겹치면 다른 4분면을 고른다."""
    a = c.call("placement.state")["area"]
    s = a["w"] / a["dipW"]
    for fx, fy in ((0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75)):
        dx, dy = a["dipW"] * fx, (a["h"] / s) * fy
        hit = any(b and b["x"] <= dx <= b["x"] + b["w"] and b["y"] <= dy <= b["y"] + b["h"] for b in avoid)
        if not hit:
            return int(a["x"] + a["w"] * fx), int(a["y"] + a["h"] * fy), (fx, fy)
    return None


def _avoid_rects(c, parsed):
    """회피 대상 = 보이는 카드 + 아직 안 채운 모든 칸(활성·흐린 칸, 계산 사각형). 채운 칸 누름은 덮개 규칙상 바깥(outside)이라 회피하지 않는다(SnapAssistLayer.Input.cs:100-104)."""
    out = []
    for i in range(8):
        n = c.rig.find("snap.assist.card.%d" % i) or {}
        if n.get("effectivelyVisible") and n.get("bounds"):
            out.append(n["bounds"])
    filled = parsed.get("filled") or []
    out += _zone_rects(c, parsed, [i for i in range(parsed.get("cells") or 0) if i not in filled])
    return out


@_safe
def item_card_click(c):
    """카드 0번 실클릭 → 그 창 1개만 활성 칸으로 움직이고 칸이 채워지며 활성 칸이 다음으로 간다(snapassist.pick 판정과 같음)."""
    ids, s0, f = _open_session(c)
    out = {"flyout": f, "start": s0.get("state"), "expect": "카드 실클릭 → 창 1개만 활성 칸에 놓이고 filled 에 그 칸 추가·활성 칸 이동"}
    if not s0.get("isActive"):
        return "FAIL", dict(out, why="세션이 뜨지 않음")
    cell = s0["parsed"].get("activeCell")
    b0 = _bounds(c)
    try:
        out["click"] = _click_id(c, "snap.assist.card.0")
        time.sleep(0.6)
        s1 = _assist(c)
        mv = sar.moved(b0, _bounds(c), [ids[0]])
        p = s1["parsed"]
        out.update({"after": s1.get("state"), "moved": mv, "cell": cell})
        ok = len(mv) == 1 and cell in (p.get("filled") or []) and p.get("activeCell") != cell
        return ("PASS" if ok else "FAIL"), out
    finally:
        _end(c)


@_safe
def item_dim_cell_click(c):
    """흐린 칸 실클릭 → 활성 칸이 그 칸으로 바뀌고 배치는 그대로(snapassist.zone 판정과 같음)."""
    ids, s0, f = _open_session(c)
    out = {"flyout": f, "start": s0.get("state"), "expect": "흐린 칸 실클릭 → activeCell 이 그 칸으로, 문서 bounds 무변화"}
    if not s0.get("isActive"):
        return "FAIL", dict(out, why="세션이 뜨지 않음")
    p0 = s0["parsed"]
    filled, active = p0.get("filled") or [], p0.get("activeCell")
    target = next((i for i in range(p0.get("cells") or 0) if i not in filled and i != active), None)
    if target is None:
        return "FAIL", dict(out, why="활성 칸 말고 비어 있는 흐린 칸이 없음")
    b0 = _bounds(c)
    try:
        out["target"] = target
        pt = _cell_center_px(c, p0, target)
        if pt is None:
            return "FAIL", dict(out, why="배치 %r 의 칸 %d 비율을 모름" % (p0.get("layout"), target))
        # 계산 지점이 보이는 카드(활성 칸 안)에 들어가면 흐린 칸 누름이 아니라 카드 누름이 된다 → 시험 조건 불성립
        a = c.call("placement.state")["area"]
        s = a["w"] / a["dipW"]
        dx, dy = (pt[0] - a["x"]) / s, (pt[1] - a["y"]) / s
        cards = [n["bounds"] for n in (c.rig.find("snap.assist.card.%d" % i) or {} for i in range(8))
                 if n.get("effectivelyVisible") and n.get("bounds")]
        if any(b["x"] <= dx <= b["x"] + b["w"] and b["y"] <= dy <= b["y"] + b["h"] for b in cards):
            return "FAIL", dict(out, why="계산한 흐린 칸 중앙이 카드·활성 칸 안에 있음", point=pt)
        out["click"] = {"x": pt[0], "y": pt[1]}
        c.rig.click(pt[0], pt[1], hwnd=c.rig.main)
        time.sleep(0.5)
        s1 = _assist(c)
        mv = sar.moved(b0, _bounds(c))
        out.update({"after": s1.get("state"), "moved": mv})
        ok = s1.get("isActive") and s1["parsed"].get("activeCell") == target and not mv
        return ("PASS" if ok else "FAIL"), out
    finally:
        _end(c)


@_safe
def item_outside_click(c):
    """카드·흐린 칸 밖 실클릭 → 세션 끝(lastEnd=outside)·배치 무변화(되돌림 없음)."""
    ids, s0, f = _open_session(c)
    out = {"flyout": f, "start": s0.get("state"), "expect": "바깥 실클릭 → 세션 종료(lastEnd=outside)·문서 bounds 무변화"}
    if not s0.get("isActive"):
        return "FAIL", dict(out, why="세션이 뜨지 않음")
    pt = _outside_point(c, _avoid_rects(c, s0["parsed"]))
    if pt is None:
        return "FAIL", dict(out, why="카드·칸을 피한 바깥 점을 못 찾음")
    b0 = _bounds(c)
    try:
        c.rig.click(pt[0], pt[1], hwnd=c.rig.main)
        out["point"] = {"x": pt[0], "y": pt[1], "frac": pt[2]}
        end = _fix(c.wait_assist(False, tries=10))
        mv = sar.moved(b0, _bounds(c))
        out.update({"end": end.get("state"), "lastEnd": end.get("lastEnd"), "moved": mv})
        ok = not end.get("isActive") and end.get("lastEnd") == "outside" and not mv
        return ("PASS" if ok else "FAIL"), out
    finally:
        _end(c)


@_safe
def item_esc_key(c):
    """Esc 실키 → 세션 끝(lastEnd=esc)·후보 표시 닫힘·배치 무변화(snapassist.cancel 판정과 같음)."""
    ids, s0, f = _open_session(c)
    out = {"flyout": f, "start": s0.get("state"), "expect": "Esc 실키 → 세션 종료(lastEnd=esc)·후보 표시 닫힘·문서 bounds 무변화"}
    if not s0.get("isActive"):
        return "FAIL", dict(out, why="세션이 뜨지 않음")
    b0 = _bounds(c)
    try:
        opened = c.rig.find("snap.assist") or {}  # 열림 대조 — 같은 ID 가 열린 세션에서 찾히고 보여야 닫힘 판정이 의미가 있다
        if not (opened.get("effectivelyVisible") and opened.get("bounds")):
            return "FAIL", dict(out, why="열림 상태에서 덮개 미검출", overlay_open=opened)
        out["overlay_open"] = True
        c.rig.key("Esc", hwnd=c.rig.main)
        end = _fix(c.wait_assist(False, tries=10))
        shown = bool((c.rig.find("snap.assist") or {}).get("effectivelyVisible"))
        mv = sar.moved(b0, _bounds(c))
        out.update({"end": end.get("state"), "lastEnd": end.get("lastEnd"), "overlay_visible": shown, "moved": mv})
        ok = not end.get("isActive") and end.get("lastEnd") == "esc" and not shown and not mv
        return ("PASS" if ok else "FAIL"), out
    finally:
        _end(c)


ITEMS = {
    CASE_NAMES["card_click"]: item_card_click,
    CASE_NAMES["dim_cell_click"]: item_dim_cell_click,
    CASE_NAMES["outside_click"]: item_outside_click,
    CASE_NAMES["esc_key"]: item_esc_key,
}
