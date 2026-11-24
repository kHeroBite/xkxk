#!/usr/bin/env python3
# AC-3-07 보강 측정 — 알림 패널을 4방향으로 옮기며 패널 모서리 버튼의 글리프·툴팁·초점 속성을 전수 수집한다(판정은 judge 의 glyph 규칙이 아니라 이 스크립트 말미 rules() — 결과 보기 전에 고정)
"""
사용: PD_REMOTE_INFO=<격리 앱 연결 정보> python3 -B retest_panel_glyphs.py --out <결과.json>
규칙(고정): 변이 left/right 인 패널(세로로 서는 패널)의 버튼 글리프는 ∧ 또는 ∨, 변이 top/bottom 인 패널의 버튼 글리프는 < 또는 >.
          모든 버튼은 툴팁이 비어 있지 않고 focusable·tabStop 이 참. 각 방향에서 버튼이 1개 이상 나타나야 한다.
종료 코드: 0=규칙 전부 참, 1=위반 있음, 2=연결 실패. logs/ui28/ 에는 쓰지 않는다.
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

N = "NotificationPane"
VERT = {"left", "right"}


def pane_edges(state):
    return {p["id"]: str(p["edge"]).lower() for p in state.get("placements", [])}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    if os.path.abspath(args.out).replace("\\", "/").find("/logs/ui28/") >= 0:
        sys.exit("logs/ui28 에는 쓰지 않는다")
    if not os.environ.get("PD_REMOTE_INFO"):
        sys.exit("PD_REMOTE_INFO 필수")
    r = Remote()
    if r.call("status") is None:
        return 2
    rows, bad = [], []
    for side in ("right", "top", "bottom", "left"):
        r.call("panel.dock", id=N, side=side)
        time.sleep(0.6)
        st = r.call("panel.corner.state")
        edges = pane_edges(st)
        vis = [b for b in st["buttons"] if b["visible"]]
        if not vis:
            bad.append("%s: 버튼 없음" % side)
        for b in vis:
            pane = b["id"].split(".")[2]
            edge = edges.get(pane, "?")
            ok_glyph = (b["glyph"] in ("∧", "∨")) if edge in VERT else (b["glyph"] in ("<", ">"))
            rows.append({"notify_side": side, "id": b["id"], "pane": pane, "edge": edge, "glyph": b["glyph"], "tip": b["tip"],
                         "focusable": b["focusable"], "tabStop": b["tabStop"], "glyph_ok": ok_glyph})
            if not ok_glyph:
                bad.append("%s %s edge=%s glyph=%s" % (side, b["id"], edge, b["glyph"]))
            if not b["tip"] or not (b["focusable"] and b["tabStop"]):
                bad.append("%s %s tip/focus 속성 이상 %s" % (side, b["id"], {k: b[k] for k in ("tip", "focusable", "tabStop")}))
    r.call("panel.dock", id=N, side="right")
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    out = {"rows": rows, "violations": bad, "glyphs_seen": sorted({x["glyph"] for x in rows})}
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("버튼 %d개, 글리프 %s, 위반 %d건 %s" % (len(rows), out["glyphs_seen"], len(bad), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
