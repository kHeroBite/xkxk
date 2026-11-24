# 좁은 창 도구 머리줄 방송 구간(ui.narrowtool.apply.*) 반복 측정 도구 — narrow_toggle 시나리오(앱·동결 도구 무변경)
"""
측정 대상: NarrowToolChrome.Apply 의 좁음/넓음 전환 1회(진단판 PerfProbe 작업 ui.narrowtool.apply.{narrow|wide}).
시나리오 narrow_toggle: 메인 창 폭을 넓음→좁음→넓음으로 왕복시키며 시행마다 perf.reset → resizeMain → 대기 → perf.ops 로
그 시행의 frame/sync 값을 표본으로 받는다. 좁음 표본·넓음 표본을 따로 모은다.

선결 조건(앱은 이 도구가 띄우지 않는다): 진단판을 --form-factor touch --primary-override ... --remote-control 로 기동.
터치 폼팩터가 아니면 계측이 0건이라 rc=2 로 알린다(측정 실패를 정상 값으로 적지 않는다).

사용:
  python3 -I perf_narrow_tool.py --scenario narrow_toggle --n 20 --out result.json [--wide 1400 --narrow 700 --h 800]
  python3 -I perf_narrow_tool.py --scenario narrow_toggle --dry-run      # 1회만 재고 값 출력
  python3 -I perf_narrow_tool.py --self-test
판정: 정본 PERF-10(03_PoC_UI_합격기준 §6.4) 기준 p95 ≤ 50ms(좁음·넓음 각각, 표본 all n≥20)로 PASS/FAIL 을 낸다.
  rc 0=PASS, 1=FAIL, 3=보류(표본 부족·값 없음), 2=계측 0건. 부하는 참고 칸.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import perf_repeat as pr  # noqa: E402  Rig·통계·부하 참고값 재사용

OP_PREFIX = "ui.narrowtool.apply."
OP_NARROW = OP_PREFIX + "narrow"
OP_WIDE = OP_PREFIX + "wide"
SETTLE_SEC = 1.0
NARROW_P95_LIMIT_MS = 50.0  # 정본 PERF-10(plan/04_기술스택/03_PoC_UI_합격기준_Mars실측.md §6.4) — 콜드 포함 all p95
MIN_SAMPLES = 20  # PERF-10 판정 표본 하한


def pick(ops, name):
    """perf.ops 결과에서 해당 작업의 시행 값(frame·sync p50, 횟수). 없으면 횟수 0."""
    o = ops.get(name)
    if not o:
        return {"count": 0, "frame_ms": None, "sync_ms": None}
    return {"count": o.get("count", 0), "frame_ms": o.get("frame_p50_ms"), "sync_ms": o.get("sync_p50_ms")}


def one_toggle(rig, to_narrow, wide, narrow, h):
    """창 폭을 한 방향으로 바꿔 그 방향 작업 1시행 값을 돌려준다."""
    rig.call("perf.reset")
    rig.call("placement.resizeMain", w=narrow if to_narrow else wide, h=h)
    time.sleep(SETTLE_SEC)
    return pick(rig.ops(OP_PREFIX), OP_NARROW if to_narrow else OP_WIDE)


def run(rig, n, wide, narrow, h):
    rig.call("placement.resizeMain", w=wide, h=h)
    time.sleep(SETTLE_SEC)
    trials = []
    for i in range(n):
        nar = one_toggle(rig, True, wide, narrow, h)
        wid = one_toggle(rig, False, wide, narrow, h)
        rec = {"trial": i + 1, "narrow": nar, "wide": wid, "load1": pr.load1()}  # load1 은 참고 칸
        trials.append(rec)
        print("trial %d/%d narrow=%s wide=%s" % (i + 1, n, nar, wid), flush=True)
    return trials


def stats(trials):
    out = {}
    for side in ("narrow", "wide"):
        for fld in ("frame_ms", "sync_ms"):
            out["%s_%s" % (side, fld)] = pr.cold_warm([t[side][fld] for t in trials])
    return out


def judge(st):
    """stats 결과로 side 별 판정(PERF-10). all.p95 ≤ 한도 PASS, 초과 FAIL, 표본 부족·값 없음 HOLD. 전체는 FAIL>HOLD>PASS 순."""
    out = {}
    for side in ("narrow", "wide"):
        a = st["%s_frame_ms" % side]["all"]
        if not isinstance(a.get("p95"), (int, float)) or a.get("n", 0) < MIN_SAMPLES:
            out[side] = "HOLD"
        else:
            out[side] = "PASS" if a["p95"] <= NARROW_P95_LIMIT_MS else "FAIL"
    v = list(out.values())
    out["overall"] = "FAIL" if "FAIL" in v else ("HOLD" if "HOLD" in v else "PASS")
    return out


def verdict_rc(overall):
    return {"PASS": 0, "FAIL": 1}.get(overall, 3)


def zero_count(trials):
    """모든 시행에서 좁음 계측이 한 번도 안 잡혔으면 True(터치 폼팩터·primary-override 미기동 의심)."""
    return all(t["narrow"]["count"] == 0 for t in trials)


def self_test():
    ok = True

    def check(name, cond):
        nonlocal ok
        print(("PASS " if cond else "FAIL ") + name)
        ok = ok and cond

    ops = {OP_NARROW: {"count": 2, "frame_p50_ms": 4.5, "sync_p50_ms": 1.25}}
    check("pick 값 추출", pick(ops, OP_NARROW) == {"count": 2, "frame_ms": 4.5, "sync_ms": 1.25})
    check("pick 없는 작업은 count 0", pick(ops, OP_WIDE) == {"count": 0, "frame_ms": None, "sync_ms": None})
    check("작업 이름 접두", OP_NARROW.startswith(OP_PREFIX) and OP_WIDE.startswith(OP_PREFIX))
    tr = [{"narrow": {"count": 1, "frame_ms": v, "sync_ms": 1.0}, "wide": {"count": 1, "frame_ms": v + 1, "sync_ms": 1.0}} for v in (9, 4, 5, 6)]
    st = stats(tr)
    check("좁음 콜드 = 첫 값", st["narrow_frame_ms"]["cold"] == 9)
    check("넓음 웜 n = 3", st["wide_frame_ms"]["warm"]["n"] == 3)
    check("zero_count 정상 시행은 False", zero_count(tr) is False)
    check("zero_count 전부 0 이면 True", zero_count([{"narrow": {"count": 0}}, {"narrow": {"count": 0}}]) is True)
    def mk(vals, n_extra=0):
        t = [{"narrow": {"count": 1, "frame_ms": v, "sync_ms": 1.0}, "wide": {"count": 1, "frame_ms": 3.0, "sync_ms": 1.0}} for v in vals]
        return stats(t)

    check("judge PASS", judge(mk([10.0] * 20))["overall"] == "PASS")
    check("judge FAIL(좁음 p95 초과)", judge(mk([10.0] * 18 + [80.0, 90.0]))["narrow"] == "FAIL")
    check("judge 표본 부족은 HOLD", judge(mk([10.0] * 5))["overall"] == "HOLD")
    check("judge 값 없음은 HOLD", judge(mk([None] * 20))["overall"] == "HOLD")
    check("verdict_rc 매핑", (verdict_rc("PASS"), verdict_rc("FAIL"), verdict_rc("HOLD")) == (0, 1, 3))
    print("SELF-TEST " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description="좁은 창 도구 방송 구간 반복 측정")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--scenario", choices=["narrow_toggle"])
    ap.add_argument("--n", type=int, default=20, help="왕복 시행 수(기본 20)")
    ap.add_argument("--wide", type=int, default=1400, help="넓음 창 폭(px)")
    ap.add_argument("--narrow", type=int, default=700, help="좁음 창 폭(px)")
    ap.add_argument("--h", type=int, default=800, help="창 높이(px)")
    ap.add_argument("--out", help="결과 JSON 경로")
    ap.add_argument("--dry-run", action="store_true", help="1회만 재고 값 출력(파일 저장 없음)")
    ap.add_argument("--build-dir", help="Pd.Ui.Views.dll 이 있는 실행 폴더(sha256 기록)")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not a.scenario or not (a.out or a.dry_run):
        ap.error("--scenario 와 (--out 또는 --dry-run) 이 필요합니다(또는 --self-test)")
    rig = pr.Rig()
    n = 1 if a.dry_run else a.n
    trials = run(rig, n, a.wide, a.narrow, a.h)
    if zero_count(trials):
        print("오류: ui.narrowtool.apply.narrow 계측 0건 — --form-factor touch·--primary-override 기동(진단판)이 필요합니다", file=sys.stderr)
        return 2
    if a.dry_run:
        print("dry_run_ms", trials[0]["narrow"]["frame_ms"])
        return 0
    meta = {"scenario": a.scenario, "n": n, "wide": a.wide, "narrow": a.narrow, "h": a.h,
            "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "build_sha256_Pd.Ui.Views": pr.build_sha(a.build_dir),
            "load1_after": pr.load1()}
    result = {"meta": meta, "trials": trials, "stats": stats(trials),
              "judge": judge(stats(trials)),
              "note": "콜드=1회차, 웜=2회차 이후. 판정은 PERF-10(all p95 ≤ %sms, n≥%d). 부하는 참고 기록." % (NARROW_P95_LIMIT_MS, MIN_SAMPLES)}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print("saved", a.out)
    print("VERDICT", result["judge"])
    return verdict_rc(result["judge"]["overall"])


if __name__ == "__main__":
    sys.exit(main())
