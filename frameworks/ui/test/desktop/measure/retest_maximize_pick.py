#!/usr/bin/env python3
# 소속 모니터 판정 표본 측정 — 원격 monitor.pick(순수 함수)에 모니터 표·창 사각형을 주어 겹침 면적 최대 모니터를 표와 대조한다. 창을 옮기지 않고 사용자 모니터에 아무것도 놓지 않는다
"""
사용: PD_REMOTE_INFO=<임시 data>/remote-control.json python3 -B retest_maximize_pick.py --out <logs/<에이전트>/<회차>/pick.json>
앱은 run_desktop_bg.sh 로 진단 복사본을 임시 --data-dir·--profile-dir, --multi-instance on, --remote-control, 화면 밖 --bg-pos auto 로 띄워 둔다.
표본의 모니터 좌표는 이 PC 실측값(왼쪽·가운데(주)·오른쪽, 세로 위치 다름)과 위쪽 작업 표시줄 없는 합성 배치다. 50:50 기대값은 표준 Win32 창 실측(주 모니터 쪽)이다.
종료 코드: 0=전부 일치, 1=불일치, 2=연결 실패.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402

PC = "C:0,0,2560,1440;L:-2560,5,2560,1440;R:2560,236,1920,1200"  # 주 모니터를 앞에 둔다(DesktopMonitorProvider 정렬과 같음)
STACK = "A:0,0,1920,1080;B:0,1080,1920,1080"  # 위아래 배치
SAMPLES = [
    ("왼쪽 소속", PC, "-2360,155,1400,800", "L"),
    ("가운데 소속", PC, "200,150,1400,800", "C"),
    ("오른쪽 소속", PC, "2760,386,1400,800", "R"),
    ("왼쪽|가운데 60:40", PC, "-1200,200,2000,800", "L"),
    ("왼쪽|가운데 40:60", PC, "-800,200,2000,800", "C"),
    ("왼쪽|가운데 50:50", PC, "-1000,200,2000,800", "C"),
    ("왼쪽|가운데 1px 왼쪽", PC, "-1001,200,2000,800", "L"),
    ("왼쪽|가운데 1px 오른쪽", PC, "-999,200,2000,800", "C"),
    ("가운데|오른쪽 60:40", PC, "1408,300,1920,800", "C"),
    ("가운데|오른쪽 40:60", PC, "1792,300,1920,800", "R"),
    ("가운데|오른쪽 50:50", PC, "1600,300,1920,800", "C"),
    ("세 모니터 걸침 — 가운데 최대", PC, "-200,300,3000,800", "C"),
    # 겹침 없는 표본의 기대값은 이 PC 의 MonitorFromRect(NEAREST) 실측값이다(2026-11-04)
    ("세로 어긋남 — dy 36 < dx 40", PC, "2600,0,1000,200", "R"),
    ("근접 dx 40 = dy 40 동률", PC, "2600,-4,1000,200", "C"),
    ("근접 dy 6 < dx 40", PC, "2600,0,1000,230", "R"),
    ("근접 dx 90 > dy 16", PC, "2650,20,1000,200", "R"),
    ("왼쪽 위 빈 곳", PC, "-1000,-500,500,400", "L"),
    ("모니터 밖 왼쪽 — 가장 가까운 곳", PC, "-4000,300,800,600", "L"),
    ("모니터 밖 오른쪽 — 가장 가까운 곳", PC, "5000,500,800,600", "R"),
    ("위아래 60:40", STACK, "100,480,800,1000", "A"),
    ("위아래 40:60", STACK, "100,680,800,1000", "B"),
    ("위아래 50:50", STACK, "100,580,800,1000", "A"),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    r = Remote()
    if not r.call("ping"):
        print("연결 실패")
        return 2
    rows = []
    for name, mons, rect, expect in SAMPLES:
        res = r.call("monitor.pick", monitors=mons, rect=rect) or {}
        ok = res.get("owner") == expect
        rows.append({"case": name, "rect": rect, "expect": expect, "owner": res.get("owner"), "pass": ok})
        print("%-34s %-22s 기대 %s 결과 %s %s" % (name, rect, expect, res.get("owner"), "PASS" if ok else "FAIL"))
    # 후보 B: 위쪽 작업 표시줄(작업 영역 t=48)에서 최대화 SDI 는 제목 띠가 작업 영역 밖이어도 복구 대상이 아니다. 같은 사각형의 보통 창은 대상이다(대조)
    for name, maximized, expect in (("복구: 최대화 SDI, 위쪽 작업 표시줄", True, False), ("복구 대조: 같은 사각형 보통 SDI", False, True)):
        res = r.call("monitor.recoveryPick", work="0,48,2560,1392", rect="-8,-8,2576,1408", maximized=maximized) or {}
        ok = res.get("moved") is expect
        rows.append({"case": name, "expect": expect, "moved": res.get("moved"), "pass": ok})
        print("%-34s 기대 moved=%s 결과 %s %s" % (name, expect, res.get("moved"), "PASS" if ok else "FAIL"))
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)
    failed = sum(1 for x in rows if not x["pass"])
    print("합계 %d, 실패 %d" % (len(rows), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
