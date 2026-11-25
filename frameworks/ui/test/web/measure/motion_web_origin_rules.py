# motion_web_origin_click.py 의 판정 순수 함수와 자체 시험 — 배치 대조·양성 대조군·활성 미전환은 INVALID 다
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "desktop", "judge"))
from motion_close_rules import layout_problem  # noqa: E402,F401  배치 기준은 닫기 판정기와 같은 표를 쓴다


GHOST_PRESENT = ("started", "done")  # WindowTransitions.Switch.cs SwitchNote.Ghost — none(덮개 층 없음)·null(짝 그림 없음)은 짝 없음


def judge_pair(switch, pair):
    """
    최대화·DOCK 나가는 짝 판정(근거=lastSwitch). switch = {"before": 조작 전 lastSwitch, "after": 조작 뒤 lastSwitch}, pair = 폴링 동시 재생 수(보조 기록).
    앱 자기 기록(motion.probe lastSwitch)에 기대는 판정이다. 폴링(hold=release)은 부하가 높으면 렌더 사이에 끼지 못해 재생을 하나도 못 볼 수 있어
    (r10: 실패 10건 전부 pair=0, pair=1 은 0건) 짝 판정의 주 근거로 쓰지 않는다. 화면 근거가 필요한 판정(첫 프레임·혼합)은 close·frames 판정기가 맡는다.
    → (verdict, why)
    """
    before = ((switch or {}).get("before") or {}).get("seq") or 0
    after = (switch or {}).get("after") or {}
    grown = isinstance(after.get("seq"), int) and after["seq"] > before
    if grown and after.get("ghost") not in GHOST_PRESENT:
        return "FAIL", "lastSwitch ghost=%s — 나가는 짝 없음" % after.get("ghost")
    if pair == 1:
        return "FAIL", "폴링 동시 재생 1 — 짝 누락"
    if grown:
        return "PASS", "lastSwitch ghost=%s" % after.get("ghost")
    return "INVALID", "관측 없음 — lastSwitch seq %s→%s, 폴링 pair=%s" % (before, after.get("seq"), pair)


def resolve_played(want, perf, switch=None, expect_origin=None):
    """
    첫 전환의 재생 여부와 근거 → (played, basis). played 는 True·False·None(판단 불가), basis 는 perf·lastSwitch·none.
    perf = perf 구간 motion.play 유무. 이 구간은 재생 중 프레임 표본이 0장이면 버려진다(PerfFrameLog: End > Start 만 출력) —
    부하로 프레임 간격이 재생 시간(320ms)보다 길면 재생했어도 '없음'이 될 수 있다(r10 tab_hold2000 2건).
    보조 근거 lastSwitch: seq 가 늘고 ghost 가 started·done 이면 StartSwitch 가 효과 ID 확인을 통과해 재생을 시작한 것이다(짝 있는 최대화·DOCK 만).
    음성 사례(재생 기대 없음)는 perf 없음을 그대로 무재생으로 본다(PASS 근거 — 뒤집지 않는다).
    앱 진단 필드 enter(반영됨, SwitchNote.Enter): seq 가 늘었고 perf 가 없으면 started → 재생, skipped_none → 무재생(효과 없음이 정답),
    skipped_running → 판단 불가(열기 재생 중이라 전환 효과 여부를 이 진단으로 가릴 수 없다). enter 가 없는 옛 행은 아래 ghost 보조 근거를 쓴다.
    한계: enter·ghost 가 모두 없는 옛 행의 겹침(짝 없음)은 보조 근거가 없으므로 빈 구간 탈락이 나면 INVALID(관측 없음)로 남는다.
    """
    if perf:
        return True, "perf"
    if not want:
        return False, "perf"
    before = ((switch or {}).get("before") or {}).get("seq") or 0
    after = (switch or {}).get("after") or {}
    grown = isinstance(after.get("seq"), int) and after["seq"] > before
    enter = after.get("enter")
    if grown and enter == "started":
        return True, "lastSwitch.enter"
    if grown and enter == "skipped_none":
        # 무조건 무재생(FAIL)으로 보지 않는다 — 활성화 출처(after.origin)가 기대 출처와 같을 때만. 다르거나 미기록이면 판단 불가(INVALID)
        origin = after.get("origin")
        if not origin:
            return None, "출처 미기록"
        if expect_origin and origin != expect_origin:
            return None, "출처 불일치"
        return False, "lastSwitch.enter"
    if grown and enter == "skipped_running":
        return None, "lastSwitch.enter"
    if grown and after.get("ghost") in GHOST_PRESENT:
        return True, "lastSwitch"
    return None, "none"


def judge(want, reached, played, pair=None, problem=None, control=True, switch=None):
    """want=재생 기대, reached=활성 문서가 기대와 같음, pair=폴링 동시 재생 수, problem=배치 어긋남, control=양성 대조군 재생,
    switch=짝 판정용 lastSwitch 앞뒤(있으면 judge_pair 가 판정). switch 가 없는 옛 행은 pair 만으로 본다(0 = 관측 없음 INVALID, 1 = 짝 누락 FAIL)."""
    if problem or not control or not reached:
        return "INVALID"
    if played is None:  # 재생 여부를 판단할 근거가 없다(resolve_played basis=none) — 관측 없음
        return "INVALID"
    if played != want:
        return "FAIL"
    if want and switch is not None:
        return judge_pair(switch, pair)[0]
    if want and pair == 0:
        return "INVALID"
    if want and pair is not None and pair < 2:
        return "FAIL"
    return "PASS"


def exit_code(verdicts):
    if "FAIL" in verdicts:
        return 1
    if "ERROR" in verdicts or not verdicts:
        return 2
    return 4 if "INVALID" in verdicts else 0


def self_test():
    cases = {
        "재생": judge(True, True, True) == "PASS",
        "재생 0 은 실패": judge(True, True, False) == "FAIL",
        "짝 1 은 실패": judge(True, True, True, 1) == "FAIL",
        "짝 2": judge(True, True, True, 2) == "PASS",
        "옛 행 짝 0 은 관측 없음": judge(True, True, True, 0) == "INVALID",
        "lastSwitch 짝 있음(폴링 0 이어도)": judge(True, True, True, 0, switch={"before": {"seq": 3}, "after": {"seq": 4, "ghost": "done"}}) == "PASS",
        "lastSwitch started 도 짝 있음": judge(True, True, True, 2, switch={"before": {"seq": 3}, "after": {"seq": 4, "ghost": "started"}}) == "PASS",
        "lastSwitch ghost none 은 짝 없음": judge(True, True, True, 2, switch={"before": {"seq": 3}, "after": {"seq": 4, "ghost": "none"}}) == "FAIL",
        "lastSwitch ghost null 은 짝 없음": judge(True, True, True, 0, switch={"before": {"seq": 3}, "after": {"seq": 4, "ghost": None}}) == "FAIL",
        "폴링 1 은 짝 누락": judge(True, True, True, 1, switch={"before": {"seq": 3}, "after": {"seq": 4, "ghost": "done"}}) == "FAIL",
        "seq 그대로·폴링 0 은 무효": judge(True, True, True, 0, switch={"before": {"seq": 4}, "after": {"seq": 4, "ghost": "done"}}) == "INVALID",
        "lastSwitch 없음·폴링 0 은 무효": judge(True, True, True, 0, switch={"before": None, "after": None}) == "INVALID",
        "played: perf 있으면 perf": resolve_played(True, True) == (True, "perf"),
        "played: 음성 perf 없음은 무재생(PASS 유지)": resolve_played(False, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "done"}}) == (False, "perf")
                                                    and judge(False, True, resolve_played(False, False)[0]) == "PASS",
        "played: 음성 perf 있음은 실패": judge(False, True, resolve_played(False, True)[0]) == "FAIL",
        "played: perf 없음·짝 기록 있음은 재생": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "started"}}) == (True, "lastSwitch"),
        "played: perf 없음·ghost none 은 판단 불가": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "none"}}) == (None, "none"),
        "played: perf 없음·seq 그대로는 판단 불가": resolve_played(True, False, {"before": {"seq": 2}, "after": {"seq": 2, "ghost": "done"}}) == (None, "none"),
        "played: enter started 는 재생": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": None, "enter": "started"}}) == (True, "lastSwitch.enter"),
        "played: enter skipped_none·출처 일치는 무재생(FAIL)": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "none", "enter": "skipped_none", "origin": "more_dblclick"}}, "more_dblclick") == (False, "lastSwitch.enter")
                                                       and judge(True, True, False) == "FAIL",
        "played: enter skipped_none·출처 불일치는 INVALID": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "none", "enter": "skipped_none", "origin": "body_click"}}, "more_dblclick") == (None, "출처 불일치")
                                                       and judge(True, True, None) == "INVALID",
        "played: enter skipped_none·출처 미기록은 INVALID": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "none", "enter": "skipped_none"}}, "more_dblclick") == (None, "출처 미기록"),
        "played: 음성 기대 skipped_none 은 perf 없음 PASS 유지": resolve_played(False, False, {"before": {"seq": 1}, "after": {"seq": 2, "enter": "skipped_none"}}) == (False, "perf"),
        "played: enter skipped_running 은 판단 불가": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "done", "enter": "skipped_running"}}) == (None, "lastSwitch.enter"),
        "played: enter 없음은 ghost 보조(옛 행)": resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": "done"}}) == (True, "lastSwitch"),
        "played: 겹침 perf 없음은 무효(관측 없음)": judge(True, True, resolve_played(True, False, {"before": {"seq": 1}, "after": {"seq": 2, "ghost": None}})[0]) == "INVALID",
        "첫 전환 lastSwitch(앞 없음)": judge(True, True, True, 0, switch={"before": None, "after": {"seq": 1, "ghost": "done"}}) == "PASS",
        "음성 0": judge(False, True, False) == "PASS",
        "음성 재생은 실패": judge(False, True, True) == "FAIL",
        "미전환은 무효": judge(False, False, False) == "INVALID",
        "배치 다르면 무효": judge(True, True, True, problem="effective=pc") == "INVALID",
        "대조군 0 이면 음성도 무효": judge(False, True, False, control=False) == "INVALID",
        "DOCK 기대인데 MDI": layout_problem("dock", {"effective": "Tablet", "zReason": "ok"}) is not None,
        "최대화 맞음": layout_problem("max", {"effective": "Pc", "zReason": "ok", "state": "Maximized"}) is None,
        "FAIL 우선": exit_code(["PASS", "INVALID", "FAIL"]) == 1,
        "빈 결과는 오류": exit_code([]) == 2,
        "무효만": exit_code(["PASS", "INVALID"]) == 4,
    }
    bad = [k for k, ok in cases.items() if not ok]
    print("self-test %d/%d %s" % (len(cases) - len(bad), len(cases), bad))
    return 1 if bad else 0
