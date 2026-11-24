#!/usr/bin/env python3
# MDI 창 끌기 경계 회귀 — 실제 포인터 경로(창 메시지 끌기)로 네 방향 한계·다시 잡기·복구·저장 복원·정렬·최대화·스크롤바를 재서 JSON 에 남긴다
"""
사용: python3 -B retest_mdi_drag.py --run-dir <bin/run/<이름>/net10.0> --label after [--work logs/odev-fix-mdidrag] [--keep] [--snap off|on|keep] [--only lock,…]
  --work 는 /mnt/<드라이브>/ 아래만(Linux 경로는 Windows 앱이 로그를 못 만들어 거부). --snap 기본 off(가장자리 칸 배치가 한계 검사를 가리지 않게).
  lock = 위치 고정 켬→끌기 막힘→끔→끌기 이동(끔 직후·2초 뒤).
  bin/run 복사본을 임시 data·profile, --multi-instance, 화면 밖으로 직접 띄우고 끝나면 자기 PID 만 내린다(mdi_drag_app.py).
  docs.move 만으로 재면 안 된다: Dock MdiDocumentWindow.DragMove 의 뒤따르는 영역 제한을 타지 않아 실제 끌기와 결과가 다르다. 그래서 끌기는 L2 창 메시지로 하고 docs.move 는 대조로만 남긴다.
종료 코드: 0=측정 완료·검사 통과, 1=검사 실패, 2=연결 실패, 3=실행 거부·전경 게이트 실패. logs/ui28/ 에는 쓰지 않는다(otest 전용).
"""
import argparse
import json
import math
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mdi_drag_app as app  # noqa: E402
import retest_mdi_drag_extra as ex  # noqa: E402  잡을 곳 현재값 조회에 쓴다
from offscreen_guard import GuardBreach, OffscreenGuard  # noqa: E402

SETTLE_S = 0.6
GUARD_S = 1.2  # 복구 디바운스 400ms + 여유


def ratio_keep(size, pct):  # MdiDragLimit.RatioKeep 과 같은 식 ceil(크기×비율−1e-9). pct = placement.state minVisiblePercent(0~90), 0 이면 0 = 11-08 잡을 곳 규칙 그대로
    p = min(max(pct or 0, 0), 90) / 100.0
    return math.ceil(size * p - 1e-9) if p > 0 else 0


class Run:
    def __init__(self, remote, pid):
        self.r, self.pid, self.checks = remote, pid, []
        self.hwnd = app.main_hwnd(pid)
        self.origin = app.client_origin(self.hwnd)

    def check(self, name, ok, detail=""):
        self.checks.append({"name": name, "pass": bool(ok), "detail": str(detail)})
        print("  %s %s %s" % ("PASS" if ok else "FAIL", name, detail))

    def resize_main(self, w, h):
        """메인 크기 변경은 창이 화면 안으로 끌려올 수 있는 조작이라 직후에 감시 결과를 본다."""
        self.r.call("placement.resizeMain", w=w, h=h)
        self.guard.check()

    def gate(self, where):
        self.guard.check()
        if app.foreground_pid() == self.pid:
            print("전경이 시험 앱이 됐다 — 중단: %s" % where)
            sys.exit(3)

    def state(self):
        return self.r.call("placement.state")

    def doc(self, doc):
        return next(d for d in self.state()["docs"] if d["id"] == doc)

    def bounds(self, doc):
        return self.doc(doc)["bounds"]

    def press_point(self, doc):
        """보이는 끌 수 있는 구간의 가운데(화면 물리 좌표 기준 클라이언트 좌표). 잡을 곳이 없으면 None."""
        s = self.state()
        a, d = s["area"], next(x for x in s["docs"] if x["id"] == doc)
        b, g = d["bounds"], d.get("grab") or {"left": 60, "rightInset": 310, "titleBottom": 33}
        foldable = g.get("foldable", 0)
        # 왼쪽으로 밀려 기능 버튼 줄이 접힌 상태면 잡을 구간은 RightInset-Foldable 앞까지(MdiDragLimit)
        right_inset = g["rightInset"] - foldable if (foldable > 0 and b["x"] < 0) else g["rightInset"]
        lo, hi = max(b["x"] + g["left"], 0), min(b["x"] + b["w"] - right_inset, a["dipW"])
        if hi - lo < 4:
            return None
        y = b["y"] + min(16, g["titleBottom"] / 2)
        if y >= a["dipH"] or y < 0:
            return None
        return a["x"] + (lo + hi) / 2 - self.origin[0], a["y"] + y - self.origin[1]

    def drag(self, doc, dx, dy):
        """제목 줄을 눌러 끈다. 끌 자리가 없으면 False."""
        self.gate("끌기 전")
        self.origin = app.client_origin(self.hwnd)  # 메인 크기 변경 뒤에도 맞게 매번 잰다
        at = self.press_point(doc)
        if at is None:
            return False
        app.post_drag(self.hwnd, at[0], at[1], at[0] + dx, at[1] + dy)
        time.sleep(SETTLE_S)
        self.gate("끌기 뒤")
        return True


def visible_title(b, area):
    return round(min(b["x"] + b["w"], area["dipW"]) - max(b["x"], 0), 1)


def measure_directions(run, doc, raw, new):
    """네 방향 한계와 그 자리에서 다시 잡아 되돌리기. 복구가 끼어들지 않게 메인 크기는 그대로다."""
    area = run.state()["area"]
    raw["area"], raw["grab"] = area, run.doc(doc).get("grab")
    start = run.bounds(doc)
    raw["start"] = start
    rows = {}
    for name, dx, dy in (("left", -3000, 0), ("right", 3000, 0), ("up", 0, -3000), ("down", 0, 3000)):
        run.r.call("placement.markPlaced", doc=doc, x=100, y=100, w=start["w"], h=start["h"])  # 방향마다 같은 자리에서 시작
        time.sleep(0.3)
        run.drag(doc, dx, dy)
        limit = run.bounds(doc)
        back_ok = run.drag(doc, 150 if name == "left" else -150 if name == "right" else 0, 80 if name == "up" else -80 if name == "down" else 0)
        back = run.bounds(doc)
        rows[name] = {"limit": limit, "visible_w": visible_title(limit, area), "regrab_point": back_ok, "after_regrab": back,
                      "regrabbed": back != limit}
    raw["directions"] = rows
    print("  한계:", {k: (v["limit"]["x"], v["limit"]["y"], v["visible_w"]) for k, v in rows.items()})
    if not new:
        return
    run.check("dock_magnet_restored", run.state().get("dockMagnet") is True, run.state().get("dockMagnet"))  # SDI 자석이 쓰는 값
    g = raw["grab"]
    w = start["w"]
    foldable, pct, mg = g.get("foldable", 0), run.state().get("minVisiblePercent", 0), ex.effective_min_grab(run)  # 기능 버튼 줄 폭(0=줄 없음), 잔존 비율 %(키 없는 옛 판본은 0)
    keep_left = min(w, max(ratio_keep(w, pct), (g["rightInset"] - foldable) + mg))  # 왼쪽 밖은 줄이 접힌 잔존
    keep_right = min(w, max(ratio_keep(w, pct), g["left"] + mg))
    keep_down = max(g["titleBottom"], min(start["h"], ratio_keep(start["h"], pct)))  # 아래는 제목 줄이 하한, 위는 0 정지(비율 무관)
    raw.update(min_visible_percent=pct, min_grab=mg, expected_keep={"left": keep_left, "right": keep_right, "down": keep_down}, foldable=foldable, folded=foldable > 0 and rows["left"]["limit"]["x"] < 0)
    run.check("foldable_folded_recorded", True, {"foldable": foldable, "folded": raw["folded"]})
    run.check("left_limit", abs(rows["left"]["limit"]["x"] - (keep_left - w)) <= 1, rows["left"]["limit"])
    run.check("right_limit", abs(rows["right"]["limit"]["x"] - (area["dipW"] - keep_right)) <= 1, rows["right"]["limit"])
    run.check("up_blocked", rows["up"]["limit"]["y"] == 0, rows["up"]["limit"])
    run.check("down_title_visible", abs(rows["down"]["limit"]["y"] - (area["dipH"] - keep_down)) <= 1, rows["down"]["limit"])
    for k, v in rows.items():
        run.check("regrab_" + k, v["regrabbed"], "%s -> %s" % (v["limit"], v["after_regrab"]))


def measure_recovery(run, doc, raw, new):
    """요건 5: 밀어 둔 창은 그대로, 제목 줄을 잡을 수 없게 됐을 때만 기본 위치."""
    r = run.r
    size = r.call("window.snapshot")["main"]
    w0, h0 = size["clientPxW"], size["clientPxH"]
    r.call("placement.force", on=True)
    rows = {}
    run.drag(doc, 3000, 0)
    at = run.bounds(doc)
    moved0 = run.state()["mdiMoved"]
    run.resize_main(w=w0 + 40, h=h0)
    time.sleep(GUARD_S)
    run.resize_main(w=w0, h=h0)
    time.sleep(GUARD_S)
    rows["grow_and_back"] = {"before": at, "after": run.bounds(doc), "moved": run.state()["mdiMoved"] - moved0}
    run.resize_main(w=w0 - 20, h=h0)
    time.sleep(GUARD_S)
    rows["shrink_20"] = {"before": at, "after": run.bounds(doc), "moved": run.state()["mdiMoved"] - moved0}
    run.resize_main(w=w0 - 200, h=h0)
    time.sleep(GUARD_S)
    rows["shrink_200"] = {"before": at, "after": run.bounds(doc), "moved": run.state()["mdiMoved"] - moved0, "area": run.state()["area"]}
    run.resize_main(w=w0, h=h0)
    time.sleep(GUARD_S)
    run.drag(doc, -3000, 0)
    left = run.bounds(doc)
    moved1 = run.state()["mdiMoved"]
    run.resize_main(w=w0 - 200, h=h0 - 100)
    time.sleep(GUARD_S)
    rows["left_pushed_shrink"] = {"before": left, "after": run.bounds(doc), "moved": run.state()["mdiMoved"] - moved1}
    run.resize_main(w=w0, h=h0)
    time.sleep(GUARD_S)
    r.call("placement.force", on=False)
    raw["recovery"] = rows
    if new:
        run.check("recover_not_on_grow_back", rows["grow_and_back"]["after"] == at and rows["grow_and_back"]["moved"] == 0, rows["grow_and_back"])
        run.check("recover_not_on_small_shrink", rows["shrink_20"]["after"] == at, rows["shrink_20"])
        run.check("recover_when_ungrabbable", rows["shrink_200"]["moved"] >= 1 and rows["shrink_200"]["after"]["x"] < at["x"], rows["shrink_200"])
        run.check("recover_not_left_pushed", rows["left_pushed_shrink"]["after"] == left, rows["left_pushed_shrink"])


def measure_keep(run, doc, raw, new):
    """저장·복원, 최대화·복원, 최소화·복원, 정렬, 스크롤바."""
    r = run.r
    r.call("docs.activate", doc=doc)  # 둘째 창이 제목 줄을 덮지 않게 앞으로 가져온다
    r.call("placement.markPlaced", doc=doc, x=300, y=100, w=640, h=420)
    time.sleep(0.3)
    run.drag(doc, -3000, 0)
    pushed = run.bounds(doc)
    rows = {"pushed": pushed}
    if new:
        run.check("keep_doc_really_pushed", pushed["x"] < 0, pushed)  # 안 밀렸으면 아래 유지 검사가 헛돈다
    r.call("docs.state", doc=doc, state="maximized")
    time.sleep(SETTLE_S)
    r.call("docs.state", doc=doc, state="normal")
    time.sleep(SETTLE_S)
    rows["after_max_restore"] = run.bounds(doc)
    r.call("docs.state", doc=doc, state="minimized")
    time.sleep(SETTLE_S)
    r.call("docs.state", doc=doc, state="normal")
    time.sleep(SETTLE_S)
    rows["after_min_restore"] = run.bounds(doc)
    rows["layout_save"] = r.call("layout.save", name="mdi-drag", overwrite=True)
    rows["layout_load"] = r.call("layout.load", name="mdi-drag")
    time.sleep(1.0)
    rows["after_layout"] = [d["bounds"] for d in run.state()["docs"] if d["state"] == "Normal"]
    tree = r.call("tree.get", window=0, named=False, depth=70) or {}
    bars = []

    def walk(n):
        if n.get("type") == "MdiLayoutPanel":
            bars.append(("panel", n.get("bounds")))
        if n.get("type") == "ScrollViewer" and any(c.get("type") == "MdiLayoutPanel" for c in n.get("children") or []):
            bars.append(("scrollviewer_over_panel", n.get("bounds")))
        for c in n.get("children") or []:
            walk(c)

    for t in tree.get("tree") or []:
        walk(t)
    rows["panels"] = bars
    area_before = raw["area"]
    area_after = run.state()["area"]
    rows["area_same"] = (area_before["dipW"], area_before["dipH"]) == (area_after["dipW"], area_after["dipH"])
    for mode in ("cascade", "tile-h", "tile-v"):
        r.call("docs.arrange", mode=mode)
        time.sleep(SETTLE_S)
        rows["arrange_" + mode] = [d["bounds"] for d in run.state()["docs"]]
    raw["keep"] = rows
    if not new:
        return
    run.check("max_restore_keeps", rows["after_max_restore"] == pushed, rows["after_max_restore"])
    run.check("min_restore_keeps", rows["after_min_restore"] == pushed, rows["after_min_restore"])
    run.check("layout_keeps", any(abs(b["x"] - pushed["x"]) <= 1 and abs(b["y"] - pushed["y"]) <= 1 for b in rows["after_layout"]), rows["after_layout"])
    run.check("no_scroll_over_panel", not any(k == "scrollviewer_over_panel" for k, _ in bars) and rows["area_same"], bars)
    a = area_after
    for mode in ("cascade", "tile-h", "tile-v"):
        inside = all(b["x"] >= 0 and b["y"] >= 0 and b["x"] + b["w"] <= a["dipW"] + 1 and b["y"] + b["h"] <= a["dipH"] + 1 for b in rows["arrange_" + mode])
        run.check("arrange_inside_" + mode, inside, rows["arrange_" + mode])


def measure_lock_unlock(run, doc, raw, new):
    """위치 고정 켬 → 끌기(안 움직임) → 끔 → 끌기(움직임). 끔 직후(<500ms, 끌기 스냅샷 재사용 구간)와 2초 뒤 두 시점을 실제 포인터 경로로 본다."""
    r = run.r
    rows = {}

    def place():
        r.call("placement.markPlaced", doc=doc, x=100, y=100, w=640, h=420)
        time.sleep(0.3)
        return run.bounds(doc)

    def one(name):
        b0 = run.bounds(doc)
        pressed = run.drag(doc, 150, 80)
        b1 = run.bounds(doc)
        rows[name] = {"before": b0, "after": b1, "pressed": pressed, "moved": b0 != b1, "locked": (r.call("chrome.state", doc=doc) or {}).get("locked")}

    place()
    one("baseline")
    place()
    r.call("chrome.lock", doc=doc)
    one("locked")
    r.call("chrome.lock", doc=doc)  # 끔 — 같은 시작 경계 그대로 곧바로 끈다
    one("unlock_immediate")
    place()
    time.sleep(2.0)
    one("unlock_after_2s")
    raw["lock_unlock"] = rows
    print("  고정→해제 끌기:", {k: (v["moved"], v["locked"]) for k, v in rows.items()})
    run.check("lock_baseline_moves", rows["baseline"]["moved"], rows["baseline"])
    run.check("lock_blocks_drag", rows["locked"]["locked"] is True and not rows["locked"]["moved"], rows["locked"])
    run.check("unlock_drag_immediate", rows["unlock_immediate"]["locked"] is False and rows["unlock_immediate"]["moved"], rows["unlock_immediate"])
    run.check("unlock_drag_after_2s", rows["unlock_after_2s"]["moved"], rows["unlock_after_2s"])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--run-dir", required=True)
    p.add_argument("--label", required=True)
    p.add_argument("--work", default=os.path.join(HERE, "..", "..", "..", "logs", "odev-fix-mdidrag"))
    p.add_argument("--keep", action="store_true")
    p.add_argument("--chrome-style", default="", help="mac|win|mars — 캡션이 왼쪽인 배치도 본다")
    p.add_argument("--snap", choices=("keep", "on", "off"), default="off",
                   help="창 배치(Snap) 옵션. 기본 off — 가장자리 끌기가 칸 배치·Snap Assist 로 바뀌면 한계 검사가 다른 기능을 잰다. keep=앱 저장값 그대로")
    p.add_argument("--only", default="", help="쉼표 목록만 잰다(lock,directions,narrow,resize,real_resize,capture,recovery,redock,keep). 비우면 전부")
    args = p.parse_args()
    work = os.path.abspath(os.path.join(args.work, args.label))
    if "logs/ui28" in work.replace("\\", "/"):
        print("logs/ui28/ 는 otest 결과 전용이다")
        return 3
    if not work.startswith("/mnt/"):  # Windows 앱이 \\wsl.localhost\ 경로에 로그·data 폴더를 만들지 못해 기동 즉시 죽는다(연결 정보 파일 미생성)
        print("--work 는 Windows 드라이브(/mnt/<드라이브>/…) 아래여야 한다: %s" % work)
        return 2
    os.makedirs(work, exist_ok=True)
    pid, info, log_path = app.launch(args.run_dir, work, ["--chrome-style", args.chrome_style] if args.chrome_style else [])
    print("격리 기동 pid=%s log=%s" % (pid, log_path), flush=True)
    import retest_mdi_drag_extra as extra
    from retest_remote import Remote
    r = Remote()
    run = Run(r, pid)
    fg0 = app.foreground_pid()
    raw = {"label": args.label, "pid": pid, "run_dir": args.run_dir, "virtual_screen": app.virtual_screen(), "foreground_before": fg0}
    run.guard = OffscreenGuard(pid)  # 창 사각형과 사용자 모니터 교집합·전경을 측정 내내 기록한다
    breach = None
    try:
        run.guard.start()
        if args.snap != "keep":  # 임시 프로필이라 원복할 저장본이 없다
            raw["snap_option"] = r.call("snapflyout.options", snapEnabled="true" if args.snap == "on" else "false")
        raw["snap"] = args.snap
        r.call("docs.open", kind="grid")
        time.sleep(0.5)
        doc = (r.call("docs.list") or [{}])[0].get("id")
        new = bool(run.doc(doc).get("grab"))  # 개선 전 판본에는 grab 값이 없다 — 측정만 하고 판정하지 않는다
        raw["selftest"] = r.call("placement.selftest")
        if new:
            run.check("selftest", raw["selftest"]["passed"] == raw["selftest"]["total"], [c for c in raw["selftest"]["cases"] if not c["pass"]])
        only = {s.strip() for s in args.only.split(",") if s.strip()}
        want = lambda k: not only or k in only  # noqa: E731
        if want("lock"):
            measure_lock_unlock(run, doc, raw, new)  # 먼저 잰다 — 뒤 항목이 창 배치를 바꿔도 이 판정은 시작 자리에서 한다
        if want("directions"):
            raw["manager_docs_move"] = {k: (r.call("docs.move", doc=doc, dx=dx, dy=dy, steps=3) or {}).get("final") for k, dx, dy in (("left", -3000, 0), ("right", 3000, 0), ("up", 0, -3000), ("down", 0, 3000))}
            measure_directions(run, doc, raw, new)
        for key, fn in (("narrow", extra.measure_narrow), ("resize", extra.measure_resize), ("real_resize", extra.measure_real_resize)):
            if want(key):
                fn(run, doc, raw, new)
        if want("capture"):
            extra.capture_limits(run, doc, raw, work)
        if want("recovery"):
            measure_recovery(run, doc, raw, new)
        if want("redock"):
            extra.measure_redock_left(run, doc, raw, new)
        if want("keep"):
            r.call("docs.open", kind="grid", allowDuplicate=True)  # 정렬·저장에 쓸 둘째 창
            time.sleep(0.4)
            if "area" not in raw:
                raw["area"] = run.state()["area"]
            measure_keep(run, doc, raw, new)
        bad = app.log_violation(log_path)
        run.check("no_single_instance_trace", bad is None, bad)
    except GuardBreach as e:
        breach = str(e)
        print("창 노출·전경 감지 — 중단하고 자기 앱을 내린다: %s" % breach, flush=True)
    finally:
        run.guard.stop()
        raw["guard"] = run.guard.summary()
        raw["breach"] = breach
        print("창 감시:", raw["guard"], flush=True)
        run.check("guard_no_breach", not raw["guard"]["breaches"] and raw["guard"]["stoppedCleanly"], raw["guard"]["breaches"])
        raw["checks"] = run.checks
        raw["foreground_after"] = app.foreground_pid()
        out = os.path.join(work, "mdi_drag.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=1)
        print(out)
        if not args.keep:
            print(app.stop(pid, app.win(os.path.dirname(info))))
    failed = [c["name"] for c in run.checks if not c["pass"]]
    print("검사 %d건, 실패 %d건 %s" % (len(run.checks), len(failed), failed))
    if breach:
        return 3
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
