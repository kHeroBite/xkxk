#!/usr/bin/env python3
# UI/UX 요건 2~8 Android 축 판정 규칙(판정 계층) — ui28_android.py 결과(<role>_<build>_ui28.json)로 AC-3-10·6-08·7-06 을 판정해 logs/ui28/<AC>.json 을 쓴다
"""
사용: python3 -B ui28_android_rules.py --runs <phone_Debug.json> <tablet_Debug.json> <phone_Release.json> <tablet_Release.json> --writer otest-m-1 --run-id <id> [--dry]
규칙(측정 전 고정 — 2026-11-04 otest-m-1):
  AC-3-10  phone_Debug 의 'AC-3-10' 검사 전부 PASS(≥7건 — 스크립트가 정의한 검사 수. 최초 8로 잘못 센 것을 결과 판정 전에 바로잡음) + tablet_Debug 의 'AC-3-10' 검사 전부 PASS(≥4건, 양성 대조) + 두 Debug 의 CRASH 검사 PASS
  AC-6-08  phone_Debug 의 'AC-6-08' 검사 전부 PASS(≥14건) + CRASH PASS
  AC-7-06  phone_Debug 'AC-7-06' 전부 PASS(≥6건) + tablet_Debug 'AC-7-06' 전부 PASS(≥4건) + CRASH PASS
  공통(Release 기동 게이트): phone_Release·tablet_Release 의 REL·CRASH 검사 전부 PASS — Android 축 AC 는 Debug·Release 양쪽이 기동해야 PASS
      (Release 는 진단이 꺼져 있어 AC 동작 자체는 못 재므로 기동·설정 진입·크래시 0 만 본다)
verdict: 위 조건을 모두 만족하면 PASS, 아니면 FAIL(measurements 에 실패 항목을 남긴다).
"""
import argparse
import hashlib
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))  # 영역 루트(frameworks/ui)
RULES = {
    "AC-3-10": [("phone_Debug", "AC-3-10", 7), ("tablet_Debug", "AC-3-10", 4)],
    "AC-6-08": [("phone_Debug", "AC-6-08", 14)],
    "AC-7-06": [("phone_Debug", "AC-7-06", 6), ("tablet_Debug", "AC-7-06", 4)],
}
CRASH_RUNS = ["phone_Debug", "tablet_Debug"]
REL_RUNS = ["phone_Release", "tablet_Release"]
APK = "src/xkxk.frameworks.ui.android/bin/Debug/net10.0-android/xkxk.frameworks.ui-Signed.apk"
SCRIPT = "test/mobile/measure/ui28_android.py"


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", nargs=4, required=True, help="phone_Debug tablet_Debug phone_Release tablet_Release 결과 JSON")
    ap.add_argument("--writer", default="otest-m-1")
    ap.add_argument("--run-id", required=True)
    ap.add_argument("--build-file", default=APK, help="시험한 판본 APK(결과 build 에 기록, 기본은 bin/Debug 산출물)")
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()
    names = ["phone_Debug", "tablet_Debug", "phone_Release", "tablet_Release"]
    runs = {n: json.load(open(p, encoding="utf-8")) for n, p in zip(names, a.runs)}
    gate_rel = []
    for n in REL_RUNS:
        bad = [c["name"] for c in runs[n]["checks"] if not c["pass"] and (c["name"].startswith("REL") or c["name"].startswith("CRASH"))]
        gate_rel.append({"run": n, "fail": bad, "n": len(runs[n]["checks"])})
    out_dir = os.path.join(ROOT, "logs/ui28")
    os.makedirs(out_dir, exist_ok=True)
    rc = 0
    for ac, parts in RULES.items():
        ms, ok = [], True
        for run, prefix, minimum in parts:
            cs = [c for c in runs[run]["checks"] if c["name"].startswith(prefix)]
            bad = [c["name"] for c in cs if not c["pass"]]
            ms.append({"name": "%s:%s checks" % (run, prefix), "value": {"n": len(cs), "fail": bad}, "expected": "≥%d건 전부 PASS" % minimum})
            ok = ok and len(cs) >= minimum and not bad
        for run in CRASH_RUNS:
            bad = [c["name"] for c in runs[run]["checks"] if c["name"].startswith("CRASH") and not c["pass"]]
            has = [c for c in runs[run]["checks"] if c["name"].startswith("CRASH")]
            ms.append({"name": "%s:CRASH" % run, "value": {"n": len(has), "fail": bad}, "expected": "1건 PASS"})
            ok = ok and len(has) == 1 and not bad
        for g in gate_rel:
            ms.append({"name": "%s:REL·CRASH" % g["run"], "value": {"n": g["n"], "fail": g["fail"]}, "expected": "전부 PASS(Release 기동 게이트)"})
            ok = ok and g["n"] >= 5 and not g["fail"]
        ms.append({"name": "elapsed_ms", "value": sum(r.get("elapsed_ms", 0) for r in runs.values())})
        evid = [p for p in a.runs]
        res = {"verdict": "PASS" if ok else "FAIL", "ac_id": ac, "writer": a.writer, "run_id": a.run_id,
               "started_at": min(r["started_at"] for r in runs.values()), "finished_at": max(r["finished_at"] for r in runs.values()),
               "script": {"path": SCRIPT, "sha256": sha(os.path.join(ROOT, SCRIPT))},
               "build": {"path": os.path.relpath(os.path.join(ROOT, a.build_file), ROOT), "sha256": sha(os.path.join(ROOT, a.build_file))},
               "measurements": ms, "evidence": [os.path.relpath(p, ROOT) for p in evid]}
        print(ac, res["verdict"])
        if not a.dry:
            json.dump(res, open(os.path.join(out_dir, ac + ".json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        rc = rc or (0 if ok else 1)
    return rc


if __name__ == "__main__":
    sys.exit(main())
