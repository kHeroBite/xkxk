#!/usr/bin/env python3
# 요건 4 SDI→MDI 위치 보존 측정 — 원격 명령만으로 재결합 전후 화면 사각형을 모아 JSON 에 저장한다(판정은 otest 가 결과 규칙으로 낸다)
"""
사용: python3 -B retest_redock_policy.py --out <logs/<에이전트>/<회차>/redock.json>
앱은 먼저 임시 폴더로 띄운다(화면 밖 시험 창이 사용자 화면으로 끌려오지 않게 override·가드 필수):
  run_desktop_bg.sh --conf Work --no-kill -- --remote-control --data-dir <임시> --profile-dir <임시>
      --bg-pos 21500,100 --primary-override 18000,0,6000,1440 --offscreen-guard on
격리되지 않은 앱에 붙으면 rc=3 으로 거부한다. logs/ui28/ 에는 쓰지 않는다(otest 전용).
종료 코드: 0=측정 완료, 2=연결 실패, 3=실행 거부.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

SETTLE_S = 0.6  # 재결합 뒤 배치·재보정(Loaded 2회)이 끝나기를 기다리는 시간
ROUND_TRIPS = 3


def doc_row(state, doc):
    return next((d for d in state["docs"] if d["id"] == doc), None)


def open_doc(r):
    before = {d["id"] for d in r.call("docs.list") or []}
    r.call("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(0.3)
    return [d["id"] for d in r.call("docs.list") or [] if d["id"] not in before][0]


def place_sdi(r, doc, area, ratio, height_inset=60):
    """SDI 보이는 프레임의 ratio 만큼만 MDI 영역에 들도록 오른쪽 경계에 걸쳐 놓고, 실제 프레임을 돌려준다."""
    frame = doc_row(r.call("placement.state"), doc)["sdiFrame"]
    target_x = area["x"] + area["w"] - round(frame["w"] * ratio)
    target_y = area["y"] + height_inset
    for _ in range(3):  # 창 사각형과 보이는 프레임의 여백은 앱만 안다 — 재서 맞춘다
        win = r.call("window.snapshot")["sdi"]
        row = next(s for s in win if s.get("doc") == doc)
        r.call("sdi.setBounds", doc=doc, x=row["x"] + target_x - frame["x"], y=row["y"] + target_y - frame["y"])
        time.sleep(0.2)
        frame = doc_row(r.call("placement.state"), doc)["sdiFrame"]
        if abs(frame["x"] - target_x) <= 1 and abs(frame["y"] - target_y) <= 1:
            break
    return frame


def measure_redock(r, doc, area, ratio):
    r.call("docs.sdi", doc=doc, on=True)
    time.sleep(SETTLE_S)
    frame = place_sdi(r, doc, area, ratio)
    r.call("docs.redock", doc=doc)
    time.sleep(SETTLE_S)
    row = doc_row(r.call("placement.state"), doc)
    return {"ratio": ratio, "frame": frame, "screen": row["screen"], "bounds": row["bounds"], "placed": row["placed"], "area": area}


def measure_hold(r, doc):
    """놓은 값 유지 → 메인 크기 변화에 그대로, 사용자 이동이면 해제."""
    state = r.call("placement.state")
    start = doc_row(state, doc)
    size = r.call("window.snapshot")["main"]
    r.call("placement.resizeMain", w=size["w"] + 40, h=size["h"])
    time.sleep(0.8)
    grown = doc_row(r.call("placement.state"), doc)
    r.call("placement.resizeMain", w=size["w"], h=size["h"])
    time.sleep(0.8)
    back = doc_row(r.call("placement.state"), doc)
    r.call("docs.move", doc=doc, dx=-20, dy=0, steps=2)
    time.sleep(0.3)
    moved = doc_row(r.call("placement.state"), doc)
    return {"start": start, "after_grow": grown, "after_back": back, "after_user_move": moved}


def measure_round_trips(r, doc, area):
    measure_redock(r, doc, area, 0.6)
    rects = [doc_row(r.call("placement.state"), doc)["screen"]]
    for _ in range(ROUND_TRIPS):
        r.call("docs.sdi", doc=doc, on=True)
        time.sleep(SETTLE_S)
        r.call("docs.redock", doc=doc)
        time.sleep(SETTLE_S)
        rects.append(doc_row(r.call("placement.state"), doc)["screen"])
    return rects


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    args = p.parse_args()
    if os.path.normpath(args.out).replace("\\", "/").find("logs/ui28/") >= 0:
        print("logs/ui28/ 는 otest 결과 전용이다 — 원시 측정은 logs/<에이전트>/<회차>/ 에 쓴다.")
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    iso = retest_user_files.isolation(status.get("pid"))
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3

    r.call("placement.force", on=True)
    raw = {"pid": status.get("pid"), "selftest": r.call("placement.selftest")}
    anchor = open_doc(r)  # 추정 기준이 될 다른 MDI 창
    doc = open_doc(r)
    r.call("docs.activate", doc=doc)
    time.sleep(0.3)
    area = r.call("placement.state")["area"]
    raw["keep_60"] = measure_redock(r, doc, area, 0.6)
    raw["hold"] = measure_hold(r, doc)
    raw["clamp_40"] = measure_redock(r, doc, area, 0.4)
    raw["round_trips"] = measure_round_trips(r, doc, area)
    raw["anchor"] = anchor

    r.call("placement.force", on=False)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
