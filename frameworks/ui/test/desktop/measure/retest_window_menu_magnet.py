# Q8 자석 흡착 판정 — docs.move·docs.resize(odev-2 MagnetDriver, 사용자 끌기와 같은 계산 경로)로 범위·실시간 흡착·끄는 가장자리·고정/최대화 제외·놓은 위치·계산 시간을 본다(실행기 retest_window_menu.py)
"""
판정 기준(lead 01:4x): ① 범위 24 기본·0 이면 비활성 ② 이동 중 실시간 흡착 ③ 리사이즈 때 끄는 가장자리만 흡착
④ 고정·최대화 창 제외 ⑤ 놓은 위치 = 마지막으로 보인 위치 ⑥ 계산 시간 프레임당 1ms 미만.
좌표는 MDI 문서 영역 기준 DIP(문서 MdiBounds). 준비: 문서 2개를 세로 바둑판(tile-v)으로 두고 오른쪽 창(R)을 왼쪽 창(L)에서 떼어 gap 을 만든다.
"""
TOL = 0.5  # 흡착 기준선 일치 허용(구동기 SnappedTo 와 같은 기준)
GAP = 60


def _mb(ctx, doc):
    b = (ctx.call("chrome.state", doc=doc) or {}).get("mdiBounds") or {}
    return {"x": b.get("x"), "y": b.get("y"), "w": b.get("w"), "h": b.get("h")}


def _right(b):
    return (b.get("x") or 0) + (b.get("w") or 0)


def _setup(ctx):
    """문서 2개 tile-v → R 을 range=0 으로 줄이고 떼어 GAP 만큼 벌린다."""
    ctx.call("docs.close", all=True)  # 자석 설정 되돌림은 _keep_snap 이 항목마다 한다
    ids = [((ctx.call("docs.open", kind="grid", title=t, allowDuplicate=True) or [{}])[0]).get("id") for t in ("MG-L", "MG-R")]  # docs.open 은 배열
    ctx.call("docs.arrange", mode="tile-v")
    docs = sorted(ids, key=lambda d: _mb(ctx, d)["x"] or 0)
    left, right = docs[0], docs[1]
    ctx.call("snap.set", on=True, range=0)  # 준비 동작은 흡착 없이
    ctx.call("docs.resize", doc=right, edge="right", d=-(GAP * 3))
    ctx.call("docs.move", doc=right, dx=GAP, dy=0)
    return left, right


def _approach(ctx, right, rng, dx=-GAP, steps=12):
    ctx.call("snap.set", on=True, range=rng)
    return ctx.call("docs.move", doc=right, dx=dx, dy=0, steps=steps) or {}


def _ms_ok(run):
    return all((s.get("ms") or 0) < 1 for s in run.get("steps") or []) and (run.get("maxMs") or 0) < 1


def it_magnet_range(ctx):
    """① 범위: reset 하면 24, range=0 이면 이웃 창 가장자리로 다가가도 흡착이 하나도 없다."""
    d = ctx.call("snap.set", reset=True) or {}

    def pure_run(dx):
        """range=0 으로 dx 만큼 다가가며, 각 단계 x = 시작 x + dx·k/n(±0.5) 인지(위치 보정 없음)."""
        left, right = _setup(ctx)
        lr = _right(_mb(ctx, left))
        run = _approach(ctx, right, 0, dx=dx)
        steps = run.get("steps") or []
        st = run.get("start") or {}
        n = len(steps) or 1
        moved = [abs((s["bounds"]["x"] or 0) - ((st.get("x") or 0) + dx * (i + 1) / n)) <= TOL for i, s in enumerate(steps)]
        last_x = steps[-1]["bounds"]["x"] if steps else None
        return {"dx": dx, "pureMotion": bool(steps) and all(moved), "status": run.get("status"), "reason": run.get("reason"),
                "finalGapToLeft": None if last_x is None else round(last_x - lr, 1),
                "coincidentLines": sorted({t for s in steps for t in s.get("snappedTo") or []})}

    # 비활성 판정은 '위치가 보정되지 않는가'로 본다. snappedTo 는 '정확히 겹친 기준선' 목록이라 우연히 맞닿은 선(y=0 의 area.top 등)도
    # 나온다 — 개수로 판정하면 오판(i0215 02:29: 12건 전부). ① dx=−GAP(간격과 같음 → 마지막은 설계상 맞닿음)
    # ② dx=−47(간격과 다름, lead 02:4x): 마지막 간격 13 이 기본 범위 24 안이지만 range=0 이면 붙지 않고 13 그대로여야 한다.
    a = pure_run(-GAP)
    b = pure_run(-47)
    b_ok = b["pureMotion"] and b["finalGapToLeft"] is not None and abs(b["finalGapToLeft"] - (GAP - 47)) <= TOL
    ok = d.get("range") == 24 and a["pureMotion"] and a["status"] == "Done" and b_ok and b["status"] == "Done"
    return ok, {"resetRange": d.get("range"), "gapEqual": a, "gapDiff": b, "gapDiffKept13": b_ok}


def it_magnet_live(ctx):
    """② 이동 중 실시간 흡착: range=24 로 다가가면 gap 이 24 이하가 되는 중간 단계부터 왼쪽 창 right 에 붙는다(마지막 단계만이 아니라)."""
    left, right = _setup(ctx)
    lr = _right(_mb(ctx, left))
    run = _approach(ctx, right, 24)
    steps = run.get("steps") or []
    hit = [s for s in steps if "doc:%s.right" % left in (s.get("snappedTo") or [])]
    first = hit[0] if hit else None
    ok = bool(first) and first["index"] < steps[-1]["index"] and abs((first["bounds"]["x"] or 0) - lr) <= TOL
    return ok, {"leftRight": lr, "firstSnapIndex": first and first["index"], "lastIndex": steps and steps[-1]["index"],
                "firstSnapX": first and first["bounds"]["x"], "snapped": len(hit), "steps": len(steps)}


def it_magnet_resize_edge(ctx):
    """③ 리사이즈: R 의 left 가장자리만 끌면 left 만 왼쪽 창 right 에 붙고, 나머지(right·top·bottom)는 움직이지 않는다."""
    left, right = _setup(ctx)
    lr = _right(_mb(ctx, left))
    ctx.call("snap.set", on=True, range=24)
    run = ctx.call("docs.resize", doc=right, edge="left", d=-GAP, steps=12) or {}
    st = run.get("start") or {}
    steps = run.get("steps") or []
    fixed = all(abs(_right(s["bounds"]) - _right(st)) <= TOL and abs(s["bounds"]["y"] - st["y"]) <= TOL
                and abs(s["bounds"]["h"] - st["h"]) <= TOL for s in steps)
    hit = [s for s in steps if "doc:%s.right" % left in (s.get("snappedTo") or [])]
    ok = run.get("status") == "Done" and fixed and bool(hit) and abs((hit[0]["bounds"]["x"] if hit else -1) - lr) <= TOL
    return ok, {"otherEdgesFixed": fixed, "snapped": len(hit), "leftRight": lr, "status": run.get("status"), "reason": run.get("reason")}


def it_magnet_excluded(ctx):
    """④ 고정 창은 움직이지 않고, 최대화 창은 흡착 기준선(doc:{Id}.*)으로 나오지 않는다."""
    left, right = _setup(ctx)
    if not (ctx.call("chrome.state", doc=right) or {}).get("locked"):
        ctx.call("chrome.lock", doc=right)
    locked = _approach(ctx, right, 24)
    st = locked.get("start") or {}
    lock_still = all(s["bounds"] == st for s in locked.get("steps") or []) and locked.get("final") == st
    ctx.call("chrome.lock", doc=right)  # 고정 해제
    ctx.call("docs.state", doc=left, state="maximized")
    run = _approach(ctx, right, 24)
    leaked = [s["index"] for s in run.get("steps") or [] if any(t.startswith("doc:%s." % left) for t in s.get("snappedTo") or [])]
    ctx.call("docs.state", doc=left, state="normal")
    return lock_still and not leaked, {"lockedNoMove": lock_still, "maximizedAsTargetSteps": leaked,
                                       "lockedStatus": locked.get("status"), "maxStatus": run.get("status")}


def it_magnet_drop(ctx):
    """⑤ 놓은 위치(final) = 마지막으로 보인 단계 위치 = 문서 MdiBounds(chrome.state)."""
    left, right = _setup(ctx)
    run = _approach(ctx, right, 24)
    steps = run.get("steps") or []
    last = steps[-1]["bounds"] if steps else None
    now = _mb(ctx, right)
    same = lambda a, b: a and b and all(abs((a.get(k) or 0) - (b.get(k) or 0)) <= TOL for k in ("x", "y", "w", "h"))
    return bool(same(run.get("final"), last) and same(run.get("final"), now)), {"final": run.get("final"), "lastStep": last, "mdiBounds": now}


def it_magnet_time(ctx):
    """⑥ 계산 시간: 이동·리사이즈 모든 단계의 레이아웃 관리자 계산 시간 < 1ms(프레임당)."""
    left, right = _setup(ctx)
    mv = _approach(ctx, right, 24)
    ctx.call("docs.move", doc=right, dx=GAP, dy=0)
    rs = ctx.call("docs.resize", doc=right, edge="left", d=-GAP, steps=12) or {}
    ctx.call("docs.close", all=True)  # 정리: 시험 문서
    return _ms_ok(mv) and _ms_ok(rs), {"moveMaxMs": mv.get("maxMs"), "resizeMaxMs": rs.get("maxMs")}


def _keep_snap(fn):
    """항목이 바꾼 자석 설정을 그 항목이 끝날 때(예외·PENDING 포함) 바로 되돌린다.
    재기동 대조는 끝의 ctx.restore 보다 먼저 돌아서, 되돌림을 끝으로 미루면 #3 이 등록한 snap 기대값과 어긋났다
    (otest-m-1 01:5x: Android i0136 '재기동 유지: snap.get' FAIL — 재기동 직전 마지막 snap.set on=True range=0 이 남음, 시험 설계 오류)."""
    def run(ctx):
        s0 = ctx.call("snap.get") or {}
        try:
            return fn(ctx)
        finally:
            ctx.r.call("snap.set", on=s0.get("on"), range=s0.get("range"))
    return run


ITEMS_Q8 = [(no, name, _keep_snap(fn)) for no, name, fn in [
    (31, "Q8 자석 범위(기본 24·0 이면 비활성)", it_magnet_range),
    (32, "Q8 이동 중 실시간 흡착", it_magnet_live),
    (33, "Q8 리사이즈 끄는 가장자리만 흡착", it_magnet_resize_edge),
    (34, "Q8 고정·최대화 창 제외", it_magnet_excluded),
    (35, "Q8 놓은 위치 = 마지막으로 보인 위치", it_magnet_drop),
    (36, "Q8 계산 시간 프레임당 1ms 미만", it_magnet_time),
]]
