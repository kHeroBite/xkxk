# window_load_judge.py 의 판정 규칙과 자체 시험 — 앱·캡처 없이 값만 판정한다
import judge_session as session

SHAPE_PX = 8  # 바탕과 다른 화소가 이만큼은 있어야 모양


def judge_cycle(samples, idle_px, busy_px, shape_px):
    """samples = 시작~끝 상태 [{loading, shown}]. idle_px 는 끝난 뒤, busy_px 는 도중 아이콘과 평상시 아이콘의 차."""
    loading = [i for i, x in enumerate(samples) if x["loading"]]
    problems = []
    if not loading:
        problems.append("loading 을 보지 못함")
    else:
        if any(not x["loading"] for x in samples[loading[0]:loading[-1]]):
            problems.append("도중에 idle 로 내려감")
        if not any(x["loading"] and x["shown"] for x in samples):
            problems.append("스피너 표시값이 켜지지 않음")
        if any(x["loading"] and not x["shown"] for x in samples[loading[0] + 1:loading[-1]]):
            problems.append("도중에 표시가 꺼짐")
    if not samples or samples[-1]["loading"] or samples[-1]["shown"]:
        problems.append("끝난 뒤 복귀하지 않음")
    if shape_px < SHAPE_PX:
        problems.append("도중 아이콘 자리가 빈 칸(바탕색뿐)")
    if busy_px < SHAPE_PX:
        problems.append("도중 캡처가 평상시 아이콘과 같음")
    if idle_px >= SHAPE_PX:
        problems.append("끝난 뒤 캡처가 원래 아이콘과 다름")
    return {"verdict": "FAIL" if problems else "PASS", "why": ", ".join(problems) or "loading→idle·스피너→원래 아이콘",
            "idle_diff_px": idle_px, "busy_diff_px": busy_px, "busy_shape_px": shape_px, "samples": samples}


def inside(shots, gaps, spacing=180):
    """막힘 구간 안에서 시작하고 끝난 캡처를 0.2초 안팎 간격으로 3장."""
    for start, end in gaps:
        picked = []
        for shot in shots:
            if shot[1] >= start and shot[2] <= end and (not picked or shot[1] - picked[-1][1] >= spacing):
                picked.append(shot)
        if len(picked) >= 3:
            return picked[:3]
    return []


def judge_rotation(shape, turn):
    if len(shape) < 3:
        return {"verdict": "DEFERRED", "why": "막힘 구간 안 캡처 3장을 얻지 못해 회전을 잴 수 없다", "rotation": "미확인"}
    if min(shape) < SHAPE_PX:
        return {"verdict": "FAIL", "why": "막힘 중 스피너 모양이 없다", "shape_px": shape, "rotation_diff_px": turn}
    if max(turn) < SHAPE_PX:
        return {"verdict": "RECORDED", "why": "막힘 중 모양은 있고 회전 차이 없음 — 23 한계 기록·최종 보고 필요",
                "shape_px": shape, "rotation_diff_px": turn, "rotation": "멈춤"}
    return {"verdict": "PASS", "why": "막힘 중 모양·회전 차이 있음", "shape_px": shape, "rotation_diff_px": turn, "rotation": "회전"}


def judge_overlap(rows):
    """rows = [(이름, 기대 수, 실제 수, 표시)]. 수가 남아 있으면 표시도 남아 있어야 한다."""
    bad = [r for r in rows if r[1] != r[2] or r[3] != (r[1] > 0)]
    return {"verdict": "FAIL" if bad else "PASS", "why": "수·표시 불일치 %d" % len(bad), "rows": rows}


def judge_switch_active(rows):
    """rows = [(단계, 자리, 스피너 화소, 바탕색)]. 모든 자리에 모양이 있고, 활성이 넘어가 바탕색이 실제로 바뀌었어야 한다."""
    blank = [r for r in rows if r[2] < SHAPE_PX]
    if blank:
        return {"verdict": "FAIL", "why": "빈 칸 %d자리" % len(blank), "blank": blank, "rows": rows}
    grounds = {}
    for _, place, _, ground in rows:
        grounds.setdefault(place, set()).add(tuple(ground))
    if not any(len(g) > 1 for g in grounds.values()):
        return {"verdict": "INVALID", "why": "활성 전환으로 바탕색이 바뀐 자리가 없다", "rows": rows}
    return {"verdict": "PASS", "why": "활성·비활성 자리 모두 모양 있음, 전환 뒤에도 유지", "rows": rows}


def self_test():
    def cycle(*pairs):
        return [{"loading": a, "shown": b} for a, b in pairs]

    good = cycle((True, True), (True, True), (False, False))
    flicker = cycle((True, True), (False, False), (True, True), (False, False))
    shots = [(i, 1000 + i * 100, 1020 + i * 100) for i in range(8)]
    return session.run_cases([
        ("정상 주기 PASS", judge_cycle(good, 0, 90, 60)["verdict"] == "PASS"),
        ("loading 없음 FAIL", judge_cycle(cycle((False, False)), 0, 90, 60)["verdict"] == "FAIL"),
        ("도중 idle FAIL", judge_cycle(flicker, 0, 90, 60)["verdict"] == "FAIL"),
        ("도중 표시 꺼짐 FAIL", judge_cycle(cycle((True, True), (True, False), (True, True), (False, False)), 0, 90, 60)["verdict"] == "FAIL"),
        ("끝난 뒤 표시 남음 FAIL", judge_cycle(cycle((True, True), (False, True)), 0, 90, 60)["verdict"] == "FAIL"),
        ("도중 캡처 같음 FAIL", judge_cycle(good, 0, 0, 60)["verdict"] == "FAIL"),
        ("빈 칸 FAIL(평상시와 달라도)", judge_cycle(good, 0, 90, 0)["verdict"] == "FAIL"),
        ("원래 아이콘 아님 FAIL", judge_cycle(good, 90, 90, 60)["verdict"] == "FAIL"),
        ("막힘 안 0.2초 간격 3장", [x[0] for x in inside(shots, [(1000, 2000)])] == [0, 2, 4]),
        ("막힘 끝난 뒤 캡처 제외", inside(shots, [(1000, 1300)]) == []),
        ("막힘을 걸친 캡처 제외", inside([(0, 900, 2100)] * 3, [(1000, 2000)]) == []),
        ("3장 미만 DEFERRED", judge_rotation([], [])["verdict"] == "DEFERRED"),
        ("모양 없음 FAIL", judge_rotation([90, 0, 90], [20, 20])["verdict"] == "FAIL"),
        ("멈춤은 RECORDED", judge_rotation([90, 90, 90], [0, 0])["verdict"] == "RECORDED"),
        ("회전 PASS", judge_rotation([90, 90, 90], [20, 30])["verdict"] == "PASS"),
        ("전환 PASS", judge_switch_active([("a", "x", 40, (0, 0, 0)), ("b", "x", 40, (9, 9, 9))])["verdict"] == "PASS"),
        ("전환 뒤 빈 칸 FAIL", judge_switch_active([("a", "x", 40, (0, 0, 0)), ("b", "x", 0, (9, 9, 9))])["verdict"] == "FAIL"),
        ("바탕 그대로면 INVALID", judge_switch_active([("a", "x", 40, (0, 0, 0)), ("b", "x", 40, (0, 0, 0))])["verdict"] == "INVALID"),
        ("겹침 PASS", judge_overlap([("a", 2, 2, True), ("b", 0, 0, False)])["verdict"] == "PASS"),
        ("남았는데 표시 꺼짐 FAIL", judge_overlap([("a", 1, 1, False)])["verdict"] == "FAIL"),
    ])
