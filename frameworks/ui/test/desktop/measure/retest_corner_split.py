#!/usr/bin/env python3
# 패널 끝 확장(요건 3) 결함 재측정 — 문서 영역 분할 때 버튼 숨김·클릭 거절·해제 뒤 복귀, 낮은 창 최소 48dip, 머리줄 재사용 여부를 원격으로 재서 JSON 으로 낸다. 판정 결과 파일은 만들지 않는다(otest 소관)
"""
사용: PD_REMOTE_INFO=<격리 앱 연결 정보> python3 -B test/desktop/measure/retest_corner_split.py --out <결과.json> [--only docsplit,wrap,edgeguard,viewstate,minsize,reuse]
앱은 임시 --data-dir·--profile-dir 로 --remote-control 진단 빌드를 먼저 띄워 둔다(사용자 layout.json·settings.json 을 쓰지 않는다).
docsplit: 문서 영역을 Center 단과 같은 방향으로 나눠 단에 문서 칸이 2개인 상태 — 버튼 숨김, 클릭 Rejected, 단 방향 불변, 분할 해제 뒤 버튼 복귀, 복귀 뒤 클릭 동작.
wrap: 다른 방향 분할(새 ProportionalDock 으로 감싸짐, 단의 문서 칸 1개) — 버튼을 유지해도 문서 분할 방향이 돌아가지 않는다.
edgeguard: 모서리를 가져간 뒤 문서를 나눈 채 알림 가장자리를 옮기면 거절, 해제 뒤 허용.
viewstate: 로그 목록 선택·스크롤이 모서리 왕복에서 유지되는가.
minsize: 낮은 창에서 두꺼운 하단 패널이 모서리를 가져갈 때 줄어드는 좌 패널 높이가 48dip 이상인가.
reuse: 트리 조작 전후 ToolChromeControl 인스턴스 하나가 다른 패널 Id 를 맡는 경우(M-2)가 있는가. 값은 관측이며 없어도 '일어나지 않음'의 증명은 아니다.
종료 코드: 0=측정 완료(검사 전부 참), 1=검사 거짓 있음, 2=연결 실패.
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

L, B, N = "LeftPane", "BottomPane", "NotificationPane"
HOST = "DocumentsHost"  # 문서 분할은 이 칸 안에서만 일어난다(패널 단과 분리)
MIN_DIP = 48
CORNERS = ("TopLeft", "TopRight", "BottomLeft", "BottomRight")


def ensure_default(r):
    r.call("panel.dock", id=N, side="right")
    for corner in CORNERS:
        r.call("panel.corner.release", corner=corner)
    time.sleep(0.3)


def state(r):
    time.sleep(0.4)  # 버튼 갱신은 다음 UI 틱이다
    return r.call("panel.corner.state")


def button_ids(s):
    return sorted(b["id"] for b in (s or {}).get("buttons", []) if b["visible"])


def tree_of(s, pane):
    return ((s or {}).get("tree") or {}).get(pane) or {}


def summary(s):
    return {"owners": (s or {}).get("owners"), "buttons": button_ids(s), "center": tree_of(s, HOST)}


def doc_cells(s):
    return [c for c in tree_of(s, HOST).get("children", []) if isinstance(c, str) and c.startswith("Documents")]


def open_doc(r):
    r.call("docs.close", all="true")  # 앞 단계 잔여 문서·분할 칸 정리
    r.call("docs.open", kind="form", count=2)  # 문서 1개뿐인 칸은 자기 자신으로 나눌 수 없어 Dock 이 거절한다(실측 Rejected)
    time.sleep(0.4)
    return r.call("docs.list")[-1]["id"]


def close_docs(r, ids):
    for doc in ids:
        r.call("docs.close", doc=doc)
    time.sleep(0.5)


def docsplit(r, out):
    """I-3: 단과 같은 방향의 문서 분할."""
    ensure_default(r)
    doc = open_doc(r)
    before = state(r)
    r.check("docsplit.buttons_before", len(button_ids(before)) > 0, button_ids(before))
    orient = tree_of(before, HOST).get("orientation")
    op = "Bottom" if orient == "Vertical" else "Right"  # 문서 칸과 같은 방향이면 칸에 바로 들어간다
    # ① 누르지 않고 분할·해제만: 버튼이 사라졌다가 돌아오는가
    done = r.call("split.do", target="Documents", op=op, doc=doc)
    r.check("docsplit.created", (done or {}).get("status") == "Split", (done or {}).get("status"))
    split = state(r)
    r.check("docsplit.two_doc_cells_in_level", len(doc_cells(split)) == 2, tree_of(split, "CenterPane"))
    r.check("docsplit.buttons_hidden", button_ids(split) == [], button_ids(split))
    r.call("docs.close", doc=doc)
    released = state(r)
    r.check("docsplit.cell_collapsed", len(doc_cells(released)) == 1, tree_of(released, HOST))
    r.check("docsplit.buttons_back", button_ids(released) == button_ids(before), button_ids(released))
    # ② 분할한 채 원격 클릭·해제: 거절되고 방향이 그대로인가
    doc = open_doc(r)
    r.call("split.do", target="Documents", op=op, doc=doc)
    split2 = state(r)
    clicks = {c: (r.call("panel.corner.click", panel=B, corner=c) or {}).get("status") for c in ("BottomLeft", "BottomRight")}
    r.check("docsplit.click_rejected", all(v == "Rejected" for v in clicks.values()), clicks)
    after = state(r)
    r.check("docsplit.orientation_kept", tree_of(after, HOST).get("orientation") == orient,
            {"before": orient, "after": tree_of(after, HOST).get("orientation")})
    r.check("docsplit.owners_kept", (after or {}).get("owners") == (split2 or {}).get("owners"), (after or {}).get("owners"))
    # ③ 해제 뒤 다시 누를 수 있는가
    r.call("docs.close", doc=doc)
    again = state(r)
    click = (r.call("panel.corner.click", panel=B, corner="BottomLeft") or {}).get("status")
    r.check("docsplit.click_works_after", click == "Done", {"click": click, "tree": tree_of(again, HOST)})
    ensure_default(r)
    out["docsplit"] = {"before": summary(before), "split": summary(split), "released": summary(released), "clicks": clicks, "after": summary(after), "again": summary(again)}


def wrap(r, out):
    """다른 방향 분할은 감싸져 단의 칸 수가 그대로다 — 버튼을 눌러도 문서 분할 방향이 유지돼야 한다."""
    ensure_default(r)
    doc = open_doc(r)
    orient = tree_of(state(r), HOST).get("orientation")
    done = r.call("split.do", target="Documents", op="Right" if orient == "Vertical" else "Bottom", doc=doc)
    r.check("wrap.created", (done or {}).get("status") == "Split", (done or {}).get("status"))
    split = state(r)
    r.check("wrap.single_cell_in_level", sum(1 for c in tree_of(split, HOST).get("children", []) if c != "|") == 1, tree_of(split, HOST))
    r.call("panel.corner.click", panel=B, corner="BottomLeft")
    after = state(r)
    views = r.call("panel.corner.views") or {}
    cells = sorted(c["id"] for c in views.get("controls", []) if c["type"] == "DocumentDockControl" and c["id"].startswith("Documents"))
    r.check("wrap.doc_cells_kept", len(cells) == 2, cells)
    close_docs(r, [doc])
    ensure_default(r)
    out["wrap"] = {"split": summary(split), "after": summary(after), "cells": cells}


def minsize(r, out):
    """M-1: 낮은 창에서 두꺼운 하단 패널이 왼쪽 모서리를 가져갈 때 좌 패널 높이."""
    ensure_default(r)
    rows = []
    for win_h, share in ((420, 0.9), (360, 0.8), (720, 0.5)):
        r.call("window.resize", w=1100, h=win_h)
        time.sleep(0.6)
        r.call("panel.resize", id=B, ratio=share)
        time.sleep(0.5)
        before = r.call("panel.bounds") or {}
        click = (r.call("panel.corner.click", panel=B, corner="BottomLeft") or {}).get("status")
        time.sleep(0.6)
        after = r.call("panel.bounds") or {}
        row = {"windowH": win_h, "share": share, "click": click, "before": {k: before.get(k) for k in (L, B)}, "after": {k: after.get(k) for k in (L, B)}}
        rows.append(row)
        h = (after.get(L) or {}).get("h")
        name = "minsize.left_h_%d_%s" % (win_h, share)
        r.check(name, h is not None and h >= MIN_DIP - 1, row["after"])
        ensure_default(r)
        r.call("panel.resize", id=B, ratio=0.25)
    out["minsize"] = rows


def edgeguard(r, out):
    """자체 발견: 모서리를 가져간 뒤 문서를 나누고 알림 가장자리를 옮기면 트리가 깨졌다. 지금은 문서 칸이 분리돼 분할 중에도 옮겨지고 문서 칸이 그대로다. 옛 문구: 분할 중에는 거절되고 해제 뒤에는 옮겨져야 한다."""
    ensure_default(r)
    doc = open_doc(r)
    r.call("panel.corner.click", panel=B, corner="BottomLeft")
    r.call("split.do", target="Documents", op="Right", doc=doc)  # 가져간 뒤 Center 단은 Horizontal
    split = state(r)
    res = r.call("panel.dock", id=N, side="top") or {}
    r.check("edge.moved_while_split", res.get("status") == "Docked", res)  # 문서 칸이 패널 단과 분리돼 옮겨도 트리가 깨지지 않는다
    after = state(r)
    r.check("edge.doc_cells_kept", tree_of(after, HOST) == tree_of(split, HOST), tree_of(after, HOST))
    r.check("edge.owners_reset", not (after or {}).get("owners"), (after or {}).get("owners"))
    r.call("docs.close", doc=doc)
    state(r)
    res = r.call("panel.dock", id=N, side="right") or {}
    r.check("edge.moved_after_release", res.get("status") == "Docked", res)
    ensure_default(r)
    out["edgeguard"] = {"split": summary(split), "after": summary(after)}


def viewstate(r, out):
    """I-4: PanelViewState 이동 뒤에도 로그 목록 선택·스크롤이 왕복에서 유지되는가. 표본이 없으면(목록 1줄 이하·스크롤 범위 없음) 거짓으로 낸다."""
    ensure_default(r)
    r.call("panel.corner.content", pane=B, select=1, scrollY=38)
    time.sleep(0.3)
    before = r.call("panel.corner.content", pane=B) or {}
    r.check("view.sample_ok", (before.get("list") or {}).get("selected") == 1 and (before.get("scroll") or {}).get("y", 0) > 0, before)
    rows = []
    for corner, action in (("BottomLeft", "panel.corner.click"), ("BottomLeft", "panel.corner.release")):
        r.call(action, panel=B, corner=corner) if action.endswith("click") else r.call(action, corner=corner)
        time.sleep(0.8)  # 복원은 0·100·300ms 에 맞춘다
        now = r.call("panel.corner.content", pane=B) or {}
        rows.append(now)
        r.check("view.%s" % action.rsplit(".", 1)[-1], (now.get("list") or {}).get("selected") == 1 and (now.get("scroll") or {}).get("y") == before["scroll"]["y"], now)
    out["viewstate"] = {"before": before, "after": rows}


def chrome_ids(r):
    res = {}
    for c in (r.call("panel.corner.views") or {}).get("controls", []):
        if c["type"] == "ToolChromeControl":
            res[c["hash"]] = c["id"]
    return res


def reuse(r, out):
    """M-2: 같은 머리줄 인스턴스가 다른 패널 Id 를 맡는가. 트리를 흔드는 조작마다 인스턴스→Id 표를 모은다."""
    ensure_default(r)
    seen = {}
    log = []

    def snap(step):
        now = chrome_ids(r)
        log.append({"step": step, "chromes": now})
        for h, i in now.items():
            seen.setdefault(h, set()).add(i)

    snap("start")
    for corner in ("BottomLeft", "BottomRight"):
        r.call("panel.corner.click", panel=B, corner=corner)
        time.sleep(0.4)
        snap("claim " + corner)
    ensure_default(r)
    snap("released")
    for side in ("top", "left", "bottom", "right"):
        r.call("panel.dock", id=N, side=side)
        time.sleep(0.4)
        snap("edge " + side)
    for pane in (L, B):
        r.call("panel.pin", id=pane, on="true")
        time.sleep(0.4)
        snap("pin " + pane)
        r.call("panel.pin", id=pane, on="false")
        time.sleep(0.4)
        snap("unpin " + pane)
    doc = open_doc(r)
    done = r.call("split.do", target=L, op="Bottom", doc=doc)
    time.sleep(0.4)
    snap("split left " + str((done or {}).get("status")))
    close_docs(r, [doc])
    snap("split closed")
    ensure_default(r)
    reused = {str(h): sorted(ids) for h, ids in seen.items() if len(ids) > 1}
    r.check("reuse.observed", True, {"instances": len(seen), "reusedWithOtherId": reused})
    out["reuse"] = {"instances": len(seen), "reusedWithOtherId": reused, "steps": [x["step"] for x in log]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="docsplit,wrap,edgeguard,viewstate,minsize,reuse")
    args = ap.parse_args()
    if os.path.abspath(args.out).replace("\\", "/").find("/logs/ui28/") >= 0:
        sys.exit("logs/ui28 에는 쓰지 않는다(결과 파일은 otest 가 만든다)")
    try:
        r = Remote()
    except Exception as e:  # 연결 정보 없음·형식 오류
        print("연결 실패:", e)
        return 2
    out = {}
    steps = {"docsplit": docsplit, "wrap": wrap, "edgeguard": edgeguard, "viewstate": viewstate, "minsize": minsize, "reuse": reuse}
    for name in args.only.split(","):
        print("[%s]" % name)
        steps[name](r, out)
    out["checks"] = r.checks
    out["calls_failed"] = [c for c in r.calls if not c["ok"]]
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    failed = [c["name"] for c in r.checks if not c["pass"]]
    print("검사 %d건, 거짓 %d건 %s" % (len(r.checks), len(failed), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
