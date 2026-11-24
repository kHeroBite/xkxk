# 집중 시험 ③ (라) 분할선 끌기 원격 대리 판정 — panel.resize 뒤 dock Id 기준 사각형(panel.bounds)을 안정될 때까지 다시 재서 +40/−40 을 본다
SLACK = 8  # 분할선·여백 허용 오차(DIP) — retest_window_menu_focus.SLACK 과 같은 값


def try_resize(ctx, delta=40):
    """
    (라) 분할선 끌기: panel.resize(진단 빌드 원격 명령, lead 승인 — 동결 해제 뒤 추가 예정)가 있으면
    LeftPane 너비를 +delta 로 바꿔 패널이 넓어지고 문서 영역이 그만큼 줄었는지 본 뒤 원래 너비로 되돌린다. 명령이 없으면 미판정.
    """
    # 재측정 보정(lead 03:2x, odev-1 요청 — Android i0235 #125 FAIL 은 응답 after +40 인데 재측정이 LeftPane=None·문서 −5.3):
    #  판정 수치는 그대로(LeftPane w +delta ±2, Documents w −delta ±(2+SLACK)). 바뀐 것은 측정 방법뿐이다.
    #  ① 사각형은 문서 영역 x 기준 추정(_pane_rects)이 아니라 dock Id 로 직접 읽는다(panel.bounds: LeftPane·Documents).
    #  ② after 는 응답값을 단독 근거로 쓰지 않는다 — 기록만. panel.bounds 를 100ms 간격으로 다시 재서 연속 2회 같은 값(안정)이어야 채택,
    #     1초 안에 안정되지 않거나 사각형이 없으면(None) FAIL. '마지막 좋은 값 채택' 이 아니라 '안정된 값이 기준 충족' 이다.
    #  ③ before·after 사각형 전체와 측정 시각(ms)을 detail 에 남긴다.
    before = stable_bounds(ctx)
    if before.get("unavailable"):
        return {"judged": False, "why": "미판정 — panel.bounds 없음(이 판본)", "error": before.get("error")}
    b_l, b_d = (before.get("value") or {}).get("LeftPane"), (before.get("value") or {}).get("Documents")
    if not before.get("stable") or not b_l or not b_d:
        return {"judged": True, "ok": False, "why": "기준 사각형 불안정 또는 없음", "before": before}
    res = ctx.r.call("panel.resize", id="LeftPane", px=b_l["w"] + delta)
    if res is None:
        return {"judged": False, "why": "미판정 — panel.resize 없음(이 판본)", "error": (ctx.r.calls[-1] if ctx.r.calls else {}).get("error")}
    def meets(v):  # 판정 기준(수치 불변): LeftPane w +delta±2 · Documents w −delta±(2+SLACK)
        lp, dc = (v or {}).get("LeftPane"), (v or {}).get("Documents")
        return bool(lp and dc and abs(lp["w"] - (b_l["w"] + delta)) <= 2 and abs((b_d["w"] - dc["w"]) - delta) <= 2 + SLACK)

    # otest-1 개선 (c): relayout 이 늦으면 변경 전 값에서 먼저 '안정' 될 수 있다 → 안정 + 기준 충족이 될 때까지 1초 안에서 계속 읽는다.
    # 만료되면 마지막 안정값(또는 마지막 읽기)으로 FAIL — '좋은 값 하나만 보면 채택' 이 아니라 '연속 2회 같은 값이 기준 충족' 이어야 한다.
    after = stable_bounds(ctx, until=meets)
    ctx.r.call("panel.resize", id="LeftPane", px=b_l["w"])  # 되돌림
    ok = bool(after.get("stable") and after.get("reached") and meets(after.get("value")))
    return {"judged": True, "ok": ok, "mode": "원격 대리 판정 — 모델 Proportion 조정, 실제 마우스 끌기 아님(lead 00:4x)",
            "criteria": "LeftPane w +%d±2 · Documents w −%d±%d (안정 재측정 기준)" % (delta, delta, 2 + SLACK),
            "resp": res, "respIsNotEvidence": True, "before": before, "after": after}


def stable_bounds(ctx, timeout_s=1.0, step_s=0.1, until=None):
    """
    panel.bounds(LeftPane·Documents — dock Id 로 직접)를 step_s 간격으로 읽는다(최대 timeout_s).
    until 없음: 연속 2회 같은 값이 나오면 그 값을 채택. until 있음: 연속 2회 같은 값이면서 until(값)=참일 때 채택(reached=True),
    만료되면 마지막 안정값(없으면 마지막 읽기)과 reached=False.
    → {stable, reached, value, reads:[{t_ms, LeftPane, Documents}], unavailable?}. stable=False 면 판정에 쓰지 않는다.
    """
    import time
    t0 = time.time()
    reads, prev, last_stable = [], None, None
    while True:
        pb = ctx.r.call("panel.bounds")
        if pb is None:
            return {"stable": False, "reached": False, "unavailable": True,
                    "error": (ctx.r.calls[-1] if ctx.r.calls else {}).get("error"), "reads": reads}
        cur = {"LeftPane": pb.get("LeftPane"), "Documents": pb.get("Documents")}
        reads.append(dict(cur, t_ms=round((time.time() - t0) * 1000)))
        if prev is not None and cur == prev:
            last_stable = cur
            if until is None or until(cur):
                return {"stable": True, "reached": True, "value": cur, "reads": reads}
        if time.time() - t0 >= timeout_s:
            return {"stable": last_stable is not None, "reached": False, "value": last_stable or cur, "reads": reads}
        prev = cur
        time.sleep(step_s)
