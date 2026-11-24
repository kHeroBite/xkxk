# PERF-17 효과 단계(high·medium·low)별 열기 첫 프레임·전환 커밋 프레임·합성 끌기 커밋 프레임을 기록하는 도구
"""
사용(앱은 run_desktop_bg.sh 로 격리 기동돼 있어야 한다)
  perf_effect_levels.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json> [--n 20] [--docs 20] [--moves 80]
  perf_effect_levels.py --self-test

단계마다 낱개 덮어쓰기 0 을 확인하고 잰다(있으면 INVALID). 전환 효과가 없는 단계(medium·low)의 전환 값은 '재생 없음'이다.
medium 은 PERF-12·13·02 를 모두 만족해야 한다(04/03 §6.4). 여기서는 medium 열기 p95 를 low(효과 없음) p95 * 1.15 와 견주고
medium 의 열기 재생 구간으로 13 을 본다. 02 는 합성 이동만 재므로 기준을 넘으면 '묶음 재검토 신호', 넘지 않아도 DEFERRED 다.
"""
import sys
import time

import perf_effect_common as common
import perf_motion_open as motion

LEVELS = ("high", "medium", "low")


def judge_medium(levels):
    v12, r12 = motion.judge_open(levels["medium"]["open_ms"], levels["low"]["open_ms"])
    v13 = levels["medium"]["play"]["verdict"]
    within = levels["medium"]["drag"].get("within_limit")
    parts = {"PERF-12": dict(r12, verdict=v12), "PERF-13": {"verdict": v13},
             "PERF-02": {"verdict": "DEFERRED", "synthetic_within_limit": within, "note": common.SYNTHETIC}}
    parts["revise_medium_signal"] = v12 == "FAIL" or v13 == "FAIL" or within is False
    if "INVALID" in (v12, v13) or levels["medium"]["drag"]["verdict"] == "INVALID":
        return "INVALID", parts
    if "FAIL" in (v12, v13):
        return "FAIL", parts
    return "DEFERRED", parts  # 실제 끌기(PERF-02) 전에는 합격으로 내지 않는다


def open_plays(s, n):
    perfs = []
    for _ in range(n):
        s.away(True)
        s.call("docs.close", all=True)
        time.sleep(0.6)
        s.call("perf.reset")
        s.call("docs.open", kind="form", count=1)
        time.sleep(1.0)
        perfs.append(s.call("perf.get"))
    return perfs


def measure_level(s, level, a):
    s.effect("effectLevel", level)
    state = s.call("style.state")
    if state.get("effectLevel") != level or (state.get("levelDisplay") or {}).get("overrides"):
        raise common.Abort("INVALID", "단계 %s 만의 상태가 아니다: %s" % (level, state.get("levelDisplay")))
    out = {"moveShadow": state.get("moveShadow"), "open_ms": [s.open_trial("grid") for _ in range(a.n + 1)][1:]}
    if level == "low":
        out["play"] = {"verdict": "RECORDED", "note": "재생 없음(효과 none)"}
    else:
        # high 는 전환, medium 은 전환 효과가 없어 열기 재생 구간으로 잰다
        perfs = open_plays(s, a.n) if level == "medium" else switch_plays(s, a.n)
        v, r = motion.judge_play(perfs)
        out["play"] = dict(r, verdict=v, raw=perfs)
    s.fresh_docs("grid", a.docs)
    s.call("docs.arrange", mode="cascade")
    time.sleep(1.0)
    perf = s.synthetic_drag(a.moves)
    v, r = common.synthetic_verdict(perf)
    out["drag"] = dict(r, verdict=v, raw=perf)
    return out


def switch_plays(s, n):
    docs = s.fresh_docs("form", 2)
    perfs = []
    for i in range(n):
        s.away(True)
        s.call("perf.reset")
        s.call("docs.activateFrom", doc=docs[i % 2]["id"], origin="TitleTab")
        time.sleep(0.8)
        perfs.append(s.call("perf.get"))
    return perfs


def self_test():
    def levels(medium=100.0, low=100.0, play="PASS", within=True, drag="DEFERRED"):
        return {"medium": {"open_ms": [medium] * 20, "play": {"verdict": play}, "drag": {"verdict": drag, "within_limit": within}},
                "low": {"open_ms": [low] * 20}}

    return common.run_cases([
        ("전부 기준 안이어도 DEFERRED(02 실끌기)", judge_medium(levels())[0] == "DEFERRED"),
        ("기준 안이면 재검토 신호 없음", judge_medium(levels())[1]["revise_medium_signal"] is False),
        ("12 초과 FAIL", judge_medium(levels(medium=120.0))[0] == "FAIL"),
        ("13 초과 FAIL", judge_medium(levels(play="FAIL"))[0] == "FAIL"),
        ("합성 초과는 신호만", judge_medium(levels(within=False))[0] == "DEFERRED" and judge_medium(levels(within=False))[1]["revise_medium_signal"]),
        ("13 무효는 INVALID", judge_medium(levels(play="INVALID"))[0] == "INVALID"),
        ("끌기 무효는 INVALID", judge_medium(levels(drag="INVALID"))[0] == "INVALID"),
    ])


def main():
    ap = common.parser(__doc__)
    ap.add_argument("--n", type=int, default=common.MIN_TRIALS)
    ap.add_argument("--docs", type=int, default=20)
    ap.add_argument("--moves", type=int, default=80)
    a = ap.parse_args()
    if a.self_test:
        return self_test()

    def body():
        s = common.Session(a)
        before = s.state0.get("effectLevel") or "theme"
        try:
            levels = {level: measure_level(s, level, a) for level in LEVELS}
        finally:
            s.rig.call("style.effect.set", key="effectLevel", value=before)
            s.rig.call("docs.close", all=True)
        v, parts = judge_medium(levels)
        return {"verdict": v, "PERF-17": {"levels": levels, "medium": parts}, "meta": s.meta}

    return common.finish(a, "perf_effect_levels", body)


if __name__ == "__main__":
    sys.exit(main())
