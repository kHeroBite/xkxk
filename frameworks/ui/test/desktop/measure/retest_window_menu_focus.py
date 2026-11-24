# 레이아웃 템플릿 '집중'(tpl.focus) 시험 — 탐색기·로그 숨김, 빈 패널 자리 회수, 해제, 저장·불러오기 유지(실행기는 retest_window_menu.py)
import datetime
import os
import subprocess

from retest_window_menu_resize import try_resize  # (라) 분할선 끌기 판정(분리 모듈, 300줄 상한)

# 템플릿 5종(LayoutTemplates.cs). tpl.focus 는 탐색기·로그·알림 패널을 모두 숨긴다.
TEMPLATE_IDS = sorted(["tpl.mdi-cascade", "tpl.mdi-tile-v", "tpl.mdi-tile-h", "tpl.tabbed", "tpl.focus"])
HIDDEN = {"explorer": False, "log": False, "notify": False}
SHOWN = {"explorer": True, "log": True, "notify": True}
SLACK = 8  # 분할선·여백 허용 오차(DIP)


def panel_state(ctx):
    """panel.list 로 탐색기·로그·알림 보임 여부를 본다. 숨긴 도구는 HiddenDockables 로 빠지므로 패널 탭 목록에 없다."""
    lst = ctx.call("panel.list")
    if not lst:  # 응답 실패를 '전부 숨김'으로 읽으면 거짓 판정이 된다(i0136 01:59: 집중 상태 panel.list 예외 → explorer·log·notify 모두 False 로 오판)
        raise RuntimeError("panel.list 응답 없음(원격 오류) — %s" % ((ctx.r.calls[-1] if ctx.r.calls else {}).get("error") or "")[:160])
    ps = {p.get("id"): p for p in lst}
    tabs = lambda pid: [t.get("id") for t in (ps.get(pid) or {}).get("tabs") or []]
    # i0215 부터 집중 숨김(SetSidePanelsHidden)은 측면 패널을 통째로 접고 탭 목록은 그대로 둔다 → 탭만 보면 '보임'으로 오판(02:29 실측).
    # 측면 패널 visible 필드(Panels.cs 02:1x 이후 판)가 있으면 그것과 함께 본다. 없는 옛 판은 탭 기준 그대로.
    shown = lambda pid, tool: tool in tabs(pid) and (ps.get(pid) or {}).get("visible", True) is not False
    return {"explorer": shown("LeftPane", "Explorer"), "log": shown("BottomPane", "Log"),
            "notify": (ps.get("NotificationPane") or {}).get("visible") is True}


def shot_path(ctx, label):
    """
    캡처 저장 위치(lead 01:5x): 데스크톱은 앱이 처음부터 logs/odev6r/shots/ 에 쓰게 path= 를 준다(사용자 폴더 → 옮기기 불필요).
    Android·웹은 앱이 그 경로를 쓸 수 없어 기본 위치(앱 저장소·inline) 그대로 둔다.
    """
    if ctx.target != "desktop":
        return {}
    d = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs", "odev6r", "shots")
    os.makedirs(d, exist_ok=True)
    name = "%s_%s.png" % (label, datetime.datetime.now().strftime("%H%M%S"))
    win = subprocess.run(["wslpath", "-w", os.path.abspath(d)], capture_output=True, text=True).stdout.strip()
    return {"path": win + "\\" + name} if win else {}


def _nodes(tree):
    for n in tree:
        yield n
        yield from _nodes(n.get("children") or [])


def _has_notify(n):
    return any(m.get("id") == "notify.root" for m in _nodes([n]))


def geometry(ctx):
    """메인 창 전체 트리(named=false)에서 문서 영역(가장 큰 DocumentDockControl)과 알림 외 도구 패널(ToolDockControl) 사각형을 뽑는다."""
    dump = ctx.call("tree.get", named=False, depth=400) or {}
    nodes = list(_nodes(dump.get("tree") or []))
    rect = lambda n: n.get("bounds") or {}
    area = lambda b: (b.get("w") or 0) * (b.get("h") or 0)
    docs = [rect(n) for n in nodes if n.get("type") == "DocumentDockControl" and n.get("visible")]
    tools = [rect(n) for n in nodes if n.get("type") == "ToolDockControl" and n.get("visible") and not _has_notify(n)]
    splits = [rect(n) for n in nodes if n.get("type") in ("ProportionalStackPanelSplitter", "GridSplitter") and n.get("visible")
              and (rect(n).get("w") or 0) > 0 and (rect(n).get("h") or 0) > 0]
    # 도구 핀(자동 숨김) 상태: 가장자리 핀 막대의 항목(ToolPinItemControl) 안 글자
    pins = sorted(m.get("text") for n in nodes if n.get("type") == "ToolPinItemControl" and n.get("visible")
                  for m in _nodes([n]) if m.get("text"))
    # 핀 막대 항목별 글자·위치(odev-1 01:5x: ToolPinnedControl=가장자리별 ItemsControl, 항목마다 ToolPinItemControl — 왼쪽 판정용)
    pin_items = [{"text": next((m.get("text") for m in _nodes([n]) if m.get("text")), None), "x": rect(n).get("x"), "y": rect(n).get("y")}
                 for n in nodes if n.get("type") == "ToolPinItemControl" and n.get("visible")]
    # 트리 식별 실측용(lead 00:5x): 판정에 쓰는 Dock 컨트롤 타입이 실제로 몇 개 있는가(0 이면 그 판정의 가정이 틀린 것)
    kinds = ("DocumentDockControl", "ToolDockControl", "ToolChromeControl", "ToolPinItemControl", "ToolPinnedControl",
             "PinnedDockControl", "ProportionalStackPanelSplitter", "GridSplitter")
    types = {k: sum(1 for n in nodes if n.get("type") == k) for k in kinds}
    return {"doc": max(docs, key=area) if docs else None, "tools": tools, "splitters": splits, "pins": pins, "pinItems": pin_items,
            "types": types, "nodes": dump.get("nodes"), "truncated": dump.get("truncated")}


def _pane_rects(g):
    """문서 영역(Documents)·그 왼쪽 패널(LeftPane)·그 아래 패널(BottomPane) 사각형. 없으면 None."""
    d = g["doc"]
    if not d:
        return {"Documents": None, "LeftPane": None, "BottomPane": None}
    big = lambda ts: max(ts, key=lambda t: t["w"] * t["h"]) if ts else None
    live = [t for t in g["tools"] if t["w"] > 1 and t["h"] > 1]
    return {"Documents": d,
            "LeftPane": big([t for t in live if t["x"] + t["w"] <= d["x"] + SLACK]),
            "BottomPane": big([t for t in live if t["y"] >= d["y"] + d["h"] - SLACK])}


def _same_rect(a, b, tol=2):
    return a is not None and b is not None and all(abs((a.get(k) or 0) - (b.get(k) or 0)) <= tol for k in ("x", "y", "w", "h"))


def _panel_snapshot(ctx):
    """panel.list 중 되돌림 대조 대상: 패널 Id·쪽·탭 순서·활성 탭(제목은 언어에 따라 바뀌므로 뺀다)."""
    out = []
    for p in ctx.call("panel.list") or []:
        tabs = p.get("tabs") or []
        out.append({"id": p.get("id"), "side": p.get("side"), "tabs": [t.get("id") for t in tabs],
                    "active": [t.get("id") for t in tabs if t.get("active")], "visible": p.get("visible")})
    return out


def _side_sizes(g):
    """문서 영역 왼쪽에 붙은 패널 너비, 아래에 붙은 패널 높이(최댓값)."""
    d = g["doc"]
    left = [t["w"] for t in g["tools"] if t["x"] + t["w"] <= d["x"] + SLACK and t["w"] > 1 and t["h"] > 1]
    below = [t["h"] for t in g["tools"] if t["y"] >= d["y"] + d["h"] - SLACK and t["w"] > 1 and t["h"] > 1]
    return max(left or [0]), max(below or [0])


def it_focus_apply(ctx):
    """
    ② 집중 템플릿 적용 → 탐색기·로그 숨김, 알림 보임.
    + 빈 패널 판정(리드 지시): 알림 외 도구 패널이 남아 있으면 너비·높이가 0(접힘)이어야 하고,
      문서 영역은 같은 탭 배치(tpl.tabbed)일 때보다 왼쪽 패널 너비·아래 패널 높이만큼 넓어져야 한다. 캡처 1장을 남긴다.
    """
    ctx.call("layout.template.apply", id="tpl.tabbed")
    g0 = geometry(ctx)
    if not g0["doc"]:
        return False, {"reason": "문서 영역(DocumentDockControl) 식별 실패", "truncated": g0["truncated"]}
    left0, below0 = _side_sizes(g0)
    ok = ctx.call("layout.template.apply", id="tpl.focus") is True
    st = panel_state(ctx)
    g1 = geometry(ctx)
    shot = ctx.call("screenshot", window=0, **shot_path(ctx, "focus_124")) or {}
    occupied = [t for t in g1["tools"] if t["w"] > 1 and t["h"] > 1]
    d0, d1 = g0["doc"], g1["doc"] or {}
    grew_w = (d1.get("w") or 0) - d0["w"]
    grew_h = (d1.get("h") or 0) - d0["h"]
    found = left0 > 0 and below0 > 0  # 기준 상태에서 왼쪽·아래 패널을 찾았어야 판정이 성립한다
    reclaimed = grew_w >= left0 - SLACK and grew_h >= below0 - SLACK
    detail = {"panels": st, "emptyPanes": occupied, "before": {"doc": d0, "leftW": left0, "bottomH": below0},
              "after": {"doc": d1, "grewW": grew_w, "grewH": grew_h}, "shot": shot.get("path") or shot.get("bytes"),
              "treeTypes": {"tabbed": g0["types"], "focus": g1["types"], "nodes": (g0["nodes"], g1["nodes"])}}
    if not found:
        detail["reason"] = "tpl.tabbed 상태에서 왼쪽·아래 도구 패널 식별 실패(트리 구조 확인 필요)"
    return ok and st == HIDDEN and found and not occupied and reclaimed and (shot.get("bytes") or 0) > 0, detail


def it_focus_leave(ctx):
    """
    ③ 집중 상태에서 다른 템플릿 적용 → 탐색기·로그가 다시 보이고, 숨기기 전과 같아야 한다(odev-1 요청).
    (가) 숨기기 전 panel.list(Id·쪽·탭 순서·활성 탭)와 LeftPane·BottomPane·Documents 사각형·핀·분할선 기록
    (나) 집중 적용 뒤 탐색기·로그 숨김, 알림 그대로
    (다) 해제 뒤 panel.list 동일, 사각형 ±2 이내, 핀 동일
    (라) 분할선이 되돌아왔는가(개수·존재). 원격 끌기 명령이 없어 실제 끌기 크기 조절은 판정하지 않는다(detail 에 명시).
    알려진 결함(odev-1 SetSidePanelsHidden 저장 전 판본): 되돌리면 탐색기가 오른쪽으로 가고 분할선이 사라짐 → 이 항목 FAIL 이 정상.
    """
    ctx.call("layout.template.apply", id="tpl.mdi-cascade")
    snap0, g0 = _panel_snapshot(ctx), geometry(ctx)
    r0 = _pane_rects(g0)
    ctx.call("layout.template.apply", id="tpl.focus")
    hid = panel_state(ctx)
    g_mid = geometry(ctx)
    ok = ctx.call("layout.template.apply", id="tpl.mdi-cascade") is True
    after = panel_state(ctx)
    snap1, g1 = _panel_snapshot(ctx), geometry(ctx)
    r1 = _pane_rects(g1)
    found = all(r0.values())  # 기준 상태에서 세 사각형을 다 찾아야 대조가 성립한다
    rect_ok = {k: _same_rect(r0[k], r1[k]) for k in r0}
    checks = {
        "가_기준식별": found,
        "나_숨김": hid == HIDDEN,
        "다_보임": after == SHOWN,
        "다_panelList동일": snap1 == snap0,
        "다_사각형±2": all(rect_ok.values()),
        "라_분할선복귀": len(g1["splitters"]) == len(g0["splitters"]) and len(g0["splitters"]) > 0,
    }
    # 핀(odev-1 00:4x): 핀 대상은 안쪽 탭 영역(LeftPane.Tabs·BottomPane.Tabs, 제목=panel.explorer·panel.log).
    # 핀 막대 항목이 ToolPinItemControl 로 그려진다는 것은 실측 전 추정 → 기준 0건이면 0=0 우연 PASS 를 막으려고 판정에서 뺀다.
    # 집중 적용 중 핀은 풀리고(막대 항목 사라짐) 해제 뒤 다시 걸리는 것이 정상이다.
    if g0["pins"]:
        checks["나_핀풀림"] = g_mid["pins"] == []
        checks["다_핀동일"] = g1["pins"] == g0["pins"]
        pin_note = "대조함(기준 %d건)" % len(g0["pins"])
    else:
        pin_note = "핀 미대조(기준 0건) — 해당 없음: 핀 없는 회차, 핀 판정은 #127(선핀 회차)로 커버"
    drag = try_resize(ctx)
    # 판정 불능(panel.bounds·panel.resize 없음)도 checks 에 남긴다 — 빠뜨리면 그 항목 없이 #125 가 PASS 될 수 있었다(otest-1 주의 b, lead 03:3x).
    checks["라_분할선끌기(원격대리)"] = drag["ok"] if drag.get("judged") else UNDET
    detail = {"checks": checks, "rects": {"before": r0, "after": r1, "same": rect_ok},
              "treeTypes": {"before": g0["types"], "focus": g_mid["types"], "after": g1["types"]},
              "panelList": {"before": snap0, "after": snap1} if snap1 != snap0 else "same",
              # 기준 기록 직전 핀 상태(odev-1 ③): 핀이면 바깥 LeftPane 이 비어 보여 사각형 식별이 흔들릴 수 있다
              "pins": {"note": pin_note, "baselinePinned": g0["pins"], "focus": g_mid["pins"], "after": g1["pins"]},
              "splitters": {"before": len(g0["splitters"]), "after": len(g1["splitters"])},
              "drag": drag}
    return verdict(ok, checks), detail


UNDET = "UNDET"  # 판정 불능(필요한 원격 명령이 없는 판 등) — PASS 로 세지 않는다


def verdict(ok, checks):
    """checks 의 값으로 항목 판정: False 가 하나라도 있으면 False(FAIL), 그 밖에 UNDET 가 있으면 'UNDET'(미판정 — PASS 아님), 모두 True 면 True."""
    vals = list(checks.values())
    if not ok or any(v is False for v in vals):
        return False
    return UNDET if any(v == UNDET for v in vals) else all(v is True for v in vals)


PIN_TEXTS = ("탐색기", "Explorer")  # LeftPane 안쪽 탭 영역 제목(panel.explorer) — 언어별


def it_focus_pinned(ctx):
    """
    ③ 핀 회차(odev-1 01:5x 판정 제안): ① LeftPane 을 먼저 핀 ② 기준: 핀 막대 항목 1건·'탐색기'·왼쪽·panel.list pinned=true
    ③ 집중 중 0건(숨길 때 핀을 먼저 푼다) ④ 해제 뒤 다시 1건·같은 글자·왼쪽·pinned=true ⑤ 끝나면 핀 원복.
    사각형(±2)은 핀 없는 회차(#125)와 나눠 이 회차 값으로 따로 적는다. 핀 상태면 LeftPane 바깥 ToolDock 이 비어 접힐 수 있어 둘 다 없으면 같음으로 본다.
    """
    ctx.call("layout.template.apply", id="tpl.mdi-cascade")
    pin = ctx.call("panel.pin", id="LeftPane", on=True) or {}
    try:
        def state():
            g = geometry(ctx)
            lp = next((p for p in ctx.call("panel.list") or [] if p.get("id") == "LeftPane"), {})
            d = g["doc"] or {}
            left = [i for i in g["pinItems"] if (i.get("x") or 0) < (d.get("x") or 0) + SLACK]
            return g, {"items": g["pinItems"], "leftItems": len(left), "texts": [i["text"] for i in g["pinItems"]], "pinned": lp.get("pinned")}
        g0, s0 = state()
        ctx.call("layout.template.apply", id="tpl.focus")
        _, s1 = state()
        ctx.call("layout.template.apply", id="tpl.mdi-cascade")
        g2, s2 = state()
        one = lambda s: len(s["items"]) == 1 and s["texts"][0] in PIN_TEXTS and s["leftItems"] == 1
        r0, r2 = _pane_rects(g0), _pane_rects(g2)
        same = {k: (r0[k] is None and r2[k] is None) or _same_rect(r0[k], r2[k]) for k in r0}
        checks = {
            "①_핀적용": pin.get("status") == "Done" and pin.get("pinned") is True,
            "②_기준1건·탐색기·왼쪽": one(s0), "②_pinned필드": s0["pinned"] is True,
            "③_집중중0건": len(s1["items"]) == 0,
            "④_해제뒤1건·같은글자·왼쪽": one(s2) and s2["texts"] == s0["texts"], "④_pinned필드": s2["pinned"] is True,
            "다_사각형±2(핀 회차)": all(same.values()),
        }
        # 트리 글자 판정과 pinned 필드가 어긋나면 그 사실을 남긴다(odev-1 요청)
        mismatch = [n for n, s in (("기준", s0), ("해제뒤", s2)) if one(s) != (s["pinned"] is True)]
        return all(checks.values()), {"checks": checks, "pinBefore": s0, "focus": s1, "pinAfter": s2,
                                      "rects": {"before": r0, "after": r2, "same": same}, "treeVsField": mismatch or "일치"}
    finally:
        ctx.r.call("panel.pin", id="LeftPane", on=False)  # ⑤ 원복


def it_focus_persist(ctx):
    """
    ④ 집중 상태에서 layout.save → 다른 템플릿으로 숨김 해제 → layout.load 하면 숨김이 돌아온다.
    자동 저장 슬롯은 ① 재기동 대조에 쓰이므로 건드리지 않고 개인 레이아웃(시험 전용 이름)으로 저장·불러온 뒤 지운다.
    """
    name = "WM-FOCUS-%s" % datetime.datetime.now().strftime("%H%M%S")
    ctx.call("layout.template.apply", id="tpl.focus")
    f_focus = _saved_flag(ctx)
    s = ctx.call("layout.save", name=name) or {}
    ctx.call("layout.template.apply", id="tpl.mdi-cascade")
    mid, f_mid = panel_state(ctx), _saved_flag(ctx)
    ok = ctx.call("layout.load", id=s.get("id")) is True
    got, f_got = panel_state(ctx), _saved_flag(ctx)
    # 정리: 숨김을 풀고 이번 시험이 만든 레이아웃만 지운다
    ctx.call("layout.template.apply", id="tpl.mdi-cascade")
    if s.get("id"):
        ctx.call("layout.delete", id=s.get("id"), confirm=True)
    # 저장 필드 SidePanelsHidden(LayoutSerializer, lead 01:0x): 집중=true · 해제=false/없음 · 불러온 뒤=true
    flag_ok = f_focus is True and not f_mid and f_got is True
    return (s.get("status") == "Saved" and ok and mid == SHOWN and got == HIDDEN and flag_ok,
            {"save": s, "beforeLoad": mid, "afterLoad": got, "SidePanelsHidden": {"focus": f_focus, "cleared": f_mid, "loaded": f_got}})


def _saved_flag(ctx):
    """layout.get(현재 레이아웃 직렬화 — 저장과 같은 형식)의 SidePanelsHidden 값. 이름 대소문자 둘 다 본다."""
    j = ctx.call("layout.get") or {}
    return j.get("SidePanelsHidden", j.get("sidePanelsHidden"))
