#!/usr/bin/env python3
# MDI·SDI 탭 합류 회귀(계획 §7 항목 3 ①~⑧) — 진단판을 화면 밖·임시 폴더로 띄워 L0 진단 명령으로만 판정하고 L0 로 못 하는 항목은 '포그라운드 필요' 목록으로 남긴다
"""
사용: python3 -B retest_tab_join.py --run-dir <bin/run/<이름>[/net*.0]> --label <회차> [--work <폴더>] [--keep] [--skip-v4]
      python3 -B retest_tab_join.py --self-test
기동·종료는 mdi_drag_app.launch/stop(--multi-instance on, 임시 data·profile, 화면 밖, 자기 PID 만 종료)을 그대로 쓴다. 실입력(커서·전경·SendInput)은 쓰지 않는다.
L0 명령: tabjoin.drag(앱 안 포인터 끌기 — MdiTabJoinDrag 처리기를 그대로 탄다), window.tabJoin(합류 경로만), window.tabJoinTargets,
         mdigroup.probe(op=detach), placement.state.groups, docs.close(탭 X 와 같은 CloseDockable), ui.click chrome.btnClose doc=(묶음 창 캡션 닫기).
판정 항목: ① 겹치기만 하면 합류 없음 ② 아이콘 칸 250ms 머문 뒤 놓기 = 합류 ③ 머무름 0·150ms = 합류 없음 ④ 탭 줄 위 놓기 = 합류
          ⑤ 묶음에 1개 남으면 해제(끌어내기 모델 L0) ⑥ 캡션 닫기 = 묶음 전체 ⑦ 탭 X = 그 탭만 ⑧ 저장·복원 Version 5(+옛 Version 4 읽기)
종료 코드: 0=전 항목 통과, 1=검사 실패, 2=연결 실패, 3=실행 거부·창 노출 감지. 결과는 --work 아래 tab_join.json(logs/ui28 금지 — otest 전용).
"""
import argparse, glob, json, os, sys, time  # noqa: E401

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)
import mdi_drag_app as app  # noqa: E402  (common/measure 경로도 여기서 붙는다)

HOLD_OK, HOLD_SHORT = 300, (0, 150)  # 합류 머무름 기준은 250ms(PanelJoinDrag.DwellMs). 300 은 합류, 0·150 은 합류 없음이어야 한다
TOL = 0.5
FOREGROUND_NEEDED = [
    {"item": "SDI 데스크톱 제목 줄 끌기로 합류(⑥계획)", "why": "OS 이동 루프(BeginMoveDrag)라 앱 포인터 이벤트가 없고 L0 끌기 명령이 없다 — 여기서는 window.tabJoin 으로 합류 경로만 본다"},
    {"item": "탭을 실제로 묶음 밖으로 끌어내기", "why": "Dock 기본 탭 끌기다. ⑤ 는 mdigroup.probe op=detach(같은 Detach 모델)로만 본다"},
    {"item": "끄는 동안 표적 강조 캡처(pdTabJoinArmed·pdTabJoinTarget)", "why": "tabjoin.drag 는 한 요청 안에서 끝나 중간 상태를 캡처할 틈이 없다"},
    {"item": "L2 창 메시지 끌기로 250ms 머무름", "why": "post_mouse.ps1 은 끌기 도중 멈춤을 만들지 못한다 — 머무름 판정은 L0 tabjoin.drag 로 대신 본다"},
]


EXE_NAME = _paths.get("desktop.exe_name")


def resolve_run_dir(run_dir):
    """실행 파일이 있는 net*.0 폴더를 찾는다. run_dir 자체·그 아래·같은 부모의 형제 net*.0 중 exe 가 있는 가장 높은 버전을 고른다. 없으면 입력 그대로."""
    import re
    def ver(p):
        m = re.fullmatch(r"net(\d+)\.(\d+)", os.path.basename(p))
        return (int(m.group(1)), int(m.group(2))) if m else None
    if os.path.exists(os.path.join(run_dir, EXE_NAME)):
        return run_dir
    found = []
    for base in (run_dir, os.path.dirname(os.path.abspath(run_dir))):
        for d in glob.glob(os.path.join(base, "net*.0")):
            if ver(d) and os.path.exists(os.path.join(d, EXE_NAME)):
                found.append((ver(d), d))
        if found:
            break
    return max(found)[1] if found else run_dir


# 순수 판정(자가시험 대상) -----------------------------------------------------
def same_rect(a, b, tol=TOL): return bool(a and b) and all(abs(a[k] - b[k]) <= tol for k in ("x", "y", "w", "h"))
def joined(outcome): return "join=done" in str(outcome or "")
def dragging(outcome): return "dragging=True" in str(outcome or "")  # 8 DIP 넘게 움직여 끌기로 인정됐는가(MdiTabJoinDrag.LastOutcome)
def group_with(groups, doc): return next((g for g in groups or [] if doc in (g.get("docs") or [])), None)
def field(doc, name): return next((v for k, v in (doc or {}).items() if k.lower() == name.lower()), None)


def overlap_vector(src, dst, title_h=28):
    """src 창 제목 줄 누름점(tabjoin.drag 와 같은 x=min(40,w/2), y=제목 높이 절반)에서 dst 창 몸통 가운데까지의 이동량."""
    sx, sy = src["x"] + min(40, src["w"] / 2), src["y"] + title_h / 2
    return dst["x"] + dst["w"] / 2 - sx, dst["y"] + dst["h"] / 2 - sy


def v4_seed(text):
    """저장본을 옛 Version 4 형식(Groups 없음)으로 바꾼다. 키 대소문자는 저장본을 따른다."""
    out = {k: (4 if k.lower() == "version" else v) for k, v in json.loads(text).items() if k.lower() != "groups"}
    return json.dumps(out, ensure_ascii=False)

# 실행 ------------------------------------------------------------------------
class Run:
    def __init__(self, remote, pid):
        self.r, self.pid, self.checks, self.calls = remote, pid, [], []

    def check(self, name, ok, detail=""):
        self.checks.append({"name": name, "pass": bool(ok), "detail": detail})
        print("  %s %s %s" % ("PASS" if ok else "FAIL", name, json.dumps(detail, ensure_ascii=False, default=str)[:400]), flush=True)

    def call(self, cmd, **kw):
        res = self.r.call(cmd, **kw)
        self.calls.append({"cmd": cmd, "args": kw, "result": res})
        self.guard.check()
        return res

    def state(self): return self.call("placement.state") or {}
    def groups(self): return self.state().get("groups") or []
    def bounds(self, doc): return next((d["bounds"] for d in self.state().get("docs") or [] if d.get("id") == doc), None)
    def open_ids(self): return [d.get("id") for d in self.call("docs.list") or []]

    def open_grid(self, sdi=False):
        before = set(self.open_ids())
        self.call("docs.open", kind="grid", allowDuplicate=True, sdi=sdi or None)
        time.sleep(0.6)
        return next(i for i in self.open_ids() if i not in before)

    def drag(self, source, target=None, hold=HOLD_OK, dx=None, dy=None):
        res = self.call("tabjoin.drag", source=source, target=target, hold=hold, dx=dx, dy=dy) or {}
        time.sleep(0.3)
        return res

    def park(self, doc, anchor):
        """끌던 창은 포인터를 따라 움직인다 — 다음 끌기 전에 anchor 오른쪽 아래(320,220)로 비켜 이동 거리를 확보하고 anchor 아이콘을 덮지 않게 한다."""
        a, b = self.bounds(doc), self.bounds(anchor)
        if a and b:
            self.call("docs.move", doc=doc, dx=b["x"] + 320 - a["x"], dy=b["y"] + 220 - a["y"], steps=3)
            time.sleep(0.3)

    def target_id(self, source, owner, kind):
        """표적 Id 는 '<창 Id>#icon|#strip'(MdiTabJoinDrag.CollectTargets). source 창 기준 목록에 실제로 있을 때만 돌려준다(가려진 표적은 목록에 없다)."""
        want = "%s#%s" % (owner, kind)
        res = self.call("window.tabJoinTargets", source=source) or {}
        return want if any(t.get("id") == want for t in res.get("mdi") or []) else None


def mdi_items(run):
    a, b = run.open_grid(), run.open_grid()
    ba, bb = run.bounds(a), run.bounds(b)
    dx, dy = overlap_vector(ba, bb)
    res = run.drag(a, dx=dx, dy=dy)  # ① 표적 없이 b 몸통 가운데로
    run.check("c1_overlap_no_join", not run.groups() and dragging(res.get("outcome")) and not joined(res.get("outcome")), {"move": [dx, dy], "outcome": res.get("outcome")})
    run.park(a, b)
    icon = run.target_id(a, b, "icon")
    short = {}
    for h in HOLD_SHORT:  # ③ 끌기가 실제로 시작(dragging=True)돼야 '합류 없음' 이 근거가 된다
        run.park(a, b)
        short[h] = run.drag(a, target=icon, hold=h).get("outcome")
    run.check("c3_short_hold_no_join", bool(icon) and not run.groups() and all(dragging(o) and not joined(o) for o in short.values()), {"icon": icon, "outcomes": short})
    run.park(a, b)
    bb = run.bounds(b)
    res = run.drag(a, target=icon, hold=HOLD_OK)  # ②
    g = group_with(run.groups(), a)
    ok = bool(g) and g.get("docs") == [b, a] and g.get("active") == a and same_rect(g.get("bounds"), bb) and joined(res.get("outcome"))
    run.check("c2_icon_dwell_join", ok, {"group": g, "target_bounds_before": bb, "outcome": res.get("outcome")})
    if not g:
        return
    c = run.open_grid()
    run.call("docs.move", doc=c, dx=320, dy=220, steps=3)  # 묶음 탭 줄을 덮지 않게 비킨다
    strip = run.target_id(c, g["id"], "strip")
    res = run.drag(c, target=strip, hold=HOLD_OK)  # ④
    g = group_with(run.groups(), c)
    run.check("c4_strip_join", bool(strip) and bool(g) and g.get("docs") == [b, a, c] and g.get("active") == c and joined(res.get("outcome")),
              {"strip": strip, "group": g, "outcome": res.get("outcome")})
    run.call("docs.close", doc=c)  # ⑦ 탭 X 와 같은 CloseDockable
    time.sleep(0.4)
    g, ids = group_with(run.groups(), a), run.open_ids()
    run.check("c7_tab_close_only_that", c not in ids and a in ids and b in ids and bool(g) and g.get("docs") == [b, a], {"open": ids, "group": g})
    gb = (g or {}).get("bounds")
    run.call("mdigroup.probe", op="detach", doc=a)  # ⑤ 끌어내기 모델 — 묶음에 b 1개가 남아 해제돼야 한다
    time.sleep(0.4)
    ids = run.open_ids()
    run.check("c5_single_left_dissolves", not run.groups() and a in ids and b in ids and same_rect(run.bounds(b), gb),
              {"groups": run.groups(), "open": ids, "b_bounds": run.bounds(b), "group_bounds": gb})
    run.call("window.tabJoin", source=a, target=b)
    g = group_with(run.groups(), a)
    clicked = None
    try:
        clicked = run.call("ui.click", id="chrome.btnClose", doc=a)  # ⑥ 묶음 창 캡션 닫기(그 창 범위의 버튼)
    except Exception as e:  # noqa: BLE001  명령 오류는 결과에 남기고 FAIL 로 본다
        clicked = {"error": str(e)}
    time.sleep(0.6)
    ids = run.open_ids()
    run.check("c6_caption_close_whole_group", bool(g) and a not in ids and b not in ids and not run.groups(), {"group": g, "click": bool(clicked), "open": ids})


def profile_of(info):
    """mdi_drag_app.launch 는 <work>/data_<시각>/remote-control.json 과 <work>/profile_<시각>/ 을 짝으로 만든다."""
    data = os.path.dirname(info)
    return os.path.join(os.path.dirname(data), "profile_" + os.path.basename(data)[len("data_"):])


def find_layout(profile):
    for p in [os.path.join(profile, "layout.json")] + sorted(glob.glob(os.path.join(profile, "**", "*.json"), recursive=True)):
        try:
            with open(p, encoding="utf-8-sig") as f:
                text = f.read()
            if field(json.loads(text), "version") is not None:
                return p, text
        except (OSError, ValueError):
            continue
    return None, None


def save_restore(run, profile, raw):
    e, f = run.open_grid(), run.open_grid()
    run.call("window.tabJoin", source=e, target=f)
    g0 = group_with(run.groups(), e)
    run.call("layout.save")
    time.sleep(0.8)
    path, text = find_layout(profile)
    doc = json.loads(text) if text else {}
    saved = field(doc, "groups") or []
    ids = [field(x, "docIds") for x in saved]
    run.check("c8_saved_version5", (field(doc, "version") or 0) >= 6 and any(sorted(i or []) == sorted((g0 or {}).get("docs") or []) for i in ids),
              {"file": path, "version": field(doc, "version"), "groups": saved})
    run.call("docs.close", all=True)
    time.sleep(0.5)
    run.call("layout.load")
    time.sleep(1.5)
    g1 = (run.groups() or [None])[0]
    ok = bool(g0 and g1) and g1.get("docs") == g0.get("docs") and g1.get("active") == g0.get("active") and same_rect(g1.get("bounds"), g0.get("bounds"))
    run.check("c8_restore_groups", ok, {"before": g0, "after": g1})
    raw["saved_layout"] = path
    return text, len(g0.get("docs") or []) if g0 else 0


def sdi_l0(run):
    s1, s2 = run.open_grid(sdi=True), run.open_grid(sdi=True)
    res = run.call("window.tabJoin", source=s1, target=s2) or {}
    run.check("sdi_join_l0", res.get("status") == "Done" and res.get("sourceClosed") is True and sorted(res.get("docs") or []) == sorted([s1, s2]) and res.get("active") == s1, res)


def session(args, work, seed=None):
    pid, info, log_path = app.launch(args.run_dir, work, seed_layout=seed)
    print("격리 기동 pid=%s log=%s" % (pid, log_path), flush=True)
    from offscreen_guard import OffscreenGuard
    from retest_remote import Remote
    run = Run(Remote(), pid)
    run.guard = OffscreenGuard(pid)
    run.guard.start()
    return run, pid, info, log_path


def finish(run, pid, info, log_path, keep, raw, key):
    run.guard.stop()
    raw[key + "_guard"] = run.guard.summary()
    run.check(key + "_guard_no_breach", not raw[key + "_guard"]["breaches"] and raw[key + "_guard"]["stoppedCleanly"], raw[key + "_guard"]["breaches"])
    bad = app.log_violation(log_path)
    run.check(key + "_no_single_instance_trace", bad is None, bad)
    if not keep:
        raw[key + "_stop"] = app.stop(pid, app.win(os.path.dirname(info)))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--run-dir")
    p.add_argument("--label", default="tabjoin")
    p.add_argument("--work", default=os.path.join(HERE, "..", "..", "..", "logs", "tab_join"))
    p.add_argument("--keep", action="store_true")
    p.add_argument("--skip-v4", action="store_true", help="옛 Version 4 저장본 읽기(두 번째 기동)를 건너뛴다")
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()
    if args.self_test:
        return self_test()
    if not args.run_dir:
        print("거부: --run-dir 가 필요하다")
        return 3
    args.run_dir = resolve_run_dir(args.run_dir)
    work = os.path.abspath(os.path.join(args.work, args.label))
    if "logs/ui28" in work.replace("\\", "/"):
        print("logs/ui28/ 는 otest 결과 전용이다")
        return 3
    os.makedirs(work, exist_ok=True)
    raw = {"label": args.label, "run_dir": args.run_dir, "foreground_needed": FOREGROUND_NEEDED, "checks": []}
    from offscreen_guard import GuardBreach
    runs, breach, text = [], None, None
    try:
        run, pid, info, log_path = session(args, work)
        runs.append(run)
        try:
            mdi_items(run)
            text, _ = save_restore(run, profile_of(info), raw)
            sdi_l0(run)
        finally:
            finish(run, pid, info, log_path, args.keep, raw, "s1")
        if text and not args.skip_v4:
            seed = v4_seed(text)
            run2, pid2, info2, log2 = session(args, work, seed=seed)
            runs.append(run2)
            try:
                time.sleep(1.5)
                ids, gs = run2.open_ids(), run2.groups()
                want = [x for x in (field(json.loads(text), "documents") or []) if isinstance(x, dict)]
                run2.check("c8_v4_reads_without_groups", not gs and len(ids) >= max(len(want), 2), {"open": ids, "groups": gs, "saved_docs": len(want)})
            finally:
                finish(run2, pid2, info2, log2, args.keep, raw, "s2")
    except GuardBreach as e:
        breach = str(e)
        print("창 노출·전경 감지 — 중단: %s" % breach, flush=True)
    except SystemExit as e:
        print("기동 거부·실패: %s" % e, flush=True)
        return 2
    finally:
        raw["breach"] = breach
        raw["checks"] = [c for r in runs for c in r.checks]
        raw["calls"] = [c for r in runs for c in r.calls]
        out = os.path.join(work, "tab_join.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=1, default=str)
        print(out, flush=True)
    failed = [c["name"] for c in raw["checks"] if not c["pass"]]
    print("검사 %d건, 실패 %d건 %s" % (len(raw["checks"]), len(failed), failed))
    print("포그라운드 필요(L0 미판정) %d건: %s" % (len(FOREGROUND_NEEDED), [x["item"] for x in FOREGROUND_NEEDED]))
    if breach:
        return 3
    return 1 if failed else 0


def self_test():
    fails = []
    if not same_rect({"x": 1, "y": 2, "w": 3, "h": 4}, {"x": 1.4, "y": 2, "w": 3, "h": 4}) or same_rect({"x": 1, "y": 2, "w": 3, "h": 4}, {"x": 2, "y": 2, "w": 3, "h": 4}) or same_rect(None, {}):
        fails.append("사각형 비교")
    if not joined("drag:source=a|join=done") or joined("drag:source=a|accepted=False") or joined(None):
        fails.append("합류 판정")
    if group_with([{"docs": ["a", "b"]}], "b") is None or group_with([{"docs": ["a"]}], "z") is not None:
        fails.append("묶음 찾기")
    dx, dy = overlap_vector({"x": 0, "y": 0, "w": 400, "h": 300}, {"x": 100, "y": 50, "w": 400, "h": 300})
    if (dx, dy) != (260, 186):
        fails.append("겹침 이동량 %s" % ((dx, dy),))
    seed = json.loads(v4_seed(json.dumps({"Version": 5, "Groups": [{"Id": "g"}], "Documents": [1]})))
    if seed != {"Version": 4, "Documents": [1]}:
        fails.append("v4 변환 %s" % seed)
    if field({"Version": 5}, "version") != 5 or field({}, "version") is not None:
        fails.append("필드 대소문자")
    if HOLD_OK < 250 or any(h >= 250 for h in HOLD_SHORT):
        fails.append("머무름 기준이 250ms 경계를 나누지 않음")
    import tempfile
    with tempfile.TemporaryDirectory() as t:
        for n in ("net10.0", "net11.0"):
            os.makedirs(os.path.join(t, n))
        open(os.path.join(t, "net11.0", EXE_NAME), "w").close()
        if resolve_run_dir(os.path.join(t, "net10.0")) != os.path.join(t, "net11.0") or resolve_run_dir(t) != os.path.join(t, "net11.0"):
            fails.append("run-dir net11.0 탐색")
        if resolve_run_dir(os.path.join(t, "net11.0")) != os.path.join(t, "net11.0"):
            fails.append("run-dir 직접 지정")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
