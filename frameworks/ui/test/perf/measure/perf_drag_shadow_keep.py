# PERF-16 끌기 중 그림자 숨김/유지의 합성 이동 커밋 프레임 증가분을 기록하는 도구
"""
사용(앱은 run_desktop_bg.sh 로 격리 기동돼 있어야 한다)
  perf_drag_shadow_keep.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json> [--docs 20] [--moves 80] [--rounds 3]
  perf_drag_shadow_keep.py --self-test

숨김·유지를 번갈아 재고 가운데 값 회차로 증가분을 낸다. 판정선은 없다(04/03 §6.4).
유지 p95 가 25ms 를 넘으면 warn_over_limit=true(설정 화면 부하 경고가 필요하다는 기록).
원격 이동은 '합성 이동'이라 실제 끌기 값으로 쓰지 않는다. 실효값 불일치·입력 사건 0 은 INVALID.
"""
import statistics
import sys
import time

import perf_effect_common as common


def compare(hide, keep):
    """hide·keep = perf.get 목록."""
    out = {}
    for name, perfs in (("hide", hide), ("keep", keep)):
        rows = [common.synthetic_verdict(p) for p in perfs]
        if not rows or any(v == "INVALID" for v, _ in rows):
            return "INVALID", {"reason": "%s 원자료·입력 사건 없음" % name}
        if any(r["commit_frame_samples"] < common.b2.MIN_SAMPLES for _, r in rows):
            return "DEFERRED", {"reason": "%s 커밋 표본 부족" % name}
        out[name] = {"p95_ms": statistics.median([r["commit_frame_p95_ms"] for _, r in rows]),
                     "missed_gaps": [r["missed_gaps"] for _, r in rows]}
    out["increase_ms"] = round(out["keep"]["p95_ms"] - out["hide"]["p95_ms"], 2)
    out["warn_over_limit"] = out["keep"]["p95_ms"] > common.b2.FRAME_LIMIT_MS
    out["note"] = common.SYNTHETIC
    return "RECORDED", out


def self_test():
    fast, slow = common.fake_perf(16.0), common.fake_perf(30.0)
    return common.run_cases([
        ("증가분", compare([fast], [slow])[1]["increase_ms"] == 14.0),
        ("기록 값", compare([fast], [fast])[0] == "RECORDED"),
        ("25 초과 경고", compare([fast], [slow])[1]["warn_over_limit"] is True),
        ("25 안 경고 없음", compare([fast], [fast])[1]["warn_over_limit"] is False),
        ("입력 0 INVALID", compare([fast], [common.fake_perf(inputs=False)])[0] == "INVALID"),
        ("표본 부족 DEFERRED", compare([fast], [common.fake_perf(n=10)])[0] == "DEFERRED"),
        ("회차 0 INVALID", compare([], [fast])[0] == "INVALID"),
    ])


def main():
    ap = common.parser(__doc__)
    ap.add_argument("--docs", type=int, default=20)
    ap.add_argument("--moves", type=int, default=80)
    ap.add_argument("--rounds", type=int, default=3)
    a = ap.parse_args()
    if a.self_test:
        return self_test()

    def body():
        s = common.Session(a)
        s.fresh_docs("grid", a.docs)
        s.call("docs.arrange", mode="cascade")
        perfs = {"hide": [], "keep": []}
        try:
            for i in range(a.rounds):
                for mode in (("hide", "keep") if i % 2 == 0 else ("keep", "hide")):
                    r = s.call("style.moveShadow.set", value=mode)
                    if r.get("effective") != mode:
                        raise common.Abort("INVALID", "그림자 %s 가 실효값이 아니다: %s" % (mode, r))
                    time.sleep(0.8)
                    perfs[mode].append(s.synthetic_drag(a.moves))
        finally:
            s.rig.call("style.moveShadow.set")
            s.rig.call("docs.close", all=True)
        v, r = compare(perfs["hide"], perfs["keep"])
        return {"verdict": v, "PERF-16": r, "meta": s.meta, "raw": perfs}

    return common.finish(a, "perf_drag_shadow_keep", body)


if __name__ == "__main__":
    sys.exit(main())
