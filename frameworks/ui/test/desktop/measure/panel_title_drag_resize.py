# 패널 크기 조절·집중 숨김이 패널과 가장자리에 무관하게 같은지 재는 retest_panel_title_drag.py 의 묶음(resize·focus)
import time

SPLIT_GAP = 2  # 패널 가장자리에서 분할선 가운데까지(dip)
SPLIT_DRAG = 40  # 분할선을 끄는 거리(dip)
CASES = (("LeftPane", "Left"), ("BottomPane", "Bottom"), ("NotificationPane", "Right"), ("NotificationPane", "Left"),
         ("NotificationPane", "Top"), ("LeftPane", "Right"), ("BottomPane", "Left"))


def size_of(b, side):
    return b["w"] if side in ("Left", "Right") else b["h"]


def splitter_point(b, side):
    """패널과 문서 쪽 이웃 사이 분할선 위 한 점."""
    cx, cy = b["x"] + b["w"] / 2, b["y"] + b["h"] / 2
    return {"Left": (b["x"] + b["w"] + SPLIT_GAP, cy), "Right": (b["x"] - SPLIT_GAP, cy),
            "Top": (cx, b["y"] + b["h"] + SPLIT_GAP), "Bottom": (cx, b["y"] - SPLIT_GAP)}[side]


def resize(run, core):
    """사람용 경로(분할선 L2 끌기)와 진단 명령 panel.resize 의 값을 남긴다. core 는 retest_panel_title_drag 모듈이다."""
    for pane, side in CASES:
        run.r.call("panel.dock", id=pane, side=side)
        time.sleep(core.SETTLE_S)
        b = core.pane_bounds(run.r, pane)
        at = splitter_point(b, side)
        dx, dy = {"Left": (SPLIT_DRAG, 0), "Right": (-SPLIT_DRAG, 0), "Top": (0, SPLIT_DRAG), "Bottom": (0, -SPLIT_DRAG)}[side]
        run.guard.check()
        core.post(run.hwnd, at, (at[0] + dx, at[1] + dy))
        time.sleep(core.SETTLE_S)
        run.guard.check()
        dragged = size_of(core.pane_bounds(run.r, pane), side)
        run.check("resize.drag.%s.%s" % (pane, side), dragged - size_of(b, side) >= SPLIT_DRAG / 2, {"before": size_of(b, side), "after": dragged})
        res = run.r.call("panel.resize", id=pane, px=dragged - SPLIT_DRAG) or {}
        time.sleep(core.SETTLE_S)
        after = size_of(core.pane_bounds(run.r, pane), side)
        run.check("resize.remote.%s.%s" % (pane, side), res.get("status") == "Resized" and dragged - after >= SPLIT_DRAG / 2, {"status": res.get("status"), "reason": res.get("reason"), "before": dragged, "after": after})
        run.reset()


def focus(run, core):
    """집중 숨김은 모든 패널에 같고, 어느 도구든 보기 메뉴 경로로 되찾는다."""
    panes = core.PANES
    for pane, (tool, home) in core.HOME.items():
        run.r.call("layout.template.apply", id="tpl.focus")
        time.sleep(core.SETTLE_S)
        now = core.panels(run.r)
        run.check("focus.hides." + tool, not any(now[p]["visible"] for p in panes), {p: now[p]["visible"] for p in panes})
        rej = {p: (run.r.call("panel.resize", id=p, px=200) or {}).get("reason") for p in panes}
        run.check("focus.resizeRejected." + tool, set(rej.values()) == {"panel-hidden"}, rej)
        run.r.call("panel.show", tool=tool)  # 보기 메뉴와 같은 ShowTool 이다
        time.sleep(core.SETTLE_S)
        now = core.panels(run.r)
        run.check("focus.show." + tool, all(now[p]["visible"] for p in panes) and now[pane]["side"] == home, {p: (now[p]["visible"], now[p]["side"]) for p in panes})
        run.invariant("keep.focus." + tool)
        run.r.call("layout.template.apply", id="tpl.mdi-cascade")  # 실패해도 다음 도구가 집중 없는 상태에서 시작하게 한다
        time.sleep(core.SETTLE_S)
        run.reset()
