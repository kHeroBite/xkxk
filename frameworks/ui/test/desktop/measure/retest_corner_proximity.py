#!/usr/bin/env python3
# 패널 끝 확장 버튼 근접 표시 측정 — 버튼 경계에서 49·50·51dip 표시 상태, 다른 패널 위, 숨김 클릭 비가로채기, 눌러서 확장, 끌기·투어 동안 동작을 값으로 남긴다
"""
사용: python3 -B retest_corner_proximity.py --launch <bin/run 복사본의 net10.0 폴더> --out logs/<담당>/<회차>/desktop.json [--only 단계,...]
--launch 는 mdi_drag_app.launch 로 임시 폴더·화면 밖·--multi-instance 기동을 하고 끝나면 자기 PID 만 종료한다. 이미 띄운 격리 앱에 붙으려면 --launch 없이 PD_REMOTE_INFO=<임시 data>/remote-control.json 을 준다.
포인터는 창 메시지(WM_MOUSEMOVE·WM_LBUTTON*, L2)다. 실제 마우스 입력이 아니므로 체감은 사람 확인 항목으로 남긴다. 커서 이동·전경 획득·키 주입은 하지 않는다.
실제 커서가 창 밖이면 OS 가 WM_MOUSELEAVE 를 곧바로 보내므로, 근접 항목은 panel.corner.holdLeave on 으로 근접 판정만 그 떠남을 무시하게 하고 잰다. 창 떠남 항목은 무시하지 않고 잰다.
같은 이유로 포인터 위 상태(불투명도 1.0)는 창 메시지로 유지되지 않아 재지 않는다. 포인터 위 강조는 사람 확인 항목이다.
격리되지 않은 앱은 rc=3 으로 거부하고, 겹침·전경은 GuardBreach 로 즉시 멈춘다.
종료 코드: 0=전부 통과, 1=실패 있음, 2=연결 실패, 3=실행 거부, 4=가드 중단.
"""
import argparse
import json
import os
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
from mdi_drag_app import main_hwnd, ps, foreground_pid, launch, stop, log_violation, win  # noqa: E402

BL, BR = "panel.corner.BottomPane.BottomLeft", "panel.corner.BottomPane.BottomRight"
WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP = 0x0200, 0x0201, 0x0202
MK_LBUTTON = 1
SETTLE_S = 0.35  # 페이드 100ms + 메시지 처리 여유
POST_CS = ("Add-Type @\"\nusing System;using System.Runtime.InteropServices;\n"
           "public class PMX{[DllImport(\"user32.dll\")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}\n\"@\n")


class Run:
    def __init__(self, r, guard, pid):
        self.r, self.guard, self.pid = r, guard, pid
        self.items = []
        self.hwnd = main_hwnd(pid)
        self.scale = 1.0

    def call(self, cmd, **kw):
        self.guard.check()
        return self.r.call(cmd, **kw)

    def check(self, name, passed, detail=None):
        self.items.append({"name": name, "pass": bool(passed), "detail": detail})
        print(("PASS " if passed else "FAIL ") + name + ("  " + json.dumps(detail, ensure_ascii=False)[:240] if detail is not None else ""))

    def post(self, *messages):
        """(msg, x_dip, y_dip, wParam) 를 차례로 보낸다. 한 PowerShell 에 묶어 콘솔 생성을 줄인다."""
        self.guard.check()
        lines = []
        for msg, x, y, w in messages:
            px, py = int(round(x * self.scale)), int(round(y * self.scale))
            lines.append("[void][PMX]::PostMessage([IntPtr]%d,%d,[IntPtr]%d,[IntPtr]%d);Start-Sleep -Milliseconds 30" % (self.hwnd, msg, w, (py << 16) | (px & 0xFFFF)))
        ps(POST_CS + "\n".join(lines))
        time.sleep(SETTLE_S)

    def move(self, x, y):
        self.post((WM_MOUSEMOVE, x, y, 0))

    def state(self):
        return self.call("panel.corner.state") or {}

    def button(self, button_id, state=None):
        return next((b for b in (state or self.state()).get("buttons", []) if b["id"] == button_id), None)

    def shown(self, button_id):
        return (self.button(button_id) or {}).get("shown")


def center(rect):
    return rect["x"] + rect["w"] / 2, rect["y"] + rect["h"] / 2


def inside(rect, x, y):
    return rect["x"] <= x <= rect["x"] + rect["w"] and rect["y"] <= y <= rect["y"] + rect["h"]


def far_point(t):
    """문서 영역 가운데: 어느 버튼에서도 50dip 넘게 떨어진 점."""
    d = t.call("panel.bounds")["Documents"]
    return d["x"] + d["w"] / 2, d["y"] + d["h"] / 2


def ensure_default(t):
    t.call("panel.dock", id="NotificationPane", side="right")
    for corner in ("TopLeft", "TopRight", "BottomLeft", "BottomRight"):
        t.call("panel.corner.release", corner=corner)
    time.sleep(0.4)


def stage_hidden(t):
    """평소 숨김: 불투명도 0·적중 안 됨, 트리에는 그대로(판정 도구의 visible 은 참)."""
    t.move(*far_point(t))
    s = t.state()
    rows = {b["id"]: (b["shown"], b["hitTest"], b["opacity"], b["visible"]) for b in s["buttons"]}
    t.check("hidden.all_when_far", s["buttons"] and all(v == (False, False, 0, True) for v in rows.values()), rows)
    t.check("hidden.one_tracker_subscribed", [w["subscribed"] for w in s["proximity"]["windows"]] == [True], s["proximity"])
    return {"far": rows}


def stage_boundary(t):
    """가로·세로·대각선 거리 경계."""
    rect = t.button(BR)["rect"]
    cx, cy = center(rect)
    left, right = rect["x"], rect["x"] + rect["w"]
    top = rect["y"]
    rows = []
    for name, x, y, want in (
        ("left49", left - 49, cy, True), ("left50", left - 50, cy, True), ("left51", left - 51, cy, False),
        ("up49", cx, top - 49, True), ("up50", cx, top - 50, True), ("up51", cx, top - 51, False),
        ("diag35", left - 35, top - 35, True),  # 49.5
        ("diag37", left - 37, top - 37, False),  # 52.3 — 가로세로만 보면 안 되는지 가른다
    ):
        t.move(x, y)
        b = t.button(BR)
        rows.append({"name": name, "x": x, "y": y, "shown": b["shown"], "hitTest": b["hitTest"], "opacity": b["opacity"]})
        t.check("boundary." + name, b["shown"] is want and b["hitTest"] is want, rows[-1])
    t.move(cx, cy)
    t.move(cx, cy)  # 첫 이동으로 보이고 두 번째 이동에 포인터 위 상태가 된다
    on = t.button(BR)
    t.check("boundary.on_button", on["shown"] is True and on["hitTest"] is True and on["opacity"] > 0, on)
    return rows


def stage_other_panel(t):
    """다른 패널·분할선 위에서도 거리로만 판단하고 레이아웃을 밀지 않는다."""
    rect = t.button(BR)["rect"]
    cx, cy = center(rect)
    bounds = t.call("panel.bounds")
    notify = bounds["NotificationPane"]
    x = rect["x"] + rect["w"] + 30
    t.check("other.point_in_notification", inside(notify, x, cy), {"x": x, "y": cy, "notify": notify})
    t.move(x, cy)
    t.check("other.shown_over_notification", t.shown(BR) is True)
    after = t.call("panel.bounds")
    t.check("other.layout_same", all(bounds[k] == after[k] for k in ("LeftPane", "BottomPane", "NotificationPane", "Documents")), after)
    splitter_x = (rect["x"] + rect["w"] + notify["x"]) / 2
    t.move(splitter_x, cy)
    t.check("other.shown_over_splitter", t.shown(BR) is True, {"x": splitter_x})
    return {"notify": notify, "x": x}


def stage_hidden_click(t):
    """숨긴 버튼 자리를 눌러도 동작하지 않는다(옮기지 않고 누른 메시지만 보낸다)."""
    t.move(*far_point(t))
    rect = t.button(BL)["rect"]
    cx, cy = center(rect)
    before = t.state()["owners"]
    t.post((WM_LBUTTONDOWN, cx, cy, MK_LBUTTON), (WM_LBUTTONUP, cx, cy, 0))
    s = t.state()
    t.check("hiddenclick.not_claimed", s["owners"] == before, s["owners"])
    t.check("hiddenclick.still_hidden", t.button(BL, s)["shown"] is False, t.button(BL, s))
    t.move(*far_point(t))


def stage_click(t):
    """가까이 가서 보인 뒤 누르면 확장한다. 새 버튼도 같은 규칙으로 숨어 있다."""
    t.move(*far_point(t))
    before = t.call("panel.bounds")
    rect = t.button(BL)["rect"]
    cx, cy = center(rect)
    t.post((WM_MOUSEMOVE, cx, cy, 0), (WM_MOUSEMOVE, cx, cy, 0))
    t.check("click.revealed", t.shown(BL) is True)
    t.post((WM_LBUTTONDOWN, cx, cy, MK_LBUTTON), (WM_LBUTTONUP, cx, cy, 0))
    time.sleep(0.4)
    s = t.state()
    after = t.call("panel.bounds")
    t.check("click.claimed", s["owners"].get("BottomLeft") == "BottomPane", s["owners"])
    t.check("click.bottom_reaches_left", abs(after["BottomPane"]["x"] - before["LeftPane"]["x"]) <= 1.5, {"before": before["LeftPane"], "after": after["BottomPane"]})
    t.move(*far_point(t))
    t.check("click.left_button_hidden_when_far", t.shown("panel.corner.LeftPane.BottomLeft") is False, t.button("panel.corner.LeftPane.BottomLeft"))
    t.call("panel.corner.release", corner="BottomLeft")
    time.sleep(0.4)
    t.check("click.released", not t.state()["owners"])
    return {"before": before, "after": after}


def stage_focus(t):
    """키보드 포커스면 포인터가 멀어도 보이고, 포인터로 받은 포커스는 멀어지면 숨는다."""
    t.move(*far_point(t))
    t.call("panel.corner.focus", id=BL, method="Tab")
    time.sleep(SETTLE_S)
    b = t.button(BL)
    t.check("focus.tab_shows", b["shown"] is True and b["opacity"] == 1, b)
    t.call("panel.corner.focus", id="", method="None")
    time.sleep(SETTLE_S)
    t.check("focus.lost_hides", t.shown(BL) is False, t.button(BL))
    t.call("panel.corner.focus", id=BL, method="Pointer")
    t.move(*far_point(t))
    t.check("focus.pointer_focus_hides", t.shown(BL) is False, t.button(BL))
    t.call("panel.corner.focus", id="", method="None")


def stage_drag(t):
    """끌기 중 숨김: 단추 눌린 이동 표식과 실제 분할선 끌기."""
    rect = t.button(BR)["rect"]
    cx, cy = center(rect)
    t.move(rect["x"] - 30, cy)
    t.check("drag.shown_before", t.shown(BR) is True)
    t.post((WM_MOUSEMOVE, rect["x"] - 30, cy, MK_LBUTTON))
    t.check("drag.pressed_move_hides", t.shown(BR) is False, t.button(BR))
    t.move(rect["x"] - 30, cy)
    t.check("drag.plain_move_shows_again", t.shown(BR) is True)
    # 하단 패널과 알림 패널 사이 분할선을 왼쪽으로 끈다(끌기 도중 값을 읽는다)
    bounds = t.call("panel.bounds")
    notify = bounds["NotificationPane"]
    sx = (rect["x"] + rect["w"] + notify["x"]) / 2  # 하단 패널 오른쪽 끝과 알림 패널 사이
    t.post((WM_MOUSEMOVE, sx, cy, 0), (WM_LBUTTONDOWN, sx, cy, MK_LBUTTON))
    for step in (4, 8, 12, 16, 20):
        t.post((WM_MOUSEMOVE, sx - step, cy, MK_LBUTTON))
    during = t.button(BR)
    during_bounds = t.call("panel.bounds")["NotificationPane"]
    t.post((WM_LBUTTONUP, sx - 20, cy, 0))
    time.sleep(0.3)
    moved = t.call("panel.bounds")["NotificationPane"]
    t.check("drag.splitter_moved", abs(moved["x"] - notify["x"]) > 5, {"before": notify["x"], "during": during_bounds["x"], "after": moved["x"]})
    t.check("drag.hidden_during_splitter", during["shown"] is False, during)
    rect = t.button(BR)["rect"]
    t.move(rect["x"] - 30, center(rect)[1])
    t.check("drag.shown_after_release", t.shown(BR) is True)
    return {"during": during, "before": notify, "after": moved}


def stage_tour(t):
    """투어가 덮는 동안 거리 안이어도 숨고, 끝나면 돌아온다."""
    rect = t.button(BR)["rect"]
    cx, cy = center(rect)
    started = t.call("tour.start", scope="main")
    time.sleep(0.8)
    t.move(rect["x"] - 30, cy)
    during = t.button(BR)
    t.check("tour.hidden_while_covered", started is not None and during["shown"] is False, during)
    t.call("tour.end")
    time.sleep(0.6)
    t.move(rect["x"] - 31, cy)
    t.check("tour.shown_after_end", t.shown(BR) is True)
    return {"during": during}


def stage_cost(t):
    """적중 검사는 거리 안 이동에서만 는다."""
    win = lambda: t.state()["proximity"]["windows"][0]  # noqa: E731
    x, y = far_point(t)
    a = win()
    t.post(*[(WM_MOUSEMOVE, x + i, y, 0) for i in range(10)])
    b = win()
    t.check("cost.far_moves_counted", b["moves"] - a["moves"] >= 10, {"before": a, "after": b})
    t.check("cost.far_no_hit_test", b["hitTests"] == a["hitTests"], {"before": a, "after": b})
    return {"before": a, "after": b}


def stage_subscription(t):
    """버튼이 없으면 구독을 끊는다(문서 분할 중). SDI 창에는 버튼이 없어 추적기도 없다."""
    t.call("docs.open", kind="form", count=2)
    time.sleep(0.4)
    doc = t.call("docs.list")[-1]["id"]
    orient = (t.state()["tree"].get("CenterPane") or {}).get("orientation")
    t.call("split.do", target="Documents", op="Bottom" if orient == "Vertical" else "Right", doc=doc)
    time.sleep(0.5)
    s = t.state()
    t.check("sub.no_buttons_no_subscription", s["buttons"] == [] and [w["subscribed"] for w in s["proximity"]["windows"]] == [False], s["proximity"])
    t.call("docs.close", doc=doc)
    time.sleep(0.5)
    s = t.state()
    t.check("sub.buttons_back_subscribed", s["buttons"] and [w["subscribed"] for w in s["proximity"]["windows"]] == [True], s["proximity"])
    docs = t.call("docs.list")
    t.call("docs.sdi", doc=docs[-1]["id"], on=True)
    time.sleep(0.8)
    s = t.state()
    sdi = (t.call("window.snapshot") or {}).get("sdi") or []
    t.check("sub.sdi_opened", len(sdi) == 1, len(sdi))
    t.check("sub.sdi_has_no_tracker", len(s["proximity"]["windows"]) == 1, s["proximity"])
    t.call("docs.sdi", doc=docs[-1]["id"], on=False)
    time.sleep(0.5)


def stage_leave(t):
    """막지 않은 OS WM_MOUSELEAVE 로 숨는다(실제 커서가 창 밖이라 메시지 뒤 곧바로 온다)."""
    t.call("panel.corner.holdLeave", on=False)
    t.move(*far_point(t))
    rect = t.button(BR)["rect"]
    t.move(rect["x"] - 30, center(rect)[1])
    time.sleep(0.4)
    natural = t.button(BR)
    t.check("leave.os_message_hides", natural["shown"] is False, natural)
    # 같은 점에서 표시 상태를 먼저 확인하고, 떠남을 살린 이동으로 숨는지 본다(앞선 이동의 떠남이 이미 처리돼 WM_MOUSELEAVE 를 따로 보내면 포인터 위 대상이 없어 아무 일도 없다)
    t.call("panel.corner.holdLeave", on=True)
    t.move(rect["x"] - 30, center(rect)[1])
    t.check("leave.held_shown", t.shown(BR) is True)
    t.call("panel.corner.holdLeave", on=False)
    t.move(rect["x"] - 30, center(rect)[1])
    t.check("leave.shown_then_hidden", t.shown(BR) is False, t.button(BR))
    t.call("panel.corner.holdLeave", on=True)


def stage_pixels(t):
    """숨김·표시 화면 비교: 버튼 자리만 바뀌고 그 밖은 그대로다. 수시로 바뀌는 상태 표시줄은 패널 아래라 뺀다."""
    from PIL import Image, ImageChops
    folder = os.path.dirname(os.path.abspath(t.out))
    rect = t.button(BR)["rect"]
    box = tuple(int(v * t.scale) for v in (rect["x"], rect["y"], rect["x"] + rect["w"], rect["y"] + rect["h"]))

    panels = t.call("panel.bounds")
    limit = int((panels["BottomPane"]["y"] + panels["BottomPane"]["h"]) * t.scale)

    def shot(name):
        path = os.path.join(folder, name + ".png")
        t.call("screenshot", window=0, path=win(path))
        time.sleep(0.3)
        image = Image.open(path).convert("RGB")
        return image.crop((0, 0, image.width, limit))

    t.move(*far_point(t))
    far = shot("corner_far")
    t.move(rect["x"] - 30, center(rect)[1])
    near = shot("corner_near")
    t.move(*far_point(t))
    again = shot("corner_far_again")
    diff = ImageChops.difference(far, near).getbbox()
    t.check("pixels.near_differs_in_button", diff is not None and diff[0] >= box[0] - 2 and diff[1] >= box[1] - 2 and diff[2] <= box[2] + 2 and diff[3] <= box[3] + 2, {"diff": diff, "button": box})
    t.check("pixels.far_restored", ImageChops.difference(far, again).getbbox() is None, ImageChops.difference(far, again).getbbox())
    return {"diff": diff, "button": box}


STAGES = (("hidden", stage_hidden), ("pixels", stage_pixels), ("boundary", stage_boundary), ("other", stage_other_panel), ("hiddenclick", stage_hidden_click), ("click", stage_click),
          ("focus", stage_focus), ("drag", stage_drag), ("tour", stage_tour), ("cost", stage_cost), ("subscription", stage_subscription), ("leave", stage_leave))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--only", default="")
    p.add_argument("--launch", default="")
    args = p.parse_args()
    if not args.launch:
        return measure(args)
    work = os.path.join(os.path.dirname(os.path.abspath(args.out)), "app")
    os.makedirs(work, exist_ok=True)
    pid, _, log_path = launch(args.launch, work)
    print("기동 pid=%s log=%s" % (pid, log_path))
    try:
        code = measure(args)
    finally:
        bad = log_violation(log_path)
        print("단일 인스턴스 흔적:", bad)
        print(stop(pid, win(os.path.abspath(work))))
    return 4 if bad else code


def measure(args):
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

    only = set(filter(None, args.only.split(",")))
    raw = {"pid": pid, "data": {}, "foreground": {}}
    code = 0
    guard = OffscreenGuard(pid)
    run = None
    try:
        with guard:
            run = Run(r, guard, pid)
            run.out = args.out
            raw["hwnd"] = run.hwnd
            run.scale = run.state()["proximity"]["scaling"]
            raw["scale"] = run.scale
            raw["foreground"]["before"] = foreground_pid()
            ensure_default(run)
            run.call("panel.corner.holdLeave", on=True)
            for name, fn in STAGES:
                if not only or name in only:
                    print("[%s]" % name)
                    raw["data"][name] = fn(run)
            run.call("panel.corner.holdLeave", on=False)
            raw["foreground"]["after"] = foreground_pid()
            run.check("foreground.not_app", raw["foreground"]["after"] != pid, raw["foreground"])
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
    print("합계 %d, 실패 %d %s" % (len(raw["checks"]), len(failed), failed))
    return code or (1 if failed else 0)


if __name__ == "__main__":
    sys.exit(main())
