#!/usr/bin/env python3
# 패널 제목 줄 끌기 회귀 측정 — 패널마다 제목 줄을 L2 창 메시지로 끌어 놓고, 끌기 시작(미리 보기 창)·놓은 뒤 위치·탭과 '패널을 잃지 않음' 불변식을 값으로 남긴다
"""
사용: python3 -B retest_panel_title_drag.py --run-dir <bin/run/<복사본>/net10.0> --work logs/<이름>/<회차> [--parts matrix,safety] [--panes a,b] [--targets a,b] [--mid-shot]
bin/run 복사본을 임시 폴더·화면 밖으로 직접 띄우고(mdi_drag_app) 자기 PID 만 종료한다. 조작마다 OffscreenGuard 를 확인한다.
끌기는 PostMessage(post_mouse.ps1)라 커서·전경을 건드리지 않는다. 실제 마우스 체감은 사람 확인 항목이다.
대상: cross.{top,bottom,left,right}=문서 영역 가운데 안내의 네 방향, docs=문서 영역 가운데, body.<패널>=다른 패널 본문 가운데, title.<패널>=다른 패널 제목 줄.
safety(D-PANELLOST 회귀): 제자리 놓기, 창 밖 놓기, 마지막 남은 패널, 문서 분할 중, 패널끼리 합친 뒤 모든 도구가 보이는 패널에 있고 메뉴가 열리는지.
결과는 title_drag.json. 종료 코드 0=전부 통과, 1=실패 있음, 4=가드 중단.
"""
import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import mdi_drag_app as app  # noqa: E402
import panel_title_drag_resize as extra  # noqa: E402

LP, BP, NP = "LeftPane", "BottomPane", "NotificationPane"
PANES = [LP, BP, NP]
HOME = {LP: ("Explorer", "Left"), BP: ("Log", "Bottom"), NP: ("Notifications", "Right")}
CROSS = {"cross.top": ("Top", 0, -1), "cross.bottom": ("Bottom", 0, 1), "cross.left": ("Left", -1, 0), "cross.right": ("Right", 1, 0)}
CROSS_OFFSET = 50  # Dock 안내 십자 가운데에서 방향 칸까지(dip)
SETTLE_S = 0.8


def panels(r):
    return {p["id"]: p for p in r.call("panel.list") or []}


def tabs_of(p):
    return [t["id"] for t in p.get("tabs") or []]


def pane_bounds(r, pane):
    return (r.call("ui.find", dock=pane) or {}).get("bounds")


def title_point(b):
    return b["x"] + 40, b["y"] + 12


def target_point(r, target):
    if target.startswith(("body.", "title.")):
        kind, pane = target.split(".", 1)
        b = pane_bounds(r, pane)
        if not b:
            return None
        return title_point(b) if kind == "title" else (b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
    d = (r.call("panel.bounds") or {}).get("Documents")
    if not d:
        return None
    cx, cy = d["x"] + d["w"] / 2, d["y"] + d["h"] / 2
    if target == "docs":
        return cx, cy
    _, dx, dy = CROSS[target]
    return cx + dx * CROSS_OFFSET, cy + dy * CROSS_OFFSET


def post(hwnd, frm, to, *flags):
    tool = app.win(os.path.join(HERE, "post_mouse.ps1"))
    return subprocess.run([app.PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tool, "-Hwnd", str(hwnd), "-X1", str(int(frm[0])), "-Y1", str(int(frm[1])),
                           "-X2", str(int(to[0])), "-Y2", str(int(to[1])), "-Steps", "14", "-DelayMs", "15", *flags],
                          capture_output=True, encoding="utf-8", errors="replace", timeout=60).stdout.strip()


def own_windows(pid):
    tool = app.win(os.path.join(HERE, "win_owner_z.ps1"))
    text = subprocess.run([app.PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tool, "-Pid", str(pid), "-ArrayOnly"],
                          capture_output=True, encoding="utf-8", errors="replace", timeout=40).stdout
    try:
        return json.loads(text)
    except ValueError:
        return []


class Run:
    def __init__(self, run_dir, work):
        self.run_dir, self.work, self.pid, self.items = run_dir, work, None, []

    def start(self):
        from retest_remote import Remote
        from offscreen_guard import OffscreenGuard
        self.pid, self.info, self.log = app.launch(self.run_dir, self.work)
        self.r = Remote()
        self.guard = OffscreenGuard(self.pid)
        self.guard.__enter__()
        self.hwnd = app.main_hwnd(self.pid)
        time.sleep(1.0)
        self.base = panels(self.r)

    def stop(self):
        if self.pid:
            self.guard.__exit__(None, None, None)
            print(app.stop(self.pid, os.path.basename(os.path.dirname(self.info))), flush=True)
            self.pid = None

    def check(self, name, ok, detail=None):
        self.items.append({"name": name, "pass": bool(ok), "detail": detail})
        print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:260], flush=True)

    def drag(self, frm, to, shot=None):
        """누르고 끌어 끝점에서 잠시 머문 뒤 놓는다. (끌기 시작 여부, 미리 보기 포함 창 수) 를 돌려준다."""
        self.guard.check()
        base = len(own_windows(self.pid))
        post(self.hwnd, frm, to, "-NoUp", "-HoldMoves", "3")
        time.sleep(0.25)
        mid = len(own_windows(self.pid))
        if shot:
            self.r.call("screenshot", path=app.win(os.path.abspath(os.path.join(self.work, shot))))
        self.guard.check()
        post(self.hwnd, to, to, "-UpOnly")
        time.sleep(SETTLE_S)
        self.guard.check()
        return mid > base

    def invariant(self, name):
        """D-PANELLOST: 세 도구가 보이는 패널이나 문서 영역에 남아 있고, 보이는 패널마다 메뉴가 열리고, 빈 패널이 없다."""
        now = panels(self.r)
        where = {tool: [i for i, p in now.items() if tool in tabs_of(p) and p.get("visible")] for tool, _ in HOME.values()}
        docs = [d["id"] for d in self.r.call("docs.list") or []]
        lost = [t for t, at in where.items() if not at and t not in docs]
        menus = {i: (self.r.call("panelmenu.read", pane=i) or {}).get("visible") for i, p in now.items() if p.get("visible")}
        empty = [i for i, p in now.items() if p.get("visible") and not tabs_of(p)]
        self.check(name, not lost and all(menus.values()) and not empty, {"where": where, "lost": lost, "menus": menus, "empty": empty})

    def reset(self):
        """시작 배치로 되돌린다. 안 되면 새로 띄운다."""
        for pane, (tool, side) in HOME.items():
            if tool not in tabs_of(panels(self.r).get(pane, {})):
                self.r.call("panelmenu.click", tab=tool, target=pane)
                time.sleep(0.4)
            self.r.call("panel.dock", id=pane, side=side)
        time.sleep(0.5)
        now = panels(self.r)
        same = all(tabs_of(now.get(p, {})) == tabs_of(self.base[p]) and now.get(p, {}).get("side") == self.base[p]["side"] for p in PANES) and set(now) == set(self.base)
        if not same:
            print("초기 배치로 못 돌아옴 — 재기동", json.dumps({i: (p["side"], tabs_of(p)) for i, p in now.items()}, ensure_ascii=False), flush=True)
            self.stop()
            self.start()


def matrix(run, panes, targets, mid_shot):
    for pane in panes:
        tool, home = HOME[pane]
        others = [p for p in PANES if p != pane]
        for target in targets or list(CROSS) + ["docs"] + ["body." + p for p in others] + ["title." + p for p in others]:
            b = pane_bounds(run.r, pane)
            to = target_point(run.r, target)
            started = run.drag(title_point(b), to, "%s_%s_mid.png" % (pane, target) if mid_shot else None)
            now = panels(run.r)
            name = "%s>%s" % (pane, target)
            if target in CROSS:
                want = CROSS[target][0]
                run.check("drag." + name, started and now[pane]["side"] == want, {"started": started, "side": now[pane]["side"], "want": want})
            elif target == "docs":
                run.check("drag." + name, started and now[pane]["side"] == home and tool in tabs_of(now[pane]), {"started": started, "side": now[pane]["side"], "note": "도구 패널은 문서 영역 가운데로 들어가지 않는다"})
            else:
                other = target.split(".", 1)[1]
                run.check("drag." + name, started and tool in tabs_of(now[other]), {"started": started, "otherTabs": tabs_of(now[other])})
            run.invariant("keep." + name)
            run.reset()


def safety(run):
    # 제자리 놓기(자기 제목 줄 안에서 작게 끌기)
    for pane in PANES:
        b = pane_bounds(run.r, pane)
        frm = title_point(b)
        run.drag(frm, (frm[0] + 30, frm[1]))
        now = panels(run.r)
        run.check("safe.self." + pane, now[pane]["side"] == HOME[pane][1] and HOME[pane][0] in tabs_of(now[pane]), now[pane]["side"])
        run.invariant("keep.self." + pane)
    # 창 밖에 놓기(캡처 상태로 클라이언트 밖 좌표)
    for pane in PANES:
        b = pane_bounds(run.r, pane)
        run.drag(title_point(b), (-60, -60))
        now = panels(run.r)
        run.check("safe.outside." + pane, now[pane]["side"] == HOME[pane][1] and HOME[pane][0] in tabs_of(now[pane]), now[pane]["side"])
        run.invariant("keep.outside." + pane)
    run.reset()
    # 마지막 남은 패널: 모두 합친 뒤 그 패널을 옮긴다
    run.r.call("panelmenu.click", pane=LP, target=BP)
    time.sleep(0.5)
    run.r.call("panelmenu.click", pane=NP, target=BP)
    time.sleep(0.5)
    run.invariant("keep.merged")
    b = pane_bounds(run.r, BP)
    run.drag(title_point(b), target_point(run.r, "cross.left"))
    now = panels(run.r)
    run.check("safe.lastPanel", now[BP]["side"] == "Left" and len(tabs_of(now[BP])) == 3, {"side": now[BP]["side"], "tabs": tabs_of(now[BP])})
    run.invariant("keep.lastPanel")
    run.reset()
    # 문서 분할 중
    run.r.call("docs.open", kind="form", count=2)
    time.sleep(0.5)
    doc = run.r.call("docs.list")[-1]["id"]
    run.r.call("split.do", target="Documents", op="Bottom", doc=doc)
    time.sleep(0.5)
    for pane, target, want in ((NP, "cross.top", "Top"), (LP, "cross.bottom", "Bottom")):
        b = pane_bounds(run.r, pane)
        run.drag(title_point(b), target_point(run.r, target))
        now = panels(run.r)
        run.check("safe.docSplit." + pane, now[pane]["side"] == want, {"side": now[pane]["side"], "want": want})
        run.invariant("keep.docSplit." + pane)
    state = run.r.call("panel.corner.state") or {}
    host = (state.get("tree") or {}).get("DocumentsHost") or {}
    run.check("safe.docSplit.cellsKept", sum(1 for c in host.get("children", []) if c != "|") == 2, host)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--parts", default="matrix,safety,resize,focus")
    ap.add_argument("--panes", default=",".join(PANES))
    ap.add_argument("--targets", default="")
    ap.add_argument("--mid-shot", action="store_true", help="끌기 도중 캡처를 남긴다(안내 위치 확인용)")
    args = ap.parse_args()
    os.makedirs(args.work, exist_ok=True)
    from offscreen_guard import GuardBreach
    run = Run(args.run_dir, args.work)
    out = {"runDir": args.run_dir}
    rc = 0
    try:
        run.start()
        if "matrix" in args.parts:
            matrix(run, args.panes.split(","), [t for t in args.targets.split(",") if t], args.mid_shot)
        if "safety" in args.parts:
            run.reset()
            safety(run)
        if "resize" in args.parts:
            run.reset()
            extra.resize(run, sys.modules[__name__])
        if "focus" in args.parts:
            run.reset()
            extra.focus(run, sys.modules[__name__])
        out["guard"] = run.guard.summary()
    except GuardBreach as e:
        out["breach"] = str(e)
        rc = 4
    finally:
        run.stop()
        out["items"] = run.items
        out["passed"], out["total"] = sum(i["pass"] for i in run.items), len(run.items)
        with open(os.path.join(args.work, "title_drag.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    print("결과 %d/%d" % (out["passed"], out["total"]))
    return rc or (0 if out["passed"] == out["total"] else 1)


if __name__ == "__main__":
    sys.exit(main())
