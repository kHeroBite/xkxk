# PERF-12(효과 켬/끔 문서 열기 첫 프레임)·PERF-13(효과 재생 중 커밋 프레임)을 재고 판정하는 도구
"""
사용(앱은 run_desktop_bg.sh 로 격리 기동돼 있어야 한다)
  perf_motion_open.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json> [--n 20] [--effect fadeZoom]
  perf_motion_open.py --self-test

PERF-12: doc.open.grid(열기 명령 → 첫 프레임, 효과 재생 시간은 안 들어감)를 perf.ops 에서 시행마다 1건 받는다.
         켬·끔을 번갈아 재고(각 첫 1회는 예열로 버림) 켬 p95 <= 끔 p95 * 1.15, 각 n >= 20. 데스크톱 값이라 웹 합격은 따로 잰다.
PERF-13: 창 2개를 출처 TitleTab 으로 번갈아 전환해 motion.play 구간(phases[].phase_raw)의 커밋 간격 p95 <= 25ms·표본 >= 30,
         재생 시작(입력 사건) 뒤 25ms 안에 첫 커밋이 없으면 누락. 구간 표식이나 입력 사건이 없으면 INVALID.
재생 시간: 효과 종류(motion.open 등)만 켜면 재생 시간 키(motion)가 0ms 인 테마(ds-classic 등)에서는 재생을 건너뛴다.
         그래서 측정 동안 motion 을 --motion 값(기본 subtle)으로 덮어쓰고, 적용된 재생 시간(PdStyleMotionMs)이 0 이면
         측정하지 않고 INVALID 로 낸다. 끝나면 시작 때의 motion 덮어쓰기 값(없으면 해제)으로 되돌린다.
         결과 JSON 의 motion 에 덮어쓴 값·적용 ms·되돌린 값을 남긴다.
"""
import sys
import time

import perf_effect_common as common

RATIO_LIMIT = 1.15
PHASE = "motion.play"


def judge_open(on, off, minimum=common.MIN_TRIALS):
    a, b = [v for v in on if v is not None], [v for v in off if v is not None]
    if len(a) < minimum or len(b) < minimum:
        return "DEFERRED", {"n_on": len(a), "n_off": len(b), "need": minimum}
    p_on, p_off = common.percentile(a, 95), common.percentile(b, 95)
    body = {"on": common.summarize(a), "off": common.summarize(b), "p95_on": p_on, "p95_off": p_off,
            "ratio": round(p_on / p_off, 4) if p_off else None, "limit_ratio": RATIO_LIMIT}
    if not p_off:
        return "INVALID", body
    return ("PASS" if p_on <= p_off * RATIO_LIMIT + 1e-9 else "FAIL"), body


def judge_play(perfs):
    """전환 시행들의 perf.get 을 합쳐 판정. p95 는 재생 구간만, 누락은 시행마다 입력 → 첫 커밋."""
    dts, flags, inputs, missed, sources = [], [], 0, 0, set()
    for perf in perfs:
        r = common.b2.summarize(perf)
        if r["indeterminate"] or r.get("commit_source") != "after_commit":
            return "INVALID", {"reason": "커밋 원자료 없음", "detail": r}
        found = [p.get("phase_raw") for p in perf.get("phases") or [] if p.get("name") == PHASE]
        if not found:
            return "INVALID", {"reason": "재생 구간(%s) 0건 — 재생 시간이 0ms 이거나 효과가 재생되지 않았다" % PHASE, "plays_without_phase": 1}
        if any(not isinstance(f, dict) for f in found) or not r["inputs"]:
            return "INVALID", {"reason": "재생 구간(%s) 또는 입력 사건 없음" % PHASE}
        for f in found:
            if len(f["dt_ms"]) != len(f["committed"]):
                return "INVALID", {"reason": "구간 원자료 길이 불일치"}
            dts += f["dt_ms"]
            flags += f["committed"]
        inputs += r["inputs"]
        missed += r["missed_gaps"]
    committed = [float(d) for d, c in zip(dts, flags) if c]
    p95 = common.b2.nearest_rank(committed, 0.95)
    body = {"plays": len(perfs), "inputs": inputs, "missed_gaps": missed, "commit_frame_samples": len(committed),
            "commit_frame_p95_ms": p95, "idle_ticks_excluded": len(dts) - len(committed)}
    if len(committed) < common.b2.MIN_SAMPLES:
        return "DEFERRED", body
    return ("PASS" if p95 <= common.b2.FRAME_LIMIT_MS and not missed else "FAIL"), body


def set_open_effect(s, value):
    s.effect("motion.open", value)
    s.effect("motion.close", "none")  # 앞 시행의 닫기 효과가 다음 열기에 겹치지 않게


def set_motion(s, value):
    """재생 시간 키를 덮어쓰고 적용된 ms 를 돌려준다. 0 이면 재생이 없으므로 측정하지 않는다."""
    s.effect("motion", value)
    ms = ((s.call("style.state") or {}).get("resources") or {}).get("PdStyleMotionMs")
    try:
        ms = float(ms)
    except (TypeError, ValueError):
        ms = 0.0
    if ms <= 0:
        raise common.Abort("INVALID", "motion=%s 의 적용 재생 시간이 0ms 다(PdStyleMotionMs=%r) — 재생 구간이 생기지 않는다" % (value, ms))
    return ms


def measure_open(s, n, effect):
    samples = {"on": [], "off": []}
    for i in range(n + 1):
        for arm in (("on", "off") if i % 2 else ("off", "on")):
            set_open_effect(s, effect if arm == "on" else "none")
            v = s.open_trial("grid")
            if i:
                samples[arm].append(v)
    return samples


def measure_play(s, n):
    s.effect("motion.switch", "same_as_open")
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
    def play(dt=16.7, n=20, inputs=True, phase=True):
        p = common.fake_perf(dt, n, inputs)
        p["phases"] = [{"name": PHASE, "phase_raw": {"dt_ms": [dt] * n, "committed": [1] * n}}] if phase else []
        return p

    return common.run_cases([
        ("1.15 경계 PASS", judge_open([115.0] * 20, [100.0] * 20)[0] == "PASS"),
        ("1.15 초과 FAIL", judge_open([115.2] * 20, [100.0] * 20)[0] == "FAIL"),
        ("표본 19 DEFERRED", judge_open([1.0] * 19, [1.0] * 20)[0] == "DEFERRED"),
        ("빈 표본은 세지 않음", judge_open([None] * 20, [1.0] * 20)[0] == "DEFERRED"),
        ("기준 0 INVALID", judge_open([1.0] * 20, [0.0] * 20)[0] == "INVALID"),
        ("재생 25ms 안 PASS", judge_play([play(), play()])[0] == "PASS"),
        ("재생 40ms FAIL", judge_play([play(40.0), play(40.0)])[0] == "FAIL"),
        ("구간 없음 INVALID", judge_play([play(phase=False)])[0] == "INVALID"),
        ("입력 0 INVALID", judge_play([play(inputs=False)])[0] == "INVALID"),
        ("구간 표본 20 DEFERRED", judge_play([play()])[0] == "DEFERRED"),
    ])


def main():
    ap = common.parser(__doc__)
    ap.add_argument("--n", type=int, default=common.MIN_TRIALS)
    ap.add_argument("--effect", default="fadeZoom")
    ap.add_argument("--motion", default="subtle", help="측정 동안 덮어쓸 재생 시간 키(motion) 값 — 0ms 가 아닌 값")
    a = ap.parse_args()
    if a.self_test:
        return self_test()

    def body():
        s = common.Session(a)
        motion_before = (s.state0.get("overrides") or {}).get("motion") or ""
        motion = {"override": a.motion, "before_override": motion_before or None}
        try:
            motion["applied_ms"] = set_motion(s, a.motion)
            samples = measure_open(s, a.n, a.effect)
            v12, r12 = judge_open(samples["on"], samples["off"])
            set_open_effect(s, a.effect)
            perfs = measure_play(s, a.n)
            v13, r13 = judge_play(perfs)
        finally:
            for key in ("motion.open", "motion.close", "motion.switch"):
                s.rig.call("style.effect.set", key=key, value="")
            s.rig.call("style.effect.set", key="motion", value=motion_before)
        motion["restored_ms"] = ((s.call("style.state") or {}).get("resources") or {}).get("PdStyleMotionMs")
        order = ["INVALID", "FAIL", "DEFERRED", "PASS"]
        return {"verdict": min((v12, v13), key=order.index), "scope": "desktop(웹 PERF-12 는 별도)", "meta": s.meta,
                "effect": a.effect, "motion": motion,
                "PERF-12": dict(r12, verdict=v12, samples=samples), "PERF-13": dict(r13, verdict=v13, raw=perfs)}

    return common.finish(a, "perf_motion_open", body)


if __name__ == "__main__":
    sys.exit(main())
