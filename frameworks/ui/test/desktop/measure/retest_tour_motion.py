#!/usr/bin/env python3
# AC-6-10 자동 부분 측정 — 투어 단계 이동 직후 구멍(SpotX/Y/W/H)을 읽고 0.5초 뒤 다시 읽어 전환(200ms 이동)이 있는지 값으로 본다
"""
사용: PD_REMOTE_INFO=<격리 앱 연결 정보> python3 -B retest_tour_motion.py --out <결과.json> [--expect reduced|normal]
앱은 임시 --data-dir·--profile-dir, 움직임 줄이기 회차는 --reduced-motion 으로 띄운다.
규칙(결과 보기 전에 고정): reduced 회차 — 오버레이 IsMotionReduced=true, Transitions 없음, 모든 단계 이동에서 '직후 좌표 == 0.5초 뒤 좌표'.
                         normal 회차(양성 대조) — IsMotionReduced=false 이고 적어도 한 단계 이동에서 '직후 좌표 != 0.5초 뒤 좌표'(전환이 실제로 있음).
종료 코드: 0=규칙 참, 1=위반, 2=연결 실패. logs/ui28/ 에는 쓰지 않는다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402

PROPS = "SpotX,SpotY,SpotWidth,SpotHeight,IsMotionReduced,Transitions"


def spot(r):
    n = r.call("ui.find", id="tour.overlay", props=PROPS)
    p = (n or {}).get("props") if isinstance(n, dict) else None
    return p


def key(p):
    return tuple(p.get(k) for k in ("SpotX", "SpotY", "SpotWidth", "SpotHeight")) if p else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--expect", choices=["reduced", "normal"], required=True)
    a = ap.parse_args()
    if os.path.abspath(a.out).replace("\\", "/").find("/logs/ui28/") >= 0 or not os.environ.get("PD_REMOTE_INFO"):
        sys.exit("logs/ui28 쓰기 금지 / PD_REMOTE_INFO 필수")
    r = Remote()
    if r.call("status") is None:
        return 2
    r.call("docs.open", kind="grid", title="motion-doc", allowDuplicate=True)
    time.sleep(0.5)
    r.call("tour.start")
    time.sleep(0.8)
    steps, bad = [], []
    first = spot(r)
    for i in range(8):
        before = spot(r)
        r.call("tour.next")
        right_after = spot(r)
        time.sleep(0.5)
        later = spot(r)
        steps.append({"i": i, "before": key(before), "right_after": key(right_after), "later": key(later), "moved": key(before) != key(later), "animated": key(right_after) != key(later)})
    r.call("tour.esc")
    props = first or {}
    moved_steps = [s for s in steps if s["moved"]]
    if a.expect == "reduced":
        if str(props.get("IsMotionReduced")).lower() != "true":  # ui.find props 값은 문자열이다
            bad.append("IsMotionReduced != true: %s" % props.get("IsMotionReduced"))
        if props.get("Transitions") not in (None, "", "null", []):
            bad.append("Transitions 가 있음: %s" % props.get("Transitions"))
        bad += ["단계 %d 에서 전환 발생: %s → %s" % (s["i"], s["right_after"], s["later"]) for s in steps if s["animated"]]
        if not moved_steps:
            bad.append("구멍이 이동한 단계가 없어 판정 불가")
    else:
        if str(props.get("IsMotionReduced")).lower() != "false":
            bad.append("IsMotionReduced != false: %s" % props.get("IsMotionReduced"))
        if not any(s["animated"] for s in steps):
            bad.append("양성 대조 실패: 일반 모드에서 전환이 관측되지 않음(호출 직후 값이 이미 최종값)")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"expect": a.expect, "first": props, "steps": steps, "violations": bad}, f, ensure_ascii=False, indent=1)
    print("expect=%s 위반 %d건 %s" % (a.expect, len(bad), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
