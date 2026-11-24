#!/usr/bin/env python3
# 요건 2~8 시험 판정 규칙 — 측정 결과 JSON 과 사전 고정 기준을 대조해 AC 별 PASS/FAIL 을 낸다(측정·기록 없음). 지금은 요건 2(retest_busy) 규칙이 들어 있다
"""
사용: python3 -I ui_req2_8_rules.py busy <retest_busy 결과.json>
출력: {"AC-2-01": {"verdict": "PASS|FAIL", "reasons": [...]}, ...} 종료 코드 0=전부 PASS, 1=FAIL 있음, 2=입력 오류.
실제 커서 모양(H-2-1)과 눈에 띄는 정도(H-2-2)는 사람 확인 항목이라 여기서 판정하지 않는다.
"""
import json
import sys

CENTER_TOLERANCE_DIP = 2.0
MAX_SPINNER_DIP = 32.0
MAX_OPACITY = 0.5
FRAME_TIMES = (0.25, 0.5, 0.75, 1.0)
SELFTEST_CASES = ("short_task_not_shown", "min_visible_held", "long_task_hides_at_end", "ref_count", "scopes_independent", "restart_during_linger")
TIMER_KEYS = ("spinner", "cursor", "service", "opening")


def _result(reasons):
    return {"verdict": "FAIL" if reasons else "PASS", "reasons": reasons}


def rule_frames(frames):
    """AC-2-01: 막대마다 폭이 단조 증가, 위 막대가 아래 막대보다 먼저 시작, t=1 이면 4개 모두 완성."""
    reasons = []
    by_t = {round(float(f["t"]), 2): f["bars"] for f in frames}
    missing = [t for t in FRAME_TIMES if t not in by_t]
    if missing:
        return _result(["프레임 없음 t=%s" % missing])
    for bar in range(4):
        widths = [by_t[t][bar]["painted"] for t in FRAME_TIMES]
        if any(a > b for a, b in zip(widths, widths[1:])):
            reasons.append("막대 %d 폭이 단조 증가가 아님: %s" % (bar, widths))
    for t in FRAME_TIMES:
        painted = [b["painted"] for b in by_t[t]]
        started = [p > 0 for p in painted]
        if started != sorted(started, reverse=True):
            reasons.append("t=%s 위에서 아래 순서 위반: %s" % (t, painted))
    if any(b["painted"] <= 0 for b in by_t[1.0]):
        reasons.append("t=1 에 막대 4개가 완성되지 않음")
    if by_t[0.25][1]["painted"] > 0 and by_t[0.25][1]["progress"] > 0:
        reasons.append("t=0.25 에 둘째 막대가 이미 시작됨")
    return _result(reasons)


def _visible(state, scope):
    for s in state.get("scopes", []):
        if s.get("scope") == scope:
            return bool(s.get("visible"))
    return False


def rule_independent(raw):
    """AC-2-02: 두 스코프 동시 표시, 하나를 끝내도 나머지 유지."""
    reasons = []
    both, after = raw["both"], raw["after_end_one"]
    a, b = raw["scope_a"], raw["scope_b"]
    if not (_visible(both, a) and _visible(both, b)):
        reasons.append("동시 표시 실패: %s" % both.get("scopes"))
    if _visible(after, a) or not _visible(after, b):
        reasons.append("하나 끝낸 뒤 상태가 어긋남: %s" % after.get("scopes"))
    return _result(reasons)


def rule_first_open(series):
    """AC-2-03: 창 종류(main·mdi·sdi)마다 표시 구간이 있고 끝나면 꺼진다."""
    reasons = []
    for kind in ("main", "mdi", "sdi"):
        row = series.get(kind)
        if not row:
            reasons.append("%s 시계열 없음" % kind)
            continue
        if not row.get("saw_visible"):
            reasons.append("%s 표시 구간 없음" % kind)
        if not row.get("ended_hidden"):
            reasons.append("%s 끝난 뒤에도 켜져 있음" % kind)
    if series.get("main") and not series["main"].get("startup_open_ended"):
        reasons.append("메인 창 첫 열림 기록(main:open)이 없음")
    return _result(reasons)


def rule_geometry(overlays):
    """AC-2-04: 내용 영역 중심 오차·크기·불투명도·입력 비차단."""
    reasons = []
    if not overlays:
        return _result(["오버레이 측정 없음"])
    for scope, o in overlays.items():
        if abs(o["spinnerCenterX"] - o["regionCenterX"]) > CENTER_TOLERANCE_DIP or abs(o["spinnerCenterY"] - o["regionCenterY"]) > CENTER_TOLERANCE_DIP:
            reasons.append("%s 중심 오차 초과: %s" % (scope, o))
        if max(o["spinnerWidth"], o["spinnerHeight"]) > MAX_SPINNER_DIP:
            reasons.append("%s 크기 초과: %s×%s" % (scope, o["spinnerWidth"], o["spinnerHeight"]))
        if o["spinnerOpacity"] > MAX_OPACITY:
            reasons.append("%s 불투명도 초과: %s" % (scope, o["spinnerOpacity"]))
        if o["hitTestVisible"] or o["spinnerHitTest"]:
            reasons.append("%s 입력을 막을 수 있음" % scope)
    return _result(reasons)


def rule_selftest(selftest):
    """AC-2-05: 가짜 시계 시험 전 사례 통과, 상수값 일치."""
    reasons = []
    if selftest.get("showDelayMs") != 150 or selftest.get("minVisibleMs") != 150:
        reasons.append("상수 불일치: %s/%s" % (selftest.get("showDelayMs"), selftest.get("minVisibleMs")))
    cases = {c["name"]: c for c in selftest.get("cases", [])}
    for name in SELFTEST_CASES:
        case = cases.get(name)
        if case is None:
            reasons.append("사례 없음: %s" % name)
        elif not case.get("pass"):
            reasons.append("%s: %s" % (name, case.get("detail")))
    return _result(reasons)


def rule_idle(idle):
    """AC-2-06: busy 가 아니면 타이머 수 전부 0."""
    timers = idle.get("timers", {})
    return _result(["busy 아닌데 타이머 남음 %s=%s" % (k, timers.get(k)) for k in TIMER_KEYS if timers.get(k) != 0])


def rule_reduced(reduced):
    """AC-2-07: 움직임 줄이기면 정지 프레임, 스피너 타이머 0. 이 측정이 없으면 FAIL 이다(--reduced-motion 기동 회차 필요)."""
    if not reduced:
        return _result(["움직임 줄이기 회차 없음"])
    reasons = []
    if not reduced.get("reducedMotion"):
        reasons.append("reducedMotion=false")
    if not reduced.get("spinnerStatic"):
        reasons.append("정지 프레임이 아님")
    if reduced.get("spinnerRunning") or reduced.get("timers", {}).get("spinner") != 0:
        reasons.append("스피너 타이머가 돎")
    if reduced.get("timers", {}).get("cursor") != 0:
        reasons.append("커서 프레임 타이머가 돎")
    return _result(reasons)


def rule_cursor(cursor):
    """AC-2-08: 프레임 진행·종료 원복·같은 프레임 재사용·Wait 폴백."""
    reasons = []
    if cursor.get("mode") != "frames":
        reasons.append("비트맵 프레임 방식이 아님: %s (%s)" % (cursor.get("mode"), cursor.get("error")))
    if not cursor.get("advanced"):
        reasons.append("프레임 인덱스가 진행하지 않음")
    if not cursor.get("driven_during"):
        reasons.append("busy 중 창 커서가 프레임이 아님")
    if cursor.get("windows_after") != 0:
        reasons.append("종료 뒤 커서가 원복되지 않음")
    if cursor.get("sets_built_after") != cursor.get("sets_built_before"):
        reasons.append("두 번째 시작에서 프레임을 다시 만듦(핸들 재생성)")
    if cursor.get("fallback_mode") != "wait":
        reasons.append("폴백이 Wait 가 아님: %s" % cursor.get("fallback_mode"))
    if cursor.get("fallback_windows_after") != 0:
        reasons.append("폴백 종료 뒤 커서가 원복되지 않음")
    return _result(reasons)


MIN_BLOCK_FRAMES = 10  # 1.5초 막는 동안 렌더 스레드가 그린 프레임 최소값. 애니메이션 지속 여부만 가르는 값이고 성능 기준이 아니다


def rule_long(long_run):
    """완료까지 도는 경로: 양보 뒤 막힌 구간의 렌더 프레임 증가, 비동기 작업 내내 표시, SDI 새 창 표시, 복원 기록. 양보 없는 막기는 기록만 하고 판정하지 않는다."""
    if not long_run:
        return _result(["측정 없음"])
    reasons = []
    delta = (long_run.get("yielded_block") or {}).get("delta")
    if delta is None or delta < MIN_BLOCK_FRAMES:
        reasons.append("양보 뒤 UI 스레드가 막힌 동안 스피너가 멈춤: 프레임 증가 %s" % delta)
    async_path = long_run.get("async_path") or {}
    if not async_path.get("visible_while_running") or not async_path.get("ended_hidden"):
        reasons.append("비동기 작업 내내 표시되지 않거나 끝나고 꺼지지 않음")
    sdi = long_run.get("float") or {}
    if not sdi.get("in_new_window") or not sdi.get("ended_hidden"):
        reasons.append("SDI 분리 뒤 새 창에 스피너가 없음: %s" % sdi.get("overlay"))
    rapid = long_run.get("rapid") or {}
    if rapid.get("theme_before") is None or rapid.get("theme_before") != rapid.get("theme_after"):
        reasons.append("테마 2번 연속 전환 뒤 원래 값이 아님: %s → %s" % (rapid.get("theme_before"), rapid.get("theme_after")))
    if rapid.get("floating_after") == rapid.get("was_floating"):
        reasons.append("한 틱 연타에서 SDI 전환이 1번으로 묶이지 않음: %s → %s" % (rapid.get("was_floating"), rapid.get("floating_after")))
    if not rapid.get("doc_still_open") or rapid.get("scopes_left") != [] or any((rapid.get("timers") or {}).get(k) != 0 for k in TIMER_KEYS):
        reasons.append("연속 전환 뒤 문서가 사라졌거나 스코프·타이머가 남음: %s %s" % (rapid.get("scopes_left"), rapid.get("timers")))
    real = long_run.get("real_paths") or {}
    for path in ("theme", "open", "float", "redock"):
        moved = (real.get(path) or {}).get("delta")
        if moved is None or moved < MIN_BLOCK_FRAMES:
            reasons.append("실제 경로 %s 에서 막힌 동안 스피너가 돌지 않음: 프레임 증가 %s" % (path, moved))
    if not real.get("float_floating") or real.get("redock_floating"):
        reasons.append("지연 실행 뒤 SDI 전환 결과가 어긋남: float=%s redock=%s" % (real.get("float_floating"), real.get("redock_floating")))
    restore = long_run.get("restore") or {}
    if restore.get("history_after", 0) <= restore.get("history_before", 0) or not restore.get("ended_hidden"):
        reasons.append("복원 경로가 스피너를 지나지 않음: %s" % restore)
    return _result(reasons)


def judge_busy(raw):
    return {
        "REQ-2-LONG": rule_long(raw.get("long")),
        "AC-2-01": rule_frames(raw.get("frames", [])),
        "AC-2-02": rule_independent(raw["independent"]) if raw.get("independent") else _result(["측정 없음"]),
        "AC-2-03": rule_first_open(raw.get("first_open", {})),
        "AC-2-04": rule_geometry(raw.get("overlays", {})),
        "AC-2-05": rule_selftest(raw.get("selftest", {})),
        "AC-2-06": rule_idle(raw.get("idle", {})),
        "AC-2-07": rule_reduced(raw.get("reduced")),
        "AC-2-08": rule_cursor(raw.get("cursor", {})),
    }


def main(argv):
    if len(argv) != 2 or argv[0] != "busy":
        print(__doc__)
        return 2
    try:
        with open(argv[1], encoding="utf-8") as f:
            raw = json.load(f)
    except (OSError, ValueError) as e:
        print("입력 오류: %s" % e)
        return 2
    verdicts = judge_busy(raw)
    print(json.dumps(verdicts, ensure_ascii=False, indent=1))
    return 0 if all(v["verdict"] == "PASS" for v in verdicts.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
