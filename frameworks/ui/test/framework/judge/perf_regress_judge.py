# 성능 퇴행 판정기 — 기준판·대상판 교대 표본의 지표별 중앙값을 비교해 +N% 이내·절대 기준 이내인지 판정한다
"""
사용
  python3 -B perf_regress_judge.py <base.json> <target.json> [--pct 5] [--abs PERF-01=2000] [--metrics PERF-01,...] [--min-n 3] [--out <결과.json>]
  python3 -B perf_regress_judge.py --self-test

입력 형식(perf_baseline.py 가 만든다)
  {"version": "base|target", "commit": "<sha>", "samples": [{"seq": 1, "metrics": {"PERF-01": 1234.5}}, ...]}
  seq 는 두 판을 통틀어 실행한 순서(1부터)다. 두 파일의 seq 를 합쳐 정렬했을 때 판이 하나씩 번갈아 나와야 교대다.

판정(결과 보기 전에 고정)
  - 지표 목록: --metrics 가 있으면 그것, 없으면 기준판 표본에 있는 지표 전부. --abs 로 준 지표도 반드시 있어야 한다.
  - 지표 누락(어느 판이든 표본 하나라도 값 없음) → rc=2
  - 판별 표본 수 < --min-n(기본 3) → rc=2
  - 교대 아님(seq 중복·같은 판 연속) → rc=2
  - 대상 중앙값 > 기준 중앙값 × (1 + pct/100) → FAIL
  - 절대 기준이 있는 지표의 대상 중앙값 > 기준값 → FAIL
  - 하나라도 FAIL → rc=1, 전부 통과 → rc=0
  부하 지표(load 등)는 판정에 쓰지 않는다(참고 기록은 perf_baseline.py 몫).
"""
import argparse
import json
import statistics
import sys

RC_PASS, RC_FAIL, RC_INVALID = 0, 1, 2


def parse_abs(items):
    out = {}
    for it in items or []:
        if "=" not in it:
            raise ValueError("--abs 형식은 이름=ms: %s" % it)
        k, v = it.split("=", 1)
        out[k.strip()] = float(v)
    return out


def alternated(base, target):
    # 두 판의 seq 를 합쳐 정렬했을 때 판이 정확히 번갈아 나오는지
    seqs = [(s.get("seq"), "B") for s in base["samples"]] + [(s.get("seq"), "T") for s in target["samples"]]
    if any(not isinstance(q, int) for q, _ in seqs):
        return False
    if len({q for q, _ in seqs}) != len(seqs):
        return False
    order = [lab for _, lab in sorted(seqs)]
    return all(order[i] != order[i + 1] for i in range(len(order) - 1))


def judge(base, target, pct=5.0, abs_limits=None, metrics=None, min_n=3):
    abs_limits = abs_limits or {}
    res = {"verdict": None, "rc": None, "reasons": [], "metrics": {}, "pct": pct,
           "n_base": len(base.get("samples") or []), "n_target": len(target.get("samples") or [])}
    res["alternated"] = alternated(base, target) if res["n_base"] and res["n_target"] else False
    names = list(metrics) if metrics else sorted({k for s in base.get("samples") or [] for k in (s.get("metrics") or {})})
    for k in abs_limits:
        if k not in names:
            names.append(k)
    if not names:
        res["reasons"].append("지표 없음")
    for k in names:
        for lab, d in (("base", base), ("target", target)):
            miss = [s.get("seq") for s in d.get("samples") or [] if not isinstance((s.get("metrics") or {}).get(k), (int, float))]
            if miss or not d.get("samples"):
                res["reasons"].append("지표 누락: %s %s seq=%s" % (lab, k, miss))
    if res["n_base"] < min_n or res["n_target"] < min_n:
        res["reasons"].append("표본 부족: base=%d target=%d (최소 %d)" % (res["n_base"], res["n_target"], min_n))
    if not res["alternated"]:
        res["reasons"].append("교대 아님(seq 순서가 두 판을 번갈아 실행하지 않음)")
    if res["reasons"]:
        res["verdict"], res["rc"] = "INVALID", RC_INVALID
        return res
    fail = False
    for k in names:
        bm = statistics.median(s["metrics"][k] for s in base["samples"])
        tm = statistics.median(s["metrics"][k] for s in target["samples"])
        lim = bm * (1 + pct / 100.0)
        m = {"base_median_ms": round(bm, 2), "target_median_ms": round(tm, 2), "rel_limit_ms": round(lim, 2),
             "delta_pct": round((tm - bm) / bm * 100.0, 2) if bm else None, "rel_ok": tm <= lim}
        if k in abs_limits:
            m["abs_limit_ms"] = abs_limits[k]
            m["abs_ok"] = tm <= abs_limits[k]
        m["pass"] = m["rel_ok"] and m.get("abs_ok", True)
        if not m["pass"]:
            fail = True
            res["reasons"].append("%s 미달: 대상 %.1f / 상대 한도 %.1f%s" % (
                k, tm, lim, (" / 절대 %.0f" % abs_limits[k]) if k in abs_limits else ""))
        res["metrics"][k] = m
    res["verdict"], res["rc"] = ("FAIL", RC_FAIL) if fail else ("PASS", RC_PASS)
    return res


def _mk(version, vals, start, step=2, key="PERF-01"):
    return {"version": version, "samples": [{"seq": start + i * step, "metrics": {key: v} if v is not None else {}} for i, v in enumerate(vals)]}


def self_test():
    cases = []

    def case(name, got, want):
        ok = got == want
        print("SELFTEST %s %s (got=%s want=%s)" % ("PASS" if ok else "FAIL", name, got, want))
        cases.append(ok)

    a = {"PERF-01": 2000.0}
    # +5% 경계 이내: 기준 중앙값 1000, 대상 1050 → 통과
    r = judge(_mk("base", [990, 1000, 1010], 1), _mk("target", [1040, 1050, 1060], 2), 5, a)
    case("within_pct_boundary", r["rc"], RC_PASS)
    # +5% 초과: 1051 → FAIL
    r = judge(_mk("base", [990, 1000, 1010], 1), _mk("target", [1045, 1051, 1070], 2), 5, a)
    case("over_pct", r["rc"], RC_FAIL)
    # 절대 기준 초과: 기준도 느려 상대는 통과하지만 2000 초과 → FAIL
    r = judge(_mk("base", [2000, 2010, 2020], 1), _mk("target", [2005, 2015, 2025], 2), 5, a)
    case("over_abs", (r["rc"], r["metrics"]["PERF-01"]["rel_ok"], r["metrics"]["PERF-01"]["abs_ok"]), (RC_FAIL, True, False))
    # 표본 < 3 → INVALID
    r = judge(_mk("base", [1000, 1000], 1), _mk("target", [1000, 1000], 2), 5, a)
    case("too_few_samples", r["rc"], RC_INVALID)
    # 교대 아님: 기준 3회 연속 뒤 대상 3회 → INVALID
    r = judge(_mk("base", [1000] * 3, 1, 1), _mk("target", [1000] * 3, 4, 1), 5, a)
    case("not_alternated", r["rc"], RC_INVALID)
    # 지표 누락: 대상 한 표본에 PERF-01 없음 → INVALID
    r = judge(_mk("base", [1000] * 3, 1), _mk("target", [1000, None, 1000], 2), 5, a)
    case("metric_missing", r["rc"], RC_INVALID)
    # 절대 기준 지표가 표본 어디에도 없음 → INVALID
    r = judge(_mk("base", [1000] * 3, 1, key="X"), _mk("target", [1000] * 3, 2, key="X"), 5, a)
    case("abs_metric_absent", r["rc"], RC_INVALID)
    # seq 중복 → 교대 아님
    r = judge(_mk("base", [1000] * 3, 1), _mk("target", [1000] * 3, 1), 5, a)
    case("seq_duplicate", r["alternated"], False)
    return 0 if all(cases) else 1


def main():
    ap = argparse.ArgumentParser(description="기준판 대비 중앙값 +N%·절대 기준 성능 판정")
    ap.add_argument("base", nargs="?")
    ap.add_argument("target", nargs="?")
    ap.add_argument("--pct", type=float, default=5.0)
    ap.add_argument("--abs", action="append", default=[], help="이름=ms (여러 번)")
    ap.add_argument("--metrics", help="쉼표 구분 지표 이름")
    ap.add_argument("--min-n", type=int, default=3)
    ap.add_argument("--out")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.base and a.target):
        ap.print_usage()
        return RC_INVALID
    try:
        base = json.load(open(a.base, encoding="utf-8"))
        target = json.load(open(a.target, encoding="utf-8"))
        res = judge(base, target, a.pct, parse_abs(a.abs), a.metrics.split(",") if a.metrics else None, a.min_n)
    except (OSError, ValueError, KeyError, TypeError) as e:
        print("입력 오류: %s" % e)
        return RC_INVALID
    res["base_commit"], res["target_commit"] = base.get("commit"), target.get("commit")
    txt = json.dumps(res, ensure_ascii=False, indent=2)
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(txt + "\n")
    print(txt)
    print("RESULT=%s rc=%d" % (res["verdict"], res["rc"]))
    return res["rc"]


if __name__ == "__main__":
    sys.exit(main())
