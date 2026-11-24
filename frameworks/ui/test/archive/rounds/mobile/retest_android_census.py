# Fold5 실기기에서 Android 앱의 테마 순환 뒤 무효화 컨트롤 형식별 개수(theme-census)를 원격 cmd.run 으로 3회 회수하는 스크립트
"""
사용: python3 tools/retest_android_census.py --apk <진단 APK> [--serial R3CW70202FP] [--runs 3] [--outdir logs/otest_m1]
순서(odev-3 지정): docs.open grid·gantt·form → cmd.run cmd=perf-reset → cmd.run cmd=theme-census ×4
                   → cmd.run cmd=theme-census:pd-light → perf.get 으로 결과(theme_census)를 받아 census_now_{n}.json 저장.
원복(finally): forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값.
종료 코드: 0=회수 완료, 1=cmd.run 미지원 등 명령 실패, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402


def run(serial, apk, runs, outdir, state):
    from retest_remote import Remote
    info = ra.prepare(serial, apk, state)
    if not info:
        return {"error": "prepare 실패"}, 2
    r = Remote()
    for k in ("grid", "gantt", "form"):
        r.call("docs.open", kind=k)
    rep, code = {"runs": []}, 0
    for n in range(1, runs + 1):
        row = {"n": n, "calls": []}
        for cmd in ["perf-reset"] + ["theme-census"] * 4 + ["theme-census:pd-light"]:
            res = r.call("cmd.run", cmd=cmd)
            c = r.calls[-1]
            row["calls"].append({"cmd": cmd, "ok": c["ok"], "ms": c["ms"], "elapsed_ms": c["elapsed_ms"], "error": c["error"]})
            if not c["ok"]:
                code = 1
        perf = r.call("perf.get")
        row["theme_census"] = (perf or {}).get("theme_census") if isinstance(perf, dict) else None
        with open(os.path.join(outdir, "census_now_%d.json" % n), "w", encoding="utf-8") as f:
            json.dump({"perf": perf, "calls": row["calls"]}, f, ensure_ascii=False, indent=1)
        rep["runs"].append(row)
    return rep, code


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--runs", type=int, default=3)
    p.add_argument("--outdir", default=os.path.dirname(ra.DEFAULT_OUT))
    a = p.parse_args()
    state, code = {}, 2
    report = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk}
    try:
        res, code = run(a.serial, a.apk, a.runs, a.outdir, state)
        report.update(res)
    except Exception as e:  # noqa: BLE001 — 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
    finally:
        ra.cleanup(a.serial, state)
        os.makedirs(a.outdir, exist_ok=True)
        with open(os.path.join(a.outdir, "android_census_run.json"), "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
