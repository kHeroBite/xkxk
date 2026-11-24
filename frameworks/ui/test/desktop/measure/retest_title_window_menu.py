#!/usr/bin/env python3
# 창 메뉴(메인·SDI·MDI 제목 줄과 제목 탭) 회귀 측정 — 상태별 활성, 기본 위치 이동, 화면 밖 창 복귀, 비클라이언트 우클릭 메시지 경로를 값으로 남긴다
"""
사용: PD_REMOTE_INFO=<임시 data>/remote-control.json python3 -B retest_title_window_menu.py --stage a|load|sdi|main|popup|nc --out logs/odev-fix-winmenu/<회차>/<이름>.json
앱은 먼저 임시 폴더·화면 밖으로 띄운다:
  run_desktop_bg.sh --conf run/<복사본> --no-kill -- --remote-control --multi-instance on --data-dir <임시> --profile-dir <임시>
      --bg-pos 21500,100 --primary-override 18000,0,6000,1440 --offscreen-guard on
단계:
  a = OS 창 상태 불변: selftest, 상태별 활성, MDI·탭 동작, 기본 위치, 메뉴 항목 읽기(팝업 없음).
  load = 팝업 없이 런타임 로딩: 네 메뉴의 인스턴스·항목. sdi = 화면 밖 SDI 복귀. main = 메인 창 기본 위치. sdi·main 은 override 영역 밖이면 자기 PID 를 종료한다.
  popup·nc = 사용자 부재 승인 필요(--user-away-approved 없으면 rc=3, 앱에 연결하지 않는다): 팝업 열기, WM_NCRBUTTONUP 메시지 전달.
메인 창의 최소화·최대화 실행은 어느 단계에서도 하지 않는다(화면 밖 고정이 풀릴 수 있다). 활성 규칙만 본다.
우클릭은 실제 포인터 입력이 아니다. 사람 확인 항목으로 남긴다. 격리되지 않은 앱은 rc=3 으로 거부하고, 겹침·전경은 GuardBreach 로 즉시 멈춘다.
종료 코드: 0=전부 통과, 1=실패 있음, 2=연결 실패, 3=실행 거부, 4=가드 중단.
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
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402
from offscreen_guard import OffscreenGuard, GuardBreach  # noqa: E402
from retest_title_window_menu_win import stage_popup, stage_nc  # noqa: E402

SETTLE_S = 0.5
AREA = (18000, 0, 6000, 1440)  # --primary-override
FAR = 30000


class Run:
    def __init__(self, r, guard, pid):
        self.r, self.guard, self.pid = r, guard, pid
        self.items = []

    def call(self, cmd, **kw):
        self.guard.check()
        res = self.r.call(cmd, **kw)
        return res

    def check(self, name, passed, detail=None):
        self.items.append({"name": name, "pass": bool(passed), "detail": detail})
        print(("PASS " if passed else "FAIL ") + name + ("  " + json.dumps(detail, ensure_ascii=False)[:220] if detail is not None else ""))

    def open_doc(self):
        before = {d["id"] for d in self.call("docs.list") or []}
        self.call("docs.open", kind="grid", allowDuplicate=True)
        time.sleep(0.3)
        return [d["id"] for d in self.call("docs.list") or [] if d["id"] not in before][0]

    def row(self, doc):
        return next(d for d in self.call("placement.state")["docs"] if d["id"] == doc)

    def state(self, kind, doc=None):
        return self.call("winmenu.state", kind=kind, doc=doc)

    def run(self, kind, doc, action):
        res = self.call("winmenu.run", kind=kind, doc=doc, action=action)
        time.sleep(SETTLE_S)
        return (res or {}).get("done")

    def active(self):
        return next((d["id"] for d in self.call("docs.list") or [] if d.get("active")), None)


def flags(s):
    return (s["restore"], s["minimize"], s["maximize"], s["move"], s["close"])


def stage_a(t):
    self_test = t.call("winmenu.selftest")
    t.check("selftest", self_test and self_test["passed"] == self_test["total"], {"passed": self_test and self_test["passed"], "total": self_test and self_test["total"], "failed": [c for c in (self_test or {}).get("cases", []) if not c["pass"]]})
    t.check("main.normal.flags", flags(t.state("main")) == (False, True, True, True, True), t.state("main"))

    a, b, c = t.open_doc(), t.open_doc(), t.open_doc()
    for kind in ("mdi", "tab"):
        t.check(kind + ".normal.flags", flags(t.state(kind, a)) == (False, True, True, True, True), t.state(kind, a))
        t.check(kind + ".maximize.done", t.run(kind, a, "maximize") is True)
        t.check(kind + ".maximized.flags", flags(t.state(kind, a)) == (True, True, False, True, True), t.state(kind, a))
        t.check(kind + ".maximize.again.refused", t.run(kind, a, "maximize") is False)
        t.check(kind + ".restore.done", t.run(kind, a, "restore") is True and t.row(a)["state"] == "Normal", t.row(a)["state"])
        t.check(kind + ".minimize.done", t.run(kind, a, "minimize") is True and t.row(a)["state"] == "Minimized", t.row(a)["state"])
        t.check(kind + ".minimized.flags", flags(t.state(kind, a)) == (True, False, True, True, True), t.state(kind, a))
        t.check(kind + ".restore.from.min", t.run(kind, a, "restore") is True and t.row(a)["state"] == "Normal" and t.active() == a, {"state": t.row(a)["state"], "active": t.active()})

    # 영역 밖 MDI 창을 탭에서 불러온다(대상만 이동·활성, 다른 창 불변, 크기 유지)
    t.call("winmenu.noteTab", doc=b)
    t.call("placement.markPlaced", doc=b, x=5000, y=5000, w=400, h=300)
    t.call("docs.activate", doc=c)
    time.sleep(SETTLE_S)
    others = {d: t.row(d)["bounds"] for d in (a, c)}
    moved = t.run("tab", b, "movedefault")
    after = t.row(b)
    t.check("mdi.tab.moveDefault", moved is True and after["bounds"]["x"] < 5000 and after["bounds"]["y"] < 5000 and after["bounds"]["w"] == 400 and after["bounds"]["h"] == 300, after["bounds"])
    t.check("mdi.tab.moveDefault.active", t.active() == b, t.active())
    t.check("mdi.tab.moveDefault.others", all(t.row(d)["bounds"] == v for d, v in others.items()), others)
    area = t.call("placement.state")["area"]
    t.check("mdi.tab.moveDefault.inArea", after["bounds"]["x"] + 120 <= area["dipW"] and after["bounds"]["y"] + 16 <= area["dipH"], {"bounds": after["bounds"], "area": area})
    first = after["bounds"]
    t.run("mdi", b, "movedefault")
    t.check("mdi.moveDefault.idempotent", t.row(b)["bounds"] == first, t.row(b)["bounds"])

    # 최대화·최소화 창의 기본 위치 이동은 복원을 함께 한다
    for state in ("maximized", "minimized"):
        t.call("docs.state", doc=b, state=state)
        time.sleep(SETTLE_S)
        t.run("mdi", b, "movedefault")
        t.check("mdi.moveDefault.from." + state, t.row(b)["state"] == "Normal" and t.active() == b, t.row(b)["state"])

    # 위치 고정 창: 기본 위치 이동은 허용, 고정 상태 유지
    t.call("chrome.lock", doc=c)
    t.call("placement.markPlaced", doc=c, x=5000, y=5000, w=400, h=300)
    time.sleep(SETTLE_S)
    t.run("mdi", c, "movedefault")
    t.check("mdi.locked.moves.keepsLock", t.row(c)["bounds"]["x"] < 5000 and t.row(c)["locked"] is True, t.row(c))
    t.call("chrome.lock", doc=c)

    menus = {h: t.call("ui.contextMenu", id=h) for h in ("shell.tabStrip", "SystemMenu")}
    ids = {h: [i.get("id") for i in (m or {}).get("items", []) if i.get("id")] for h, m in menus.items()}
    t.check("menu.tab.ids", all(x in ids["shell.tabStrip"] for x in ("shell.ctxRestore", "shell.ctxMinimize", "shell.ctxMaximize", "shell.ctxMoveDefault", "shell.ctxClose", "shell.ctxRename")), ids["shell.tabStrip"])
    t.check("menu.main.ids", all(x in ids["SystemMenu"] for x in ("shell.sysRestore", "shell.sysMinimize", "shell.sysMaximize", "shell.sysMoveDefault", "shell.sysClose")), ids["SystemMenu"])

    # 닫기(대상 문서만)
    t.check("mdi.close", t.run("mdi", a, "close") is True and a not in [d["id"] for d in t.call("docs.list")], a)

    # 끌기 빈칸·로고의 실제 판정값(읽기 전용 메시지). 빈칸이 HTCAPTION 이면 OS 메뉴 경로라는 뜻이다
    snap = t.call("window.snapshot")["main"]
    drag = t.call("ui.find", id="shell.titleDragArea")
    logo = t.call("ui.find", id="shell.titleLogo")
    out = {"drag": drag and drag.get("bounds"), "logo": logo and logo.get("bounds"), "main": snap}
    t.check("hit.collected", bool(drag and logo), out)
    return {"docs": [a, b, c], "hit": out}


def in_area(x, y):
    return AREA[0] <= x < AREA[0] + AREA[2] and AREA[1] <= y < AREA[1] + AREA[3]


def stop_if_outside(t, name, x, y):
    """override 영역 밖이면 자기 PID 를 바로 끝낸다(team-lead 조건). 사용자 모니터와의 겹침은 가드가 따로 본다."""
    if not in_area(x, y):
        subprocess.run(["taskkill.exe", "/PID", str(t.pid), "/F"], capture_output=True, timeout=30)
        raise GuardBreach("%s: override 영역 밖 (%s,%s) — PID %s 종료" % (name, x, y, t.pid))


def stage_load(t):
    """팝업 없이 런타임 로딩 확인: 네 메뉴의 ContextMenu 인스턴스·항목(첨부 속성이 만든 항목)을 읽는다. SDI 창이 있어야 해서 하나 띄운다."""
    a, b = t.open_doc(), t.open_doc()
    t.call("docs.sdi", doc=b, on=True)
    time.sleep(0.6)
    expect = {"SystemMenu": "shell.sys", "shell.tabStrip": "shell.ctx", "SdiSystemMenu": "chrome.sdiSys"}
    for host, prefix in expect.items():
        menu = t.call("ui.contextMenu", id=host) or {}
        ids = [i.get("id") for i in menu.get("items", []) if i.get("id")]
        want = [prefix + s for s in ("Restore", "Minimize", "Maximize", "MoveDefault", "Close")]
        t.check("load." + host, ids[:5] == want and menu.get("isOpen") is False, {"ids": ids, "headers": [i.get("header") for i in menu.get("items", []) if i.get("id")][:5]})
    mdi = t.call("dock.menu", doc=a) or {}
    headers = [i.get("header") for i in mdi.get("items", [])]
    wanted = ("이전 크기로", "최소화", "최대화", "기본 위치로 이동", "닫기")
    t.check("load.mdi.common", all(any(str(h).startswith(w) for h in headers[:5]) for w in wanted), headers[:12])
    t.check("load.mdi.oneClose", sum(1 for h in headers if str(h).startswith("닫기")) == 1, headers)
    return {"docs": [a, b]}


def stage_sdi(t):
    """화면 밖 SDI 를 탭 메뉴 경로로 불러온다(override 영역 안). 다른 SDI 는 옮기지 않는다."""
    a, b, c = t.open_doc(), t.open_doc(), t.open_doc()
    for doc in (b, c):
        t.call("docs.sdi", doc=doc, on=True)
        time.sleep(0.5)
    t.check("sdi.normal.flags", flags(t.state("sdi", b)) == (False, True, True, True, True), t.state("sdi", b))
    t.call("sdi.setBounds", doc=c, x=AREA[0] + 900, y=500)
    t.call("sdi.setBounds", doc=b, x=FAR, y=FAR)
    t.call("docs.activate", doc=a)
    time.sleep(SETTLE_S)
    rows = {s.get("doc"): s for s in t.call("window.snapshot")["sdi"]}
    other = (rows[c]["x"], rows[c]["y"])
    size = (rows[b]["pxW"], rows[b]["pxH"])
    t.call("winmenu.noteTab", doc=b)
    t.check("sdi.tab.moveDefault.done", t.run("tab", b, "movedefault") is True)
    rows = {s.get("doc"): s for s in t.call("window.snapshot")["sdi"]}
    stop_if_outside(t, "sdi", rows[b]["x"], rows[b]["y"])
    t.check("sdi.tab.moveDefault.inArea", True, {"at": (rows[b]["x"], rows[b]["y"]), "expected": (AREA[0] + 40, AREA[1] + 40)})
    t.check("sdi.tab.moveDefault.slot", (rows[b]["x"], rows[b]["y"]) == (AREA[0] + 40, AREA[1] + 40), rows[b])
    t.check("sdi.tab.moveDefault.size", (rows[b]["pxW"], rows[b]["pxH"]) == size, size)
    t.check("sdi.tab.moveDefault.active", t.active() == b, t.active())
    t.check("sdi.other.unmoved", (rows[c]["x"], rows[c]["y"]) == other, other)
    t.call("sdi.setBounds", doc=b, x=FAR, y=FAR)
    time.sleep(SETTLE_S)
    t.check("sdi.title.moveDefault.done", t.run("sdi", b, "movedefault") is True)
    row = next(s for s in t.call("window.snapshot")["sdi"] if s.get("doc") == b)
    stop_if_outside(t, "sdi-title", row["x"], row["y"])
    t.check("sdi.title.moveDefault.slot", (row["x"], row["y"]) == (AREA[0] + 40, AREA[1] + 40), row)
    return {"docs": [a, b, c]}


def stage_main(t):
    """메인 창 기본 위치: override 영역 가운데, 크기 유지."""
    t.open_doc()
    before = t.call("window.snapshot")["main"]
    t.check("main.moveDefault.done", t.run("main", None, "movedefault") is True)
    main = t.call("window.snapshot")["main"]
    stop_if_outside(t, "main", main["x"], main["y"])
    centre = abs(main["x"] + main["pxW"] / 2 - (AREA[0] + AREA[2] / 2)) <= 4 and abs(main["y"] + main["pxH"] / 2 - (AREA[1] + AREA[3] / 2)) <= 4
    t.check("main.moveDefault.centre", centre, {"before": before, "after": main})
    t.check("main.moveDefault.size", (main["clientPxW"], main["clientPxH"]) == (before["clientPxW"], before["clientPxH"]), (main["clientPxW"], main["clientPxH"]))
    t.check("main.state.normal", main["state"] == "Normal", main["state"])
    return {"main": main}


STAGES = {"a": stage_a, "load": stage_load, "sdi": stage_sdi, "main": stage_main, "popup": stage_popup, "nc": stage_nc}
NEEDS_APPROVAL = ("popup", "nc")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--stage", choices=sorted(STAGES), required=True)
    p.add_argument("--user-away-approved", help="popup·nc 필수: 사용자가 PC 를 비운다는 근거와 승인자(누가·언제)")
    p.add_argument("--out", required=True)
    args = p.parse_args()
    if "logs/ui28/" in os.path.normpath(args.out).replace("\\", "/"):
        print("logs/ui28/ 는 otest 결과 전용이다.")
        return 3
    if args.stage in NEEDS_APPROVAL and len(args.user_away_approved or "") < 10:
        print("거부: %s 는 별도 OS 창이 사용자 화면에 뜰 수 있다. --user-away-approved '<누가·언제>' 가 필요하다." % args.stage)
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    pid = status.get("pid")
    iso = retest_user_files.isolation(pid)
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3

    raw = {"pid": pid, "stage": args.stage}
    code = 0
    guard = OffscreenGuard(pid)
    run = None
    try:
        with guard:
            run = Run(r, guard, pid)
            raw["data"] = STAGES[args.stage](run)
    except GuardBreach as ex:
        raw["breach"] = str(ex)
        print("가드 중단:", ex)
        code = 4
    raw["guard"] = guard.summary()
    raw["checks"] = run.items if run else []
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    failed = [c["name"] for c in raw["checks"] if not c["pass"]]
    print("checks=%d failed=%s guard=%s" % (len(raw["checks"]), failed, raw["guard"]))
    return code or (1 if failed else 0)


if __name__ == "__main__":
    sys.exit(main())
