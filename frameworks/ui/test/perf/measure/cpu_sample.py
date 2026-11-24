# 웹 성능 회차 직전 Windows 전체 CPU 사용률과 CPU 상위 3개 프로세스(이름·CPU%)를 샘플링해 JSON 한 줄로 출력하는 스크립트(특정 앱을 겨냥하지 않는 일반 부하 기록)
# 사용: python3 test/perf/measure/cpu_sample.py [샘플 수=5] [간격 초=1]
# 방법 명시: 사용자 앱 부하는 상위 프로세스 목록으로 기록한다(프로세스 이름으로 특정 앱을 조회하지 않음).
import json
import subprocess
import sys
import time

PS = r"""
$total = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime
$top = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $_.Name -ne '_Total' -and $_.Name -ne 'Idle' } | Sort-Object PercentProcessorTime -Descending | Select-Object -First 3
$ncpu = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
$parts = $top | ForEach-Object { "{0}:{1}" -f $_.Name, $_.PercentProcessorTime }
Write-Output ("{0}|{1}|{2}" -f $total, $ncpu, ($parts -join ';'))
"""


def one():
    out = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", PS], timeout=30).decode().strip().splitlines()[-1]
    tot, ncpu, tops = out.split("|")
    top = []
    for item in tops.split(";"):
        if ":" in item:
            n, v = item.rsplit(":", 1)
            top.append((n, float(v)))
    return float(tot), int(ncpu), top


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 5
    gap = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
    tots, agg, ncpu = [], {}, None
    for _ in range(n):
        t, ncpu, top = one()
        tots.append(t)
        for name, v in top:
            agg.setdefault(name, []).append(v)
        time.sleep(gap)
    ranked = sorted(((sum(v) / n, max(v), k) for k, v in agg.items()), reverse=True)[:3]
    res = {"samples": n, "total_cpu_pct": {"avg": round(sum(tots) / n, 1), "max": max(tots)},
           "top3_processes(이름, 평균%·최대% — 1코어=100% 기준)": [{"name": k, "avg_pct": round(a, 1), "max_pct": m} for a, m, k in ranked],
           "logical_cpus": ncpu, "method": "일반 부하 기록: 전체 CPU + CPU 상위 3개 프로세스(사용자 앱 부하는 상위 프로세스 목록으로 기록)", "ts": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
    print(json.dumps(res, ensure_ascii=False))


if __name__ == "__main__":
    main()
