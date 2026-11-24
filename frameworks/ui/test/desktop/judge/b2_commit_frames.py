#!/usr/bin/env python3
# B2 끌기 프레임 판정기 — 앱이 낸 commit_raw 로 커밋 프레임 p95 와 입력 뒤 누락 공백을 집계해 판정한다(앱 실행 없이 고정 시험 가능)
"""
사용:
  python3 -B b2_commit_frames.py --perf-json <perf.get 결과|measure.json|odebug 결과>   rc 0=PASS 1=FAIL 2=판정 불가(원자료 없음·길이 불일치·truncated)
  python3 -B b2_commit_frames.py --self-test                                             합성 기록 11사례, 전부 기대와 같으면 rc=0
정의(plan a9 반복 3): 커밋 프레임 p95 = committed==1 간격만의 nearest-rank p95(ceil(0.95n)-1), 기준 25ms.
누락 공백 = 입력 시각 이후 첫 커밋까지 25ms 초과(0건 요구). 끝까지 커밋이 없을 때 now-t<=25 는 미결(누락 아님).
값을 만들지 않는다. 원자료가 없으면 판정 불가, commit_source 가 unavailable 이면 FAIL 이다.
"""
import argparse
import json
import math
import sys

FRAME_LIMIT_MS = 25.0
GAP_LIMIT_MS = 25.0
MIN_SAMPLES = 30


def nearest_rank(values, q):
    """앱 PerfStats.Percentile 과 같은 식(ceil(q*n)-1). 빈 목록은 None."""
    if not values:
        return None
    s = sorted(values)
    return s[max(0, math.ceil(q * len(s)) - 1)]


def r2(v):
    return None if v is None else round(v + 0.0, 2)


def indeterminate(reason, source=None):
    return {"indeterminate": True, "pass": False, "commit_source": source, "fail_reasons": [reason]}


def summarize(perf, frame_limit_ms=FRAME_LIMIT_MS, gap_limit_ms=GAP_LIMIT_MS, min_samples=MIN_SAMPLES):
    """perf.get JSON(dict) -> 판정 dict. indeterminate=True 면 판정 불가(호출자가 rc=2)."""
    perf = perf if isinstance(perf, dict) else {}
    source = perf.get("commit_source")
    raw = perf.get("commit_raw")
    if not isinstance(raw, dict):
        if isinstance(source, str) and source.startswith("unavailable"):
            return {"indeterminate": False, "pass": False, "commit_source": source, "fail_reasons": ["commit_source=%s (미측정)" % source]}
        return indeterminate("판정 불가: commit_raw 없음(원자료 없음)", source)
    dts, flags = raw.get("dt_ms"), raw.get("committed")
    if not isinstance(dts, list) or not isinstance(flags, list) or len(dts) != len(flags):
        return indeterminate("판정 불가: dt_ms·committed 길이 불일치", source)
    if raw.get("truncated"):
        return indeterminate("판정 불가: commit_raw truncated", source)
    inputs = sorted(float(x) for x in (raw.get("inputs_ms") or []))
    commits = sorted(float(x) for x in (raw.get("commits_ms") or []))
    now = raw.get("now_ms")
    now = float(now) if isinstance(now, (int, float)) else (max(inputs + commits) if inputs + commits else 0.0)
    committed = [float(d) for d, f in zip(dts, flags) if f]
    idle = [float(d) for d, f in zip(dts, flags) if not f]
    missed, pending, gaps = 0, 0, []
    for t in inputs:
        nxt = next((c for c in commits if c >= t), None)
        if nxt is None:
            if round(now - t, 2) > gap_limit_ms:
                missed += 1
            else:
                pending += 1
            continue
        g = round(nxt - t, 2)
        gaps.append(g)
        if g > gap_limit_ms:
            missed += 1
    p95 = nearest_rank(committed, 0.95)
    reasons = []
    if source != "after_commit":
        reasons.append("commit_source=%s" % source)
    if len(committed) < min_samples:
        reasons.append("커밋 간격 표본 %d < %d" % (len(committed), min_samples))
    if p95 is not None and round(p95, 2) > frame_limit_ms:
        reasons.append("커밋 프레임 p95 %.2f > %g" % (p95, frame_limit_ms))
    if missed:
        reasons.append("누락 공백 %d건(입력 뒤 첫 커밋 >%gms)" % (missed, gap_limit_ms))
    return {
        "indeterminate": False, "metric": "commit_frame_p95", "commit_source": source,
        "commit_frame_samples": len(committed), "commit_frame_p50_ms": r2(nearest_rank(committed, 0.5)),
        "commit_frame_p95_ms": r2(p95), "idle_ticks_excluded": len(idle), "idle_tick_p50_ms": r2(nearest_rank(idle, 0.5)),
        "inputs": len(inputs), "missed_gaps": missed, "pending_inputs": pending,
        "gap_max_ms": max(gaps) if gaps else None, "gap_p95_ms": r2(nearest_rank(gaps, 0.95)),
        "raw_frame_p95_ms": r2(nearest_rank([float(d) for d in dts], 0.95)),
        "pass": not reasons, "fail_reasons": reasons,
    }


def find_perfs(node, out):
    """JSON 어디에 있든 commit_raw/commit_source 를 가진 dict 를 모은다(perf.get·measure.json value·odebug rows[].frames)."""
    if isinstance(node, dict):
        if "commit_raw" in node or "commit_source" in node:
            out.append(node)
        for v in node.values():
            find_perfs(v, out)
    elif isinstance(node, list):
        for v in node:
            find_perfs(v, out)
    return out


def run_perf_json(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    perfs = find_perfs(data, [])
    if not perfs:
        print("판정 불가: commit_raw 없음 — %s 에 커밋 원자료가 없어 새 지표로 재계산할 수 없다(값을 만들지 않음)" % path)
        return 2, [indeterminate("판정 불가: commit_raw 없음(원자료 없음)")]
    results = [summarize(p) for p in perfs]
    for r in results:
        print(json.dumps(r, ensure_ascii=False))
    if any(r["indeterminate"] for r in results):
        return 2, results
    return (0 if all(r["pass"] for r in results) else 1), results


# 자체 시험 ------------------------------------------------------------------
def build(seq, inputs=None, source="after_commit", now_pad=0.0):
    """seq=[(dt,committed)...] -> perf. 커밋 시각은 간격 시작+1ms, inputs=None 이면 커밋 간격마다 시작-0.5ms 에 입력."""
    t, dts, flags, commits, auto = 0.0, [], [], [], []
    for dt, c in seq:
        dts.append(dt)
        flags.append(1 if c else 0)
        if c:
            commits.append(round(t + 1.0, 2))
            auto.append(round(t - 0.5, 2))
        t += dt
    ins = auto if inputs is None else inputs(t, commits)
    return {"commit_source": source, "commit_raw": {"dt_ms": dts, "committed": flags, "inputs_ms": ins, "commits_ms": commits, "now_ms": round(t + now_pad, 2), "truncated": False}}


def with_gap(gap):
    """커밋 40회(모두 16.7ms)에 입력 1건과 그 뒤 첫 커밋까지 gap ms 를 둔 기록(커밋 시각을 입력 기준으로 다시 놓는다)."""
    perf = build([(16.7, True)] * 40, inputs=lambda t, c: [])
    raw = perf["commit_raw"]
    raw["inputs_ms"] = [100.0]
    raw["commits_ms"] = [100.0 + gap] + [100.0 + gap + 16.7 * k for k in range(1, 5)]
    raw["now_ms"] = 800.0
    return perf


def cases():
    pump_off = [(16.7, True), (31.25, False)] * 30 + [(16.7, True)] * 30
    heavy = [(16.7, True)] * 50 + [(40.0, True)] * 10
    throttled = []
    for _ in range(40):
        throttled += [(2.5, True), (38.0, False)]
    one_long = [(16.7, True)] * 40
    out = []

    def add(name, perf, want, check=None, **kw):
        out.append((name, perf, want, check, kw))

    add("1_pump_off", build(pump_off), "PASS", lambda r: r["raw_frame_p95_ms"] == 31.25 and r["commit_frame_p95_ms"] == 16.7 and r["idle_ticks_excluded"] == 30)
    add("heavy_render", build(heavy), "FAIL", lambda r: r["commit_frame_p95_ms"] == 40.0)
    add("3_gap_40ms", with_gap(40.0), "FAIL", lambda r: r["missed_gaps"] == 1 and r["commit_frame_p95_ms"] <= 25)
    b_pass = build([(25.0, True)] * 40)
    add("4a_p95_25.00", b_pass, "PASS", lambda r: r["commit_frame_p95_ms"] == 25.0)
    add("4b_p95_25.01", build([(25.01, True)] * 40), "FAIL")
    add("4c_gap_25.00", with_gap(25.0), "PASS", lambda r: r["missed_gaps"] == 0)
    add("4d_gap_25.01", with_gap(25.01), "FAIL", lambda r: r["missed_gaps"] == 1)
    add("5a_pending", build(one_long, inputs=lambda t, c: [c[-1] + 10.0], now_pad=0.0), "PASS", lambda r: r["pending_inputs"] == 1 and r["missed_gaps"] == 0)
    add("5b_tail_missed", build(one_long, inputs=lambda t, c: [c[-1] + 1.0], now_pad=30.0), "FAIL", lambda r: r["missed_gaps"] == 1)
    add("6_no_commit_raw", {"frame_p95_ms": 31.49}, "NA")
    add("7_unavailable", dict(build(one_long), commit_source="unavailable:no_event"), "FAIL")
    add("8_samples_29", build([(16.7, True)] * 29), "FAIL", lambda r: r["commit_frame_samples"] == 29)
    add("9_pump_on", build([(16.7, True)] * 60), "PASS", lambda r: r["commit_frame_p95_ms"] == r["raw_frame_p95_ms"])
    bad = build(one_long)
    bad["commit_raw"]["committed"] = bad["commit_raw"]["committed"][:-1]
    add("10a_len_mismatch", bad, "NA")
    trunc = build(one_long)
    trunc["commit_raw"]["truncated"] = True
    add("10b_truncated", trunc, "NA")
    add("11_throttled_heavy", build(throttled, inputs=lambda t, c: [c[3] + 2.5]), "FAIL", lambda r: r["commit_frame_p95_ms"] <= 25 and r["missed_gaps"] >= 1)
    return out


def self_test():
    bad = 0
    for name, perf, want, check, _ in cases():
        r = summarize(perf)
        got = "NA" if r["indeterminate"] else ("PASS" if r["pass"] else "FAIL")
        ok = got == want and (check is None or check(r))
        bad += 0 if ok else 1
        print("%s %-18s want=%s got=%s %s" % ("OK  " if ok else "BAD ", name, want, got, ";".join(r["fail_reasons"])))
        if name == "heavy_render":
            print("CANARY heavy_render pass=%s" % r["pass"])
    print("self-test %s (%d/%d)" % ("PASS" if not bad else "FAIL", len(cases()) - bad, len(cases())))
    return 0 if not bad else 1


def main():
    ap = argparse.ArgumentParser(description="B2 커밋 프레임 판정기")
    ap.add_argument("--perf-json")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if a.perf_json:
        return run_perf_json(a.perf_json)[0]
    ap.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
