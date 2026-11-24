#!/usr/bin/env python3
# 옛 레이아웃 Version 3 읽기 고정 시험 — 고정 표본(testdata/layout_v3_right.json)의 알림 위치 네 곳·분할·모서리 소유를 임시 프로필에 넣어 띄우고 복원 값과 저장 Version 을 잰다
"""
사용: python3 -B retest_layout_v3_read.py --run-dir <bin/run/<복사본>/net10.0> --work logs/<이름>/<회차>
앱을 경우마다 새로 띄운다(화면 밖·임시 폴더·자기 PID 종료). 탐색기·로그 가장자리 값이 없는 파일은 왼쪽·아래로 읽어야 한다.
결과는 layout_v3.json. 종료 코드 0=전부 통과, 1=실패 있음, 4=가드 중단.
"""
import argparse
import copy
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import mdi_drag_app as app  # noqa: E402

SAMPLE = os.path.join(HERE, "..", "..", "common", "testdata", "layout_v3_right.json")
LP, BP, NP = "LeftPane", "BottomPane", "NotificationPane"


def cases(base):
    for edge in ("Right", "Left", "Top", "Bottom"):
        d = copy.deepcopy(base)
        d["Panels"]["NotificationEdge"] = edge
        yield "edge." + edge, d, {"sides": {LP: "Left", BP: "Bottom", NP: edge}}
    d = copy.deepcopy(base)
    d["Panels"]["Tabs"] = [
        {"PanelId": "LeftPane.Tabs", "ToolIds": ["Explorer"], "ActiveToolId": "Explorer", "Area": "Left", "Index": 0, "Proportion": 0.5},
        {"PanelId": "LeftPane.2.Tabs", "ToolIds": ["Log"], "ActiveToolId": "Log", "Area": "Left", "Index": 1, "Proportion": 0.5},
    ]
    yield "split.left", d, {"tabs": {"LeftPane.2": ["Log"]}, "sides": {LP: "Left", "LeftPane.2": "Left", NP: "Right"}}
    d = copy.deepcopy(base)
    d["Panels"]["Tabs"] = [{"PanelId": "NotificationPane", "ToolIds": [], "ActiveToolId": None, "Area": "Right", "Index": 0, "Proportion": 0.5},
                           {"PanelId": "RightPane.2.Tabs", "ToolIds": ["Log"], "ActiveToolId": "Log", "Area": "Right", "Index": 1, "Proportion": 0.5}]
    yield "split.rightMarker", d, {"tabs": {"RightPane.2": ["Log"], NP: ["Notifications"]}, "sides": {NP: "Right", "RightPane.2": "Right"}}
    d = copy.deepcopy(base)
    d["Panels"]["Tabs"] = [{"PanelId": "NotificationPane.Tabs", "ToolIds": ["Notifications", "Log"], "ActiveToolId": "Notifications", "Area": None, "Index": None, "Proportion": None},
                           {"PanelId": "LeftPane.Tabs", "ToolIds": ["Explorer"], "ActiveToolId": "Explorer", "Area": None, "Index": None, "Proportion": None}]
    yield "joined.notify", d, {"tabs": {NP: ["Notifications", "Log"]}}
    d = copy.deepcopy(base)
    d["Panels"]["cornerOwners"] = {"BottomLeft": "BottomPane"}
    yield "corner.owner", d, {"owners": {"BottomLeft": "BottomPane"}}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--work", required=True)
    args = ap.parse_args()
    os.makedirs(args.work, exist_ok=True)
    from retest_remote import Remote
    from offscreen_guard import OffscreenGuard, GuardBreach
    with open(SAMPLE, encoding="utf-8-sig") as f:
        base = json.load(f)
    items, rc = [], 0
    for name, doc, want in cases(base):
        pid, info, _ = app.launch(args.run_dir, args.work, seed_layout=json.dumps(doc, ensure_ascii=False))
        r = Remote()
        try:
            with OffscreenGuard(pid) as guard:
                time.sleep(1.0)
                guard.check()
                now = {p["id"]: p for p in r.call("panel.list") or []}
                got = {"sides": {i: p.get("side") for i, p in now.items()}, "tabs": {i: [t["id"] for t in p.get("tabs") or []] for i, p in now.items()}}
                ok = all(got["sides"].get(k) == v for k, v in want.get("sides", {}).items())
                ok = ok and all(got["tabs"].get(k) == v for k, v in want.get("tabs", {}).items())
                if "owners" in want:
                    got["owners"] = (r.call("panel.corner.state") or {}).get("owners")
                    ok = ok and got["owners"] == want["owners"]
                saved = r.call("split.capture") or {}
                got["capture"] = {"notificationEdge": saved.get("notificationEdge"), "explorerProportion": saved.get("explorerProportion")}
                guard.check()
        except GuardBreach as e:
            items.append({"name": name, "pass": False, "detail": str(e)})
            rc = 4
            break
        finally:
            print(app.stop(pid, os.path.basename(os.path.dirname(info))), flush=True)
        items.append({"name": name, "pass": bool(ok), "detail": got})
        print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(got, ensure_ascii=False)[:300], flush=True)
    out = {"runDir": args.run_dir, "items": items, "passed": sum(i["pass"] for i in items), "total": len(items)}
    with open(os.path.join(args.work, "layout_v3.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("결과 %d/%d" % (out["passed"], out["total"]))
    return rc or (0 if out["passed"] == out["total"] else 1)


if __name__ == "__main__":
    sys.exit(main())
