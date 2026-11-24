# PERF-14 불러오는 중 스피너가 도는 동안 MDI 창 합성 이동의 커밋 프레임을 재는 도구
"""
사용(앱은 run_desktop_bg.sh 로 격리 기동돼 있어야 한다)
  perf_busy_drag.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json> [--docs 20] [--moves 80]
  perf_busy_drag.py --self-test

창을 열고 표 1개에 긴 조회(sample.load delayMs)를 걸어 isLoading 을 이동 앞·뒤에 확인한 뒤 docs.moveAll 로 옮긴다.
기준은 04/03 §6.4(커밋 프레임 p95 <= 25ms·누락 0)이고 within_limit 으로만 남긴다.
원격 이동은 '합성 이동'이라 최종 값은 DEFERRED 다(실제 끌기 실측이 남는다). 입력 사건 0·스피너 미유지는 INVALID.
"""
import sys
import time

import perf_effect_common as common

HOLD_MS = 60000


def loading(state, doc_id):
    return any(d.get("id") == doc_id and d.get("isLoading") is True for d in (state or {}).get("docs") or [])


def judge(before, after, perf):
    if not (before and after):
        return "INVALID", {"reason": "측정 구간 내내 불러오는 중이 아니었다", "loading_before": before, "loading_after": after}
    return common.synthetic_verdict(perf)


def self_test():
    state = {"docs": [{"id": "a", "isLoading": True}, {"id": "b", "isLoading": False}]}
    return common.run_cases([
        ("상태 판독", loading(state, "a") and not loading(state, "b") and not loading(None, "a")),
        ("기준 안도 DEFERRED(합성)", judge(True, True, common.fake_perf())[0] == "DEFERRED"),
        ("기준 안 기록", judge(True, True, common.fake_perf())[1]["within_limit"] is True),
        ("초과 기록", judge(True, True, common.fake_perf(40.0))[1]["within_limit"] is False),
        ("입력 0 INVALID", judge(True, True, common.fake_perf(inputs=False))[0] == "INVALID"),
        ("스피너 미유지 INVALID", judge(True, False, common.fake_perf())[0] == "INVALID"),
    ])


def main():
    ap = common.parser(__doc__)
    ap.add_argument("--docs", type=int, default=20)
    ap.add_argument("--moves", type=int, default=80)
    a = ap.parse_args()
    if a.self_test:
        return self_test()

    def body():
        s = common.Session(a)
        docs = s.fresh_docs("grid", a.docs)
        s.call("docs.arrange", mode="cascade")
        target = docs[-1]["id"]  # 마지막에 연 창(맨 위)
        s.call("docs.activate", doc=target)
        time.sleep(0.5)
        try:
            s.call("sample.load", delayMs=HOLD_MS, blockMs=0)
            s.call("grid.search", doc=target, text="")
            time.sleep(1.0)
            before = loading(s.call("window.load.state"), target)
            perf = s.synthetic_drag(a.moves)
            after = loading(s.call("window.load.state"), target)
        finally:
            s.rig.call("sample.load", delayMs=0, blockMs=0)
            s.rig.call("docs.close", all=True)
        v, r = judge(before, after, perf)
        return {"verdict": v, "PERF-14": r, "docs": a.docs, "moves": a.moves, "meta": s.meta, "raw": perf}

    return common.finish(a, "perf_busy_drag", body)


if __name__ == "__main__":
    sys.exit(main())
