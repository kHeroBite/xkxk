# Windows Edge GPU(headless)로 웹 perf-ui 를 여러 회 돌리며 회차마다 직전 일반 CPU 부하(전체+상위 3 프로세스)를 기록하고 임시 프로필을 지우는 스크립트
# 사용: python3 test/perf/measure/edge_perf_runs.py <URL> <회수=3> <profile=short|full> [타임아웃 초=900] [태그]
# 규약: 한 번에 한 실행만, 성능 구간 통지 뒤에만 실행. 유휴 조건 충족 여부는 결과에 CPU 값으로 명시(충족=전체 CPU 평균 < 20% 이고 상위 프로세스에 부하 없음).
import json
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import edge_headless_perf as eh  # noqa: E402

OUT = eh.OUT


def cpu_before():
    try:
        out = subprocess.check_output([sys.executable, os.path.join(HERE, "cpu_sample.py"), "3", "1"], timeout=60).decode().strip().splitlines()[-1]
        return json.loads(out)
    except Exception as ex:  # CPU 기록 실패는 측정을 막지 않고 사유를 남긴다
        return {"error": repr(ex)}


def main():
    url = sys.argv[1]
    runs = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    profile = sys.argv[3] if len(sys.argv) > 3 else "short"
    secs = int(sys.argv[4]) if len(sys.argv) > 4 else 900
    tag = sys.argv[5] if len(sys.argv) > 5 else "edge"
    results = []
    for i in range(1, runs + 1):
        cpu = cpu_before()
        avg = (cpu.get("total_cpu_pct") or {}).get("avg")
        idle_ok = avg is not None and avg < 20
        full = url + ("?cmd=perf-ui&profile=%s&perf-out=perf_%s.json" % (profile, tag))
        t0 = time.time()
        txt, sec = eh.run_edge(full, secs, "%s%d" % (tag, i))
        m = re.search(r"PERF_JSON:(\{.*\})", txt)
        rec = {"run": i, "profile": profile, "url": url, "seconds": round(sec), "cpu_before": cpu, "idle_condition_met": idle_ok,
               "idle_note": "유휴 조건 불충족(전체 CPU 평균 %s%%)" % avg if not idle_ok else "유휴 조건 충족", "ok": bool(m)}
        if m:
            d = json.loads(m.group(1))
            json.dump(d, open(os.path.join(OUT, "perf_%s_run%d.json" % (tag, i)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            ops = [o for o in d.get("operations", []) if str(o.get("name", "")).startswith("perfui.")]
            rec.update({"ops": len(ops), "frame_p50_ms": d.get("frame_p50_ms"), "frame_p95_ms": d.get("frame_p95_ms"), "startup_ms": d.get("startup_ms"), "gc": d.get("gc"), "docs_profile": d.get("perfui_profile")})
        # 임시 프로필·로그 정리(디스크 여유 보호)
        shutil.rmtree(os.path.join(OUT, "edge_profile_%s%d" % (tag, i)), ignore_errors=True)
        results.append(rec)
        print(json.dumps({k: rec[k] for k in ("run", "seconds", "ok", "idle_note", "frame_p50_ms", "frame_p95_ms") if k in rec}, ensure_ascii=False), flush=True)
        time.sleep(20)  # 회차 사이 식히기
    json.dump(results, open(os.path.join(OUT, "perf_%s_runs.json" % tag), "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
