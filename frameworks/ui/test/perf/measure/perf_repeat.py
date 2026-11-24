# 성능 반복 측정 새 판(X2 대응) — 시행별 원값 저장·콜드/웜 분리 통계·measure1 횟수·부하 참고 기록을 남기는 래퍼(동결 도구·앱 소스 무변경)
"""
왜 만들었나(odebug-perf X2): perf_ui.py 는 조작당 REPS=12 이고 PerfStats.Percentile=ceil(0.95n)-1 이라 p95=최댓값이며,
첫 회(콜드)와 이후(웜)가 섞이고 시행별 원값이 남지 않는다. 이 도구는 앱 명령(perf.reset·perf.ops·perf.get)만 쓰고
시행마다 perf.reset 으로 기록을 비워 '시행 1건 = 표본 1개' 로 원값을 직접 받는다. 반복 수는 --n 인자(기본 20).

사용(앱이 이미 --remote-control 로 떠 있어야 한다. 이 도구는 앱을 띄우지 않는다):
  python3 -I perf_repeat.py --scenario grid_open --n 20 --out result.json [--build-dir <run폴더>] [--win-cpu]
  python3 -I perf_repeat.py --scenario move20 --n 20 --out result.json
  python3 -I perf_repeat.py --scenario cmd --cmd docs.arrange --cmd-args mode=tile-h --n 20 --out result.json
  python3 -I perf_repeat.py --self-test        # 가짜 입력으로 통계 계산 검증(앱 불필요)
시나리오:
  grid_open : 문서 전부 닫고 grid 1개 열기를 n회. 시행마다 elapsed_ms(요청→화면 반영)와
              grid.open.{attach|measure1|arrange1|render}.{first|next} 의 count·sync_p50(= 그 시행 값)을 기록.
              measure1 count(first/next)는 '첫 레이아웃이 몇 번 돌았나' 를 보여 준다.
  move20    : 문서 20개(grid/gantt/form 순환) 열고 docs.moveAll 30회를 1시행으로 보고 시행의 frame_p50/p95 를 기록(B3 perf02 방식).
  cmd       : 임의 원격 명령 1개를 시행마다 호출(시행 간 상태 복원은 호출자 책임). elapsed_ms·ui_frame_ms 기록.
  (perf01 첫 화면은 시행마다 앱 재기동이 필요해 이 도구 범위 밖이다. 재기동 값 목록은 --first-frame 값들 으로 통계만 낼 수 있다.)
판정: 이 도구는 합격/불합격을 내지 않는다. 통계와 원값만 낸다. 부하(load1·Windows CPU)는 참고 칸(판정에 쓰지 않음, 2026-11-08 사용자 결정).
"""
import argparse
import hashlib
import json
import math
import os
import subprocess
import sys
import time


# ---------- 순수 통계(자체 시험 대상) ----------
def percentile(values, q):
    """선형 보간 백분위(numpy 기본과 같은 방식). q 는 0~100. n=1 이면 그 값."""
    v = sorted(values)
    if not v:
        return None
    if len(v) == 1:
        return v[0]
    pos = (len(v) - 1) * q / 100.0
    lo, hi = math.floor(pos), math.ceil(pos)
    return v[lo] + (v[hi] - v[lo]) * (pos - lo)


def summarize(values):
    vals = [x for x in values if isinstance(x, (int, float))]
    if not vals:
        return {"n": 0, "p50": None, "p95": None, "max": None, "min": None, "mean": None}
    r = lambda x: round(x, 3)
    return {"n": len(vals), "p50": r(percentile(vals, 50)), "p95": r(percentile(vals, 95)),
            "max": r(max(vals)), "min": r(min(vals)), "mean": r(sum(vals) / len(vals))}


def cold_warm(values):
    """콜드 = 1회차 한 값, 웜 = 2회차 이후 통계. 웜 p95 는 웜 표본 수가 20 미만이면 신뢰 낮음 표시."""
    vals = [x for x in values if isinstance(x, (int, float))]
    warm = summarize(vals[1:])
    warm["p95_reliable"] = warm["n"] >= 20
    return {"cold": vals[0] if vals else None, "warm": warm, "all": summarize(vals)}


def stats_of_trials(trials, field):
    return cold_warm([t.get(field) for t in trials])


def looks_cumulative(samples):
    """시행별 frame_samples 가 누적처럼 보이면 True — 3회 이상에서 매 시행 증가하고 마지막이 첫 값의 1.8배 이상.
    perf.reset 이 프레임 표본을 비우지 않던 결함(앱 11-08 수정 전 빌드)을 잡는 안전망이다. 시행이 같은 조작이면 reset 이 듣는 한 표본 수는 비슷하다."""
    v = [x for x in samples if isinstance(x, (int, float))]
    if len(v) < 3 or v[0] <= 0:
        return False
    return all(b > a for a, b in zip(v, v[1:])) and v[-1] >= v[0] * 1.8


def self_test():
    ok = True

    def check(name, cond):
        nonlocal ok
        print(("PASS " if cond else "FAIL ") + name)
        ok = ok and cond

    check("percentile p50 홀수", percentile([3, 1, 2], 50) == 2)
    check("percentile p50 짝수 보간", percentile([1, 2, 3, 4], 50) == 2.5)
    check("percentile p95 선형 보간(1..20)", abs(percentile(list(range(1, 21)), 95) - 19.05) < 1e-9)
    check("percentile n=1", percentile([7], 95) == 7)
    # 구 방식 대비: n=12 에서 ceil(0.95n)-1 은 최댓값이지만 새 방식은 최댓값 미만이어야 한다
    twelve = list(range(1, 13))
    check("n=12 에서 p95 < max(구 방식 결함 해소)", percentile(twelve, 95) < max(twelve))
    # 콜드 분리: 첫 회 300, 이후 20회 100..119
    vals = [300] + [100 + i for i in range(20)]
    cw = cold_warm(vals)
    check("콜드 = 첫 값", cw["cold"] == 300)
    check("웜 n = 20", cw["warm"]["n"] == 20)
    check("웜 max = 119(콜드 제외)", cw["warm"]["max"] == 119)
    check("웜 p50 = 109.5", cw["warm"]["p50"] == 109.5)
    check("웜 p95 = 118.05", abs(cw["warm"]["p95"] - 118.05) < 1e-6)
    check("웜 p95 신뢰 true(n>=20)", cw["warm"]["p95_reliable"] is True)
    check("전체 max = 300", cw["all"]["max"] == 300)
    check("웜 n<20 이면 p95_reliable false", cold_warm([1] * 10)["warm"]["p95_reliable"] is False)
    check("빈 입력 안전", summarize([])["n"] == 0 and cold_warm([])["cold"] is None)
    check("None·문자열 무시", summarize([1, None, "x", 3])["n"] == 2)
    # 시행 레코드 필드 집계
    trials = [{"elapsed_ms": 500.0}, {"elapsed_ms": 200.0}, {"elapsed_ms": 220.0}, {"elapsed_ms": None}]
    s = stats_of_trials(trials, "elapsed_ms")
    check("시행 필드 집계(콜드 500·웜 n=2)", s["cold"] == 500.0 and s["warm"]["n"] == 2)
    # 회귀: perf.reset 이 프레임 표본을 안 비우면 시행별 표본 수가 단조 증가(누적) — 감지해야 한다
    check("누적 감지(100,200,300,400)", looks_cumulative([100, 200, 300, 400]) is True)
    check("reset 정상(95,102,98,101)은 누적 아님", looks_cumulative([95, 102, 98, 101]) is False)
    check("완만한 증가(100,101,102,103)는 누적 아님", looks_cumulative([100, 101, 102, 103]) is False)
    check("시행 2개 이하/빈/None 안전", looks_cumulative([100, 200]) is False and looks_cumulative([]) is False and looks_cumulative([None, None, None]) is False)
    print("SELF-TEST " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


# ---------- 앱 호출 ----------
def load1():
    try:
        return float(open("/proc/loadavg").read().split()[0])
    except OSError:
        return None


def win_cpu():
    """Windows 전체 CPU% 1회 샘플(참고용). 실패하면 None."""
    ps = "(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter \"Name='_Total'\").PercentProcessorTime"
    try:
        out = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", ps], timeout=30).decode().strip().splitlines()
        return float(out[-1])
    except Exception:  # noqa: BLE001 — 참고 칸이라 실패해도 측정을 막지 않는다
        return None


def build_sha(build_dir):
    """--build-dir 안 Pd.Ui.Views.dll 의 sha256(빌드 식별). 없으면 None."""
    if not build_dir:
        return None
    p = os.path.join(build_dir, "Pd.Ui.Views.dll")
    if not os.path.isfile(p):
        return None
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


class Rig:
    def __init__(self):
        here = os.path.dirname(os.path.abspath(__file__))
        sys.path.insert(0, os.path.join(here, "..", "..", "common"))
        import _paths  # noqa: E402
        _paths.add()
        import pdctl  # noqa: E402
        self.pdctl = pdctl
        self.info = pdctl.load_info()

    def raw(self, cmd, **args):
        body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
        try:
            return json.loads(self.pdctl.curl(self.info, "api/cmd", body, timeout=90))
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": str(e)}

    def call(self, cmd, **args):
        r = self.raw(cmd, **args)
        return r.get("result") if r.get("ok") else {"_error": r.get("error")}

    def ops(self, prefix):
        res = self.call("perf.ops", op=prefix)
        return {o["name"]: o for o in res} if isinstance(res, list) else {}


def open_docs(rig, n):
    rig.call("docs.close", all=True)
    per_g, per_t, per_f = n - 2 * (n // 3), n // 3, n // 3
    for kind, c in (("grid", per_g), ("gantt", per_t), ("form", per_f)):
        if c > 0:
            rig.call("docs.open", kind=kind, count=c)
    rig.call("wait.for", docCount=n, timeout=20000)
    rig.call("docs.arrange", mode="cascade")


def trial_grid_open(rig, i):
    rig.call("docs.close", all=True)
    time.sleep(0.3)
    rig.call("perf.reset")
    t0 = time.time()
    r = rig.raw("docs.open", kind="grid", count=1)
    wall = (time.time() - t0) * 1000
    rig.call("wait.for", docCount=1, timeout=20000)
    time.sleep(0.5)  # 지연 기록(render 2프레임) 소진
    ops = rig.ops("grid.open.")
    rec = {"elapsed_ms": r.get("elapsed_ms"), "ui_frame_ms": r.get("ui_frame_ms"), "handler_ms": r.get("ms"),
           "wall_ms": round(wall, 1), "ok": bool(r.get("ok"))}
    for part in ("attach", "measure1", "arrange1", "render"):
        for tag in ("first", "next"):
            o = ops.get(f"grid.open.{part}.{tag}")
            rec[f"{part}_{tag}_count"] = o.get("count") if o else 0
            rec[f"{part}_{tag}_ms"] = (o.get("sync_p50_ms") if o else None)
    rec["measure1_count"] = rec["measure1_first_count"] + rec["measure1_next_count"]
    rec["measure1_ms"] = rec["measure1_first_ms"] if rec["measure1_first_count"] else rec["measure1_next_ms"]
    rec["raw_ops"] = ops  # 필드명 차이 대비 원본 보존
    return rec


def trial_move20(rig, i):
    rig.call("perf.reset")
    for k in range(30):
        rig.call("docs.moveAll", dx=8 if k % 2 == 0 else -8, dy=0)
        time.sleep(0.05)
    p = rig.call("perf.get") or {}
    return {"frame_p50_ms": p.get("frame_p50_ms"), "frame_p95_ms": p.get("frame_p95_ms"), "frame_max_ms": p.get("frame_max_ms"), "frame_samples": p.get("frame_samples")}


def trial_cmd(rig, i, cmd, cargs):
    r = rig.raw(cmd, **cargs)
    return {"elapsed_ms": r.get("elapsed_ms"), "ui_frame_ms": r.get("ui_frame_ms"), "handler_ms": r.get("ms"), "ok": bool(r.get("ok"))}


FIELDS = {
    "grid_open": ["elapsed_ms", "ui_frame_ms", "measure1_ms", "measure1_count", "render_first_ms", "render_next_ms"],
    "move20": ["frame_p50_ms", "frame_p95_ms", "frame_max_ms", "frame_samples"],
    "cmd": ["elapsed_ms", "ui_frame_ms", "handler_ms"],
}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[1] if __doc__ else "")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--scenario", choices=["grid_open", "move20", "cmd"])
    ap.add_argument("--n", type=int, default=20, help="시행 수(기본 20, 20 미만이면 웜 p95 신뢰 낮음 표시)")
    ap.add_argument("--out", help="결과 JSON 경로")
    ap.add_argument("--build-dir", help="Pd.Ui.Views.dll 이 있는 실행 폴더(sha256 기록)")
    ap.add_argument("--win-cpu", action="store_true", help="시작·끝에 Windows CPU%% 참고 기록(느림)")
    ap.add_argument("--cmd")
    ap.add_argument("--cmd-args", nargs="*", default=[], help="키=값 목록")
    ap.add_argument("--docs", type=int, default=20, help="move20 문서 수")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not a.scenario or not a.out:
        ap.error("--scenario 와 --out 이 필요합니다(또는 --self-test)")
    rig = Rig()
    cargs = dict(p.split("=", 1) for p in a.cmd_args)
    pg = rig.call("perf.get") or {}
    meta = {"scenario": a.scenario, "n": a.n, "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "grid_columns": pg.get("grid_columns"), "build_sha256_Pd.Ui.Views": build_sha(a.build_dir),
            "build_dir": a.build_dir, "perf_get_texts": {k: v for k, v in pg.items() if isinstance(v, str)},
            "load1_before": load1(), "win_cpu_before": win_cpu() if a.win_cpu else None}
    if a.scenario == "move20":
        open_docs(rig, a.docs)
        time.sleep(1.0)
    trials = []
    for i in range(a.n):
        if a.scenario == "grid_open":
            rec = trial_grid_open(rig, i)
        elif a.scenario == "move20":
            rec = trial_move20(rig, i)
        else:
            rec = trial_cmd(rig, i, a.cmd, cargs)
        rec["trial"] = i + 1
        rec["load1"] = load1()  # 참고 칸 — 판정에 쓰지 않는다
        trials.append(rec)
        print("trial %d/%d %s" % (i + 1, a.n, {k: rec.get(k) for k in FIELDS[a.scenario]}), flush=True)
    meta.update({"load1_after": load1(), "win_cpu_after": win_cpu() if a.win_cpu else None})
    result = {"meta": meta, "trials": trials,
              "stats": {f: stats_of_trials(trials, f) for f in FIELDS[a.scenario]},
              "warnings": ([] if a.scenario != "move20" or not looks_cumulative([t.get("frame_samples") for t in trials])
                           else ["frame_samples 가 시행마다 누적 증가 — perf.reset 이 프레임 표본을 비우지 않는 빌드(11-08 이전)로 보임. frame_* 값은 시행별 원값이 아니다"]),
              "note": "콜드=1회차, 웜=2회차 이후. p95 는 선형 보간(n<20 이면 p95_reliable=false). 부하는 참고 기록."}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print("saved", a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
