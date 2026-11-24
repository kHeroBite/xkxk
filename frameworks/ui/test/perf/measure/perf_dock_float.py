# 도킹 문서를 분리창으로 띄울 때 디자인 표식 첫 프레임 ms(ui.apply.design.firstframe.ms)를 시행별로 재는 도구(동결 도구·앱 소스 무변경)
"""
측정 대상: 원격 명령 docs.sdi on=true(문서를 분리창으로 띄움 — 이미 있는 명령을 그대로 쓴다)로 새 최상위 창이 뜰 때
  ui.apply.design.firstframe.ms        = 새 TopLevel 의 TemplateApplied → Loaded(첫 렌더 뒤) ms (진단판 계측)
  ui.apply.design.firstframe.popup.ms  = 같은 값의 팝업 루트판(참고)
  sdi.host.firstframe                  = SDI 호스트 OnOpened → 첫 프레임 ms(기존 지표, 참고로 함께 기록)
시행마다 perf.reset → docs.sdi on=true → 대기 → perf.ops 로 그 시행 1건을 표본으로 받고 docs.redock 으로 되돌린다.
perf_repeat 의 Rig·cold_warm·summarize·load1·win_cpu·build_sha 를 그대로 import 한다(같은 폴더).
판정: sdi.host.firstframe 만 정본 PERF-11(03_PoC_UI_합격기준 §6.4) p95 ≤ 200ms(표본 all n≥20)로 PASS/FAIL.
  rc 0=PASS, 1=FAIL, 3=보류(표본 부족·값 없음). 디자인 첫 프레임은 기준 없이 참고 기록(콜드=1회차, 웜=2회차 이후).

사용(앱이 이미 --remote-control 진단판으로 떠 있어야 한다. 이 도구는 앱을 띄우지 않는다):
  python3 -I perf_dock_float.py --n 20 --out result.json [--build-dir <run폴더>] [--win-cpu]
  python3 -I perf_dock_float.py --self-test        # 가짜 입력으로 표본 추출·집계 검증(앱 불필요)
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from perf_repeat import Rig, build_sha, cold_warm, load1, summarize, win_cpu  # noqa: E402

REMOTE_CMD = "docs.sdi"  # 분리 원격 명령(RemoteWindowsCommands.cs) — 새로 만들지 않고 재사용
METRIC = "ui.apply.design.firstframe.ms"
METRIC_POPUP = "ui.apply.design.firstframe.popup.ms"
HOST_METRIC = "sdi.host.firstframe"
UNIT = "ms"
HOST_P95_LIMIT_MS = 200.0  # 정본 PERF-11(plan/04_기술스택/03_PoC_UI_합격기준_Mars실측.md §6.4) — 콜드 포함 all p95
MIN_SAMPLES = 20  # PERF-11 판정 표본 하한
FIELDS = ["elapsed_ms", "design_firstframe_ms", "popup_firstframe_ms", "sdi_host_firstframe_ms"]


def pick_ms(ops, name):
    """perf.ops 결과(이름→기록)에서 name 의 ms. 시행 1건이라 sync_p50 = 그 값. 없으면 None."""
    o = ops.get(name)
    if not o:
        return None
    for k in ("sync_p50_ms", "p50_ms", "mean_ms", "max_ms"):
        if isinstance(o.get(k), (int, float)):
            return o[k]
    return None


def make_record(i, resp, wall_ms, ops_design, ops_host):
    """시행 1건의 레코드(순수 함수 — 자체 시험 대상)."""
    return {"trial": i, "ok": bool(resp.get("ok")), "elapsed_ms": resp.get("elapsed_ms"), "wall_ms": round(wall_ms, 1),
            "design_firstframe_ms": pick_ms(ops_design, METRIC), "popup_firstframe_ms": pick_ms(ops_design, METRIC_POPUP),
            "sdi_host_firstframe_ms": pick_ms(ops_host, HOST_METRIC),
            "design_hit": (ops_design.get("ui.apply.design.firstframe.hit") or {}).get("count", 0),
            "design_miss": sum(o.get("count", 0) for n, o in ops_design.items() if ".firstframe.miss" in n),
            "raw_design_ops": ops_design, "raw_host_ops": ops_host}


def judge(st):
    """stats 결과로 sdi.host.firstframe 판정(PERF-11). 표본 부족·값 없음은 HOLD."""
    a = st["sdi_host_firstframe_ms"]["all"]
    if not isinstance(a.get("p95"), (int, float)) or a.get("n", 0) < MIN_SAMPLES:
        return "HOLD"
    return "PASS" if a["p95"] <= HOST_P95_LIMIT_MS else "FAIL"


def verdict_rc(v):
    return {"PASS": 0, "FAIL": 1}.get(v, 3)


def trial_float(rig, i):
    rig.call("docs.redock", all=True)
    rig.call("docs.close", all=True)
    time.sleep(0.3)
    rig.call("docs.open", kind="form", count=1)
    rig.call("wait.for", docCount=1, timeout=20000)
    time.sleep(0.3)
    rig.call("perf.reset")
    t0 = time.time()
    resp = rig.raw(REMOTE_CMD, on=True)
    wall = (time.time() - t0) * 1000
    time.sleep(1.5)  # Loaded·지연 기록 소진
    rec = make_record(i, resp, wall, rig.ops("ui.apply.design.firstframe"), rig.ops(HOST_METRIC))
    rig.call("docs.redock", all=True)
    return rec


def self_test():
    ok = True

    def check(name, cond):
        nonlocal ok
        print(("PASS " if cond else "FAIL ") + name)
        ok = ok and cond

    ops = {METRIC: {"sync_p50_ms": 42.5}, METRIC_POPUP: {"p50_ms": 7.0}, "ui.apply.design.firstframe.hit": {"count": 1}}
    host = {HOST_METRIC: {"sync_p50_ms": 80.0}}
    rec = make_record(1, {"ok": True, "elapsed_ms": 120.0}, 130.04, ops, host)
    check("지표 추출(.ms)", rec["design_firstframe_ms"] == 42.5)
    check("팝업 지표 추출", rec["popup_firstframe_ms"] == 7.0)
    check("기존 호스트 지표 참고 기록", rec["sdi_host_firstframe_ms"] == 80.0)
    check("hit 집계", rec["design_hit"] == 1 and rec["design_miss"] == 0)
    miss = make_record(2, {"ok": True}, 1, {"ui.apply.design.firstframe.miss": {"count": 1}, "ui.apply.design.firstframe.miss.on": {"count": 1}}, {})
    check("miss 집계와 지표 없음 안전", miss["design_miss"] == 2 and miss["design_firstframe_ms"] is None)
    check("pick_ms 빈 입력", pick_ms({}, METRIC) is None and pick_ms({METRIC: {}}, METRIC) is None)
    cw = cold_warm([300.0] + [40.0 + i for i in range(20)])
    check("콜드/웜 분리", cw["cold"] == 300.0 and cw["warm"]["n"] == 20 and cw["warm"]["max"] == 59.0)
    check("집계 None 무시", summarize([1, None, 3])["n"] == 2)
    check("재사용 명령 이름", REMOTE_CMD == "docs.sdi" and UNIT == "ms")
    def st(vals):
        return {"sdi_host_firstframe_ms": cold_warm(vals)}

    check("judge PASS", judge(st([100.0] * 20)) == "PASS")
    check("judge FAIL", judge(st([100.0] * 18 + [300.0, 400.0])) == "FAIL")
    check("judge 표본 부족은 HOLD", judge(st([100.0] * 5)) == "HOLD")
    check("judge 값 없음은 HOLD", judge(st([None] * 20)) == "HOLD")
    check("verdict_rc 매핑", (verdict_rc("PASS"), verdict_rc("FAIL"), verdict_rc("HOLD")) == (0, 1, 3))
    print("SELF-TEST " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description="분리창 디자인 표식 첫 프레임 ms 측정(판정 없음)")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--n", type=int, default=20, help="시행 수(기본 20, 20 미만이면 웜 p95 신뢰 낮음 표시, PERF-11 판정은 20 이상 필요)")
    ap.add_argument("--out", help="결과 JSON 경로")
    ap.add_argument("--build-dir", help="Pd.Ui.Views.dll 이 있는 실행 폴더(sha256 기록)")
    ap.add_argument("--win-cpu", action="store_true", help="시작·끝에 Windows CPU%% 참고 기록(느림)")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not a.out:
        ap.error("--out 이 필요합니다(또는 --self-test)")
    import json
    rig = Rig()
    meta = {"scenario": "dock_float", "remote_cmd": REMOTE_CMD, "metric_name": METRIC, "unit": UNIT, "n": a.n,
            "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "build_sha256_Pd.Ui.Views": build_sha(a.build_dir),
            "build_dir": a.build_dir, "load1_before": load1(), "win_cpu_before": win_cpu() if a.win_cpu else None}
    trials = []
    for i in range(a.n):
        rec = trial_float(rig, i + 1)
        rec["load1"] = load1()  # 참고 칸 — 판정에 쓰지 않는다
        trials.append(rec)
        print("trial %d/%d %s" % (i + 1, a.n, {k: rec.get(k) for k in FIELDS}), flush=True)
    meta.update({"load1_after": load1(), "win_cpu_after": win_cpu() if a.win_cpu else None})
    stats = {f: cold_warm([t.get(f) for t in trials]) for f in FIELDS}
    verdict = judge(stats)
    result = {"meta": meta, "trials": trials, "stats": stats, "verdict": verdict,
              "note": "콜드=1회차, 웜=2회차 이후. 판정은 PERF-11(sdi.host.firstframe all p95 ≤ %sms, n≥%d), 디자인 첫 프레임은 참고. 지표가 None 이면 진단판(PD_DIAGNOSTICS) 빌드가 아니거나 창이 뜨지 않은 시행. 부하는 참고 기록." % (HOST_P95_LIMIT_MS, MIN_SAMPLES)}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print("saved", a.out)
    print("VERDICT", verdict)
    return verdict_rc(verdict)


if __name__ == "__main__":
    sys.exit(main())
