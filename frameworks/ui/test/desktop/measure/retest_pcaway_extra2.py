#!/usr/bin/env python3
# PC 비움 부분 측정(B1·B2·B5·A13·F08·S2) — 기존 도구 원자료에 없는 측정 이름을 데스크톱에서 재서 pcaway_report --measure-file 입력을 쓴다. 판정기 결과 파일은 직접 쓰지 않는다
"""
사용:
  python3 -B retest_pcaway_extra2.py --items B1,B5,A13,F08 --run-dir <bin/run/<복사본>/net10.0> --phase discovery --writer otest-d
  python3 -B retest_pcaway_extra2.py --items S2 --writer otest-d --phase final     (앱을 띄우지 않고 측정 로그만 읽는다)
  python3 -B retest_pcaway_extra2.py --self-test
출력: logs/pcaway[/discovery]/extra/<ID>.measure.json. 이어서 pcaway_report.py emit --item <ID> --raw ... --measure-file <그 파일> 로 결과를 쓴다(S2 는 pcaway_report.py s2 --measure-file).
B1 은 perf_ui.py 를 같은 앱에 돌려 extra/perf_ui.json 원자료와 B1·B2 측정 파일을 함께 남긴다. --items B2 는 없다.
B2 는 측정 직전에 문서 20개가 모두 보이게 되돌리고(전체 복원+계단 배치, 안 되면 다시 열기) docs.list visible 로 확인한다. 확인 실패면 값은 '측정 무효(절차)'이고 재지 않는다.
user_report_listed(F08)는 최종 보고 문서의 일이라 여기서 재지 않는다. no_signal_to_user_pd 는 측정 로그의 발신 쪽 값이다. 사용자 Pd 쪽 수신 기록은 파일로 남지 않아(host Log 는 --log 없으면 안 쓴다) 보지 못한다.
안전 규칙은 retest_real_input.py 와 같다. 값을 만들지 않으며 못 잰 이름은 미측정 FAIL 이다.
"""
import argparse
import glob
import json
import math
import os
import subprocess
import sys
import tempfile
import threading
import time
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_pcaway_extra as extra  # noqa: E402

REQ = {
    "B1": ["on_screen_true", "load_recorded"],
    "B5": ["busy_idle_within_2s"],
    "A13": ["h61_step_text_readable_capture"],
    "F08": ["left_remain_measured", "grab_rule_kept"],
    "S2": ["no_signal_to_user_pd", "test_window_monitor_rule"],
}
B2_REQ = ["frame_p95_le_25ms"]
B2_INVALID = "측정 무효(절차)"  # B2 직전 20창 표시 확인 실패. 기준(p95≤25ms·누락 0·min_samples 30)·FRAME_MS 는 그대로이며 이 값은 판정 근거가 아니다
B2_VISIBLE_WAIT_S = 5.0
IDLE_LIMIT_MS = 2000
RESTORE_DOC_COUNTS = (4, 20)
POLL_LIMIT_S = 20
GRAB_RULE_DIP = 108 + 50  # F08: 버튼 줄 접힘 뒤 잔존 상한 = 캡션 108 + 잡을 곳 50(+허용 오차), 잡을 곳 = 보이는 폭 − 캡션 ≥ 50
# pcaway_report.py emit 이 --raw main=<perf_ui.json> 으로 pcaway_report_map.b1·b2 를 통해 채우는 이름. 측정 파일에 자리표시자를 넣으면 emit 이 덮어쓰기로 거부하거나 미측정 FAIL 로 굳는다
RAW_MAPPED = {"B1": ("ops_all_measured_1doc", "ops_all_measured_20doc", "repeat_ge_10", "doc_a9_table_updated"),
              "B2": ("immediate_le_100ms", "open_le_500ms", "theme_font_20win_le_300ms")}
DOC_W, DOC_H = 640, 400
GRAB_TOL = 2
SIGNAL_MARKS = ("single-instance already running", "signalled=True", "single-instance activate request")


class MeasureOut(ri.Result):
    """같은 측정 API 로 --measure-file 입력을 쓴다. 측정마다 그 값을 잰 도구를 source 로 남긴다."""

    def write(self, build_required=True):
        names = {m["name"] for m in self.measurements}
        for name in self.req:
            if name not in names and name not in RAW_MAPPED.get(self.ac, ()):
                self.add(name, {"unmeasured": True}, False)
        source = "%s run_id=%s" % (self.script["path"], self.run_id)
        doc = {"item": self.ac, "writer": self.args.writer, "run_id": self.run_id, "script": self.script, "captures": self.captures, "notes": self.notes, "safety": self.safety,
               "measurements": [dict(m, source=source) for m in self.measurements]}
        folder = os.path.join(self.dir, "extra")
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, self.ac + ".measure.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
        print("측정 파일 %s" % path, flush=True)
        return all(m["pass"] for m in self.measurements)


def load_avg():
    with open("/proc/loadavg", encoding="ascii") as f:
        return f.read().split()[:3]


def b1(rig, res):
    start_load = load_avg()
    win = rig.win(rig.main)
    cmd = ri.app.ps('(Get-CimInstance Win32_Process -Filter "ProcessId=%s").CommandLine' % rig.pid)
    background = (rig.r.call("placement.state") or {}).get("background")
    res.add("on_screen_true", {"monitor": win["monitor"], "rect": win["rect"], "bgPos": "--bg-pos" in cmd, "background": background}, rig.args.monitor in win["monitor"] and "--bg-pos" not in cmd and background is False)
    raw = os.path.join(res.dir, "extra", "perf_ui.json")
    os.makedirs(os.path.dirname(raw), exist_ok=True)
    done = subprocess.run([sys.executable, "-B", os.path.join(HERE, "perf_ui.py"), raw], env=dict(os.environ), capture_output=True, text=True, errors="replace", timeout=1800)
    res.notes.append({"perf_ui_rc": done.returncode, "raw": ri.rel(raw, res.root), "tail": done.stdout[-200:]})
    res.add("load_recorded", {"before": start_load, "after": load_avg(), "perf_ui_rc": done.returncode}, done.returncode == 0)
    second = MeasureOut("B2", B2_REQ, res.args, res.script, res.build, root=res.root)
    second.safety = res.safety

    def frames():
        prep, shown = b2_prepare(rig)
        second.notes.append({"b2_prepare": prep})
        if not shown:  # 창이 다 보이지 않으면 moveAll 이 그릴 것이 없어 커밋이 안 생긴다. 판정 근거가 아니므로 재지 않고 무효로 남긴다(기준은 그대로)
            return {"invalid": B2_INVALID, "reason": "B2 직전 문서 %d개가 모두 보이는 상태를 만들지 못함" % extra.DOCS_20, "prepare": prep}, False
        value = extra.frame_p95_move(rig)
        value["prepare"] = prep
        value["metric"] = "commit_frame_p95"  # 유휴 틱을 뺀 커밋 프레임 p95 + 누락 공백 0건(judge/b2_commit_frames.py), 기준 FRAME_MS 불변
        return value, value["docs"] >= extra.DOCS_20 and value["commit"]["pass"]
    second.measure("frame_p95_le_25ms", frames)
    second.write()


def visible_docs(rows, want):
    """docs.list 행에서 보이는 문서 수를 센다. 문서가 want 개 이상이고 그중 want 개 이상이 visible=True 여야 B2 를 잴 수 있다."""
    rows = rows if isinstance(rows, list) else []
    shown = sum(1 for d in rows if isinstance(d, dict) and d.get("visible") is True)
    return {"docs": len(rows), "visible": shown, "want": want}, len(rows) >= want and shown >= want


def b2_prepare(rig):
    """B2 측정 직전에 문서 20개가 모두 보이는 상태를 만들고 확인한다(otest-2 11-11 재현: perf_ui 가 tabbed 배치로 끝나면 20개 중 1개만 visible).
    1차: 전체 복원(docs.state restore) + 계단 배치(docs.arrange cascade) + 모자란 문서 열기. 2차(1차로 안 되면): 전부 닫고 새로 20개 연다.
    확인은 docs.list 의 visible 이다. 두 단계 모두 실패하면 (기록, False) — 호출 쪽이 '측정 무효(절차)'로 남긴다."""
    want, tries = extra.DOCS_20, []

    def check(step):
        deadline = time.time() + B2_VISIBLE_WAIT_S
        while True:
            row, ok = visible_docs(rig.r.call("docs.list"), want)
            if ok or time.time() >= deadline:
                tries.append(dict(row, step=step, ok=ok))
                return ok
            time.sleep(0.3)

    tries.append(dict(visible_docs(rig.r.call("docs.list"), want)[0], step="before"))
    rig.r.call("docs.state", all="restore")
    rig.r.call("docs.arrange", mode="cascade")
    ri.open_docs(rig, count=max(0, want - len(ri.doc_ids(rig))))
    time.sleep(1.0)
    if not check("restore_cascade"):
        rig.r.call("docs.close", all=True)
        time.sleep(0.5)
        ri.open_docs(rig, count=want)
        time.sleep(1.0)
        check("reopen_%d" % want)
    style = rig.r.call("ui.style") or {}  # 읽기만(set 없음) — 설정값을 바꾸지 않는다
    ok = tries[-1].get("ok") is True
    return {"tries": tries, "ui_style": {k: style.get(k) for k in ("setting", "effective")}, "shown_all": ok}, ok


def busy_idle(state):
    """보이는 스코프가 없고 살아 있는 타이머(TIMER_KEYS)가 전부 0 이다. spinnerFrames 같은 누적값은 보지 않는다."""
    scopes = [s for s in state.get("scopes") or [] if s.get("visible") or s.get("count")]
    return not scopes and ri.timers_zero(state.get("timers"))


def health(rig):
    """/api/health(토큰 불필요)의 restore 값. 앱이 기동 복원 판단을 끝내면(StartupRestoreDone=true 직전) restored·skipped:*·failed:* 가 채워지고 그 전에는 null 이다."""
    try:
        import pdctl  # retest_remote 가 경로를 더한 뒤라 같은 클라이언트를 쓴다
        return json.loads(pdctl.curl(rig.r.info, "api/health", "{}", timeout=5)).get("result") or {}
    except (ValueError, AttributeError, ImportError, SystemExit):
        return {}


def restore_timing(samples, want_docs, launched, connected):
    """폴링 표본(t·docs·signal·idle)에서 복원 완료 시각과 그 뒤 첫 idle 을 고른다. 시각은 time.time() 초.
    복원 완료 = 문서 수가 want_docs 에 처음 도달한 표본(odev-b5 분석: 연결 직후 빈 상태의 idle 을 잡던 거짓 통과 수정). StartupRestoreDone 신호는 기록만 하고
    문서 수 도달 전 신호만으로는 완료로 보지 않는다(신호가 문서 생성보다 앞설 수 있다). idle 은 복원 완료 표본 이후(같은 표본 포함)의 첫 idle 이다."""
    restored = next((s for s in samples if s["docs"] is not None and s["docs"] >= want_docs), None)
    # 첫 표본에서 이미 문서 수에 도달했으면 복원 완료 시각을 모른다(그 전에 끝났다). 0ms 로 통과시키지 않고 판정 불가로 남긴다(r2d 1차 실측 14:46: 연결 10초 뒤 첫 표본이 이미 완료)
    undetermined = bool(samples) and restored is samples[0]
    signal = next((s for s in samples if s["signal"]), None)
    idle = next((s for s in samples if restored and s["t"] >= restored["t"] and s["idle"]), None)
    rel = lambda s: round((s["t"] - launched) * 1000) if s else None  # noqa: E731
    ms = round((idle["t"] - restored["t"]) * 1000) if restored and idle else None
    row = {"want_docs": want_docs, "restored_docs": max([s["docs"] for s in samples if s["docs"] is not None] or [0]),
           "ms_restored_to_idle": ms, "launched_at": datetime.fromtimestamp(launched).astimezone().isoformat(timespec="milliseconds"),
           "restored_at": datetime.fromtimestamp(restored["t"]).astimezone().isoformat(timespec="milliseconds") if restored else None,
           "ms_launch_to_connect": round((connected - launched) * 1000), "ms_launch_to_restore_signal": rel(signal), "restore_signal": (signal or {}).get("signal"),
           "ms_launch_to_restored": rel(restored), "ms_launch_to_idle": rel(idle), "samples": len(samples),
           "early_samples": sum(1 for s in samples if s.get("early")), "ms_launch_to_first_sample": rel(samples[0]) if samples else None, "undetermined": undetermined,
           "note": "복원 시작=앱 재기동 명령 시각(launched), 복원 완료=문서 수 want 도달, idle=그 뒤 첫 idle. 기준 %dms 는 ms_restored_to_idle 에 적용. 첫 표본이 이미 완료면 판정 불가" % IDLE_LIMIT_MS}
    return row, restored is not None and idle is not None and not undetermined and ms <= IDLE_LIMIT_MS


def early_poll(info_path, out, stop):
    """앱 기동 직후(원격 연결 정보 파일이 생기는 즉시)부터 rig.start 가 끝나기를 기다리지 않고 busy.state·docs.list·health 를 0.1초마다 읽는다.
    rig.start 는 창 배치·전경 처리로 연결까지 약 10초가 걸려(r2d 실측) 그 사이 복원이 끝나 버렸다."""
    try:
        import pdctl
    except ImportError:
        return
    info = None
    while not stop.is_set():
        if info is None:
            try:
                with open(info_path, encoding="utf-8-sig") as f:
                    info = json.load(f)
            except (OSError, ValueError):
                time.sleep(0.05)
                continue

        def cmd(name):
            try:
                resp = json.loads(pdctl.curl(info, "api/cmd", json.dumps({"cmd": name, "args": {}}), timeout=3))
            except (ValueError, SystemExit):
                return None
            return resp.get("result") if resp.get("ok") else None
        state, docs = cmd("busy.state"), cmd("docs.list")
        try:
            signal = (json.loads(pdctl.curl(info, "api/health", "{}", timeout=3)).get("result") or {}).get("restore")
        except (ValueError, SystemExit):
            signal = None
        out.append({"t": time.time(), "docs": len(docs) if isinstance(docs, list) else None, "signal": signal,
                    "idle": busy_idle(state) if isinstance(state, dict) else False, "early": True})
        time.sleep(0.1)


def restore_round(rig, want_docs):
    """문서를 want_docs 개로 맞춰 저장해 끝낸 뒤 같은 폴더로 다시 띄워, 복원 완료(문서 수 도달) 뒤 busy 가 idle 로 돌아오는 시간을 잰다."""
    ri.open_docs(rig, count=max(0, want_docs - len(ri.doc_ids(rig))))
    time.sleep(0.8)
    rig.r.call("layout.save")
    time.sleep(0.5)
    rig.r.call("app.shutdown")
    time.sleep(3)
    rig.pid = None
    info_path = os.path.join(rig.data, "remote-control.json")
    if os.path.exists(info_path):
        os.remove(info_path)  # 자기 임시 폴더. 이전 실행 연결 정보로 붙지 않게 먼저 지운다(rig.start 도 지운다)
    samples, stop = [], threading.Event()
    poller = threading.Thread(target=early_poll, args=(info_path, samples, stop), daemon=True)
    launched = time.time()
    poller.start()
    try:
        rig.start(reuse=True)
    finally:
        connected = time.time()
        stop.set()
        poller.join(timeout=10)
    state = {}
    while time.time() - connected < POLL_LIMIT_S:
        state = rig.r.call("busy.state") or {}
        idle_now = busy_idle(state)
        docs = len(ri.doc_ids(rig))
        signal = health(rig).get("restore")
        samples.append({"t": time.time(), "docs": docs, "signal": signal, "idle": idle_now})
        if docs >= want_docs and idle_now:
            break
        time.sleep(0.1)
    row, ok = restore_timing(samples, want_docs, launched, connected)
    row.update(timers=state.get("timers"), history=(state.get("history") or [])[-6:])
    return row, ok


def b5(rig, res):
    """복원 프로필: 문서 수를 늘린 회차까지 전부 2초 안에 idle 이어야 한다."""
    rounds = [restore_round(rig, n) for n in RESTORE_DOC_COUNTS]
    res.add("busy_idle_within_2s", {"limit": IDLE_LIMIT_MS, "rounds": [r[0] for r in rounds], "note": "복원 완료(문서 수 도달) 뒤 첫 idle 까지 잰 값(ms_restored_to_idle). 재기동·연결·복원 신호 시각은 함께 기록"}, all(r[1] for r in rounds))


def a13(rig, res):
    ri.open_docs(rig)
    rig.r.call("tour.start")
    time.sleep(0.8)
    rows = []
    for i in range(3):
        node = rig.find("tour.title") or {}
        rows.append(node.get("text"))
        rig.grab(res, "a13_step%d" % i)
        rig.r.call("tour.next")
        time.sleep(0.6)
    rig.r.call("tour.esc")
    res.add("h61_step_text_readable_capture", {"titles": rows, "note": "글자 읽힘은 otest 캡처 판독이 정한다"}, all(rows))


def expected_keep(win_w, pct, rule=GRAB_RULE_DIP):
    """F08 잔존 기대(DIP, 11-10) = min(창 폭, max(ceil(창 폭 × 비율 − 1e-9), 158)). MdiDragLimit.KeepWhenLeftOut 과 같은 식. pct = placement.state minVisiblePercent(0~90), 0·없음이면 158 그대로."""
    p = min(max(pct or 0, 0), 90) / 100.0
    return min(win_w, max(math.ceil(win_w * p - 1e-9) if p > 0 else 0, rule))


def left_remain(rect, width):
    """창 왼쪽이 영역 밖으로 나간 뒤 보이는 폭(영역 원점 기준)과 비율."""
    visible = rect["x"] + rect["w"]
    return visible, round(visible / width, 3)


def injection_count():
    path = os.path.join(ri.AREA, "logs", "pcaway", "injections.jsonl")  # 옛 Pd 레이아웃 ROOT/poc/ui/logs(2026-11-24 수정)
    try:
        with open(path, encoding="utf-8") as f:
            lines = f.read().splitlines()
    except OSError:
        return 0, None
    return len(lines), (lines[-1][:200] if lines else None)


def f08(rig, res):
    doc = ri.open_docs(rig)[0]
    rig.r.call("placement.markPlaced", doc=doc, x=300, y=120, w=DOC_W, h=DOC_H)
    time.sleep(ri.SETTLE_S)
    state = lambda: rig.r.call("placement.state")  # noqa: E731
    row_of = lambda st: next(d for d in st["docs"] if d["id"] == doc)  # noqa: E731
    st_before = state()
    before = row_of(st_before)
    g = before.get("grab") or {}
    if not g:
        res.add("left_remain_measured", {"grab": None}, False)
        return
    x = before["bounds"]["x"] + (g["left"] + before["bounds"]["w"] - g["rightInset"]) / 2
    area = st_before["area"]
    s = rig.win(rig.main)["dpi"] / 96.0
    frm = (int(area["x"] + x * s), int(area["y"] + (before["bounds"]["y"] + min(16, g["titleBottom"] / 2)) * s))
    to = (int(area["x"] + 8), frm[1])  # 끝점은 MDI 영역 안(영역 왼쪽 바깥은 끌기가 취소되는지 미확인이라 쓰지 않는다)
    inj_before = injection_count()
    rig.click(*frm, drag=to, hwnd=rig.main)
    time.sleep(ri.SETTLE_S)
    inj_after = injection_count()
    st_after = state()
    after = row_of(st_after)
    # 보이는 폭은 화면 rect 기준(영역 원점 대비, DIP). 모델 bounds 의 x+w 는 x≤0 일 때만 맞아 쓰지 않는다
    screen = after.get("screen")
    if screen:
        visible = round((screen["x"] + screen["w"] - st_after["area"]["x"]) / s, 1)
        share = round(visible / DOC_W, 3)
    else:
        visible, share = left_remain(after["bounds"], DOC_W)
    moved = after["bounds"]["x"] < before["bounds"]["x"]
    rig.grab(res, "f08_left")
    diag = {"from_screen": frm, "to_screen": to, "area": st_after["area"], "scale": s,
            "screen_before": before.get("screen"), "screen_after": screen, "sdiFrame_before": before.get("sdiFrame"), "sdiFrame_after": after.get("sdiFrame"),
            "mdiMoved": [st_before.get("mdiMoved"), st_after.get("mdiMoved")], "mdiEvaluations": [st_before.get("mdiEvaluations"), st_after.get("mdiEvaluations")],
            "injections": {"before": inj_before[0], "after": inj_after[0], "last": inj_after[1]},
            "model_vs_screen_moved": {"bounds_x_moved": moved, "screen_x_moved": bool(screen and before.get("screen") and screen["x"] < before["screen"]["x"])}}
    res.add("left_remain_measured", dict({"before": before["bounds"], "after": after["bounds"], "visible_dip": visible, "share": share}, **diag), moved and 0 < share < 1)
    pct = st_before.get("minVisiblePercent", 0)  # 잔존 비율 %(11-10). 키 없는 옛 판본은 0 = 158 그대로
    keep = expected_keep(DOC_W, pct)
    if moved:
        res.add("grab_rule_kept", {"visible_dip": visible, "rule_dip": keep, "min_visible_percent": pct, "tolerance": GRAB_TOL}, visible - 108 >= 50 and visible <= keep + GRAB_TOL)
    else:
        res.add("grab_rule_kept", {"na": "드래그가 창을 옮기지 못해 잔존 폭을 잴 수 없다(PASS 로 두지 않는다)", "visible_dip": visible, "rule_dip": keep}, False)


def scan_signal(root):
    """측정 도구가 띄운 앱 로그 전부에서 단일 인스턴스 신호 줄을 찾는다."""
    logs = [p for pat in ("app_*.log", "product_*.log") for p in glob.glob(os.path.join(root, "**", pat), recursive=True)]
    hits = []
    for path in logs:
        with open(path, encoding="utf-8", errors="replace") as f:
            hits += [{"log": os.path.basename(path), "line": ln.strip()[:160]} for ln in f if any(m in ln for m in SIGNAL_MARKS)]
    return {"logs": len(logs), "hits": hits[:20]}, bool(logs) and not hits


def scan_window_rule(root, monitor):
    """측정 결과 notes 의 test_window 가 전부 규칙 모니터인가. 기록 없는 결과는 값으로 드러낸다."""
    rows = []
    for path in glob.glob(os.path.join(root, "*.json")):
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
        for note in doc.get("notes") or []:
            if isinstance(note, dict) and "test_window" in note:
                rows.append({"item": doc.get("ac_id"), "monitor": note["test_window"].get("monitor")})
    return {"rule": monitor, "rows": rows}, bool(rows) and all(monitor in str(r["monitor"]) for r in rows)


def s2(args, script_path):
    res = MeasureOut("S2", REQ["S2"], args, ri.script_info(script_path), None)
    res.measure("no_signal_to_user_pd", lambda: scan_signal(res.dir))
    res.measure("test_window_monitor_rule", lambda: scan_window_rule(res.dir, args.monitor))
    return 0 if res.write() else 1


B2S_REQ = ["h07_snap_off_frames", "h07_snap_on_frames"]  # H-07(a10) Snap 켬·끔 대조. 판정기 SPEC 밖 이름이라 REQ 에 넣지 않고 drive 에만 넘긴다


def b2s(rig, res):
    """H-07: 새 앱에서 Snap 끔·켬(--snap 순서) 각각 20창 moveAll 커밋 프레임 p95(B2 와 같은 측정·기준). 끝나면 원래 snapEnabled 로 되돌린다.
    한계: docs.moveAll 은 원격 이동이라 포인터 끌기 Snap 판정 경로를 타지 않을 수 있다 — 켬·끔 차이가 없으면 그 사실을 기록한다."""
    orig = ((rig.r.call("snapflyout.state") or {}).get("snapEnabled"))
    modes = [m.strip() for m in (getattr(rig.args, "snap", None) or "off,on").split(",") if m.strip() in ("off", "on")]
    try:
        for mode in modes:
            rig.r.call("snapflyout.options", snapEnabled="true" if mode == "on" else "false")
            time.sleep(0.5)

            def frames(mode=mode):
                prep, shown = b2_prepare(rig)
                if not shown:
                    return {"invalid": B2_INVALID, "prepare": prep, "snap": mode}, False
                value = extra.frame_p95_move(rig)
                value.update(prepare=prep, snap=mode, snap_state=(rig.r.call("snapflyout.state") or {}).get("snapEnabled"), metric="commit_frame_p95")
                return value, value["docs"] >= extra.DOCS_20 and value["commit"]["pass"]
            res.measure("h07_snap_%s_frames" % mode, frames)
    finally:
        if isinstance(orig, bool):
            rig.r.call("snapflyout.options", snapEnabled="true" if orig else "false")


ITEMS = {"B1": b1, "B5": b5, "A13": a13, "F08": f08}


def self_test(args):
    fails = []
    gate = ri.gate_module(args.session)
    if gate is None:
        print("판정기 없음")
        return 1
    for item, names in list(REQ.items()) + [("B2", B2_REQ)]:
        fails += ["%s 이름이 SPEC 밖: %s" % (item, n) for n in names if n not in gate.SPEC[item]["req"]]
    if left_remain({"x": -287, "w": 640}, 640) != (353, 0.552):
        fails.append("왼쪽 잔존 계산 오류")
    if [expected_keep(640, p) for p in (0, None, 25, 26, 90, 95)] != [158, 158, 160, 167, 576, 576] or expected_keep(100, 90) != 100:
        fails.append("F08 잔존 기대(비율) 계산 오류 — 비율 0 은 158 이어야 한다")
    zero = {"spinner": 0, "cursor": 0, "service": 0, "opening": 0}
    if not busy_idle({"scopes": [{"visible": False, "count": 0}], "timers": dict(zero, spinnerFrames=37)}):
        fails.append("누적 spinnerFrames 가 idle 을 막음")
    if busy_idle({"scopes": [{"visible": True, "count": 1}], "timers": zero}):
        fails.append("보이는 스코프가 idle 로 통과함")
    for key in zero:
        if busy_idle({"scopes": [], "timers": dict(zero, **{key: 1})}):
            fails.append("타이머 %s 가 남았는데 idle 로 통과함" % key)
    # B2 직전 20창 표시 확인: perf_ui 뒤(1개만 visible)는 무효, 20개 모두 visible 이면 측정
    one = [{"id": "Grid-%d" % i, "visible": i == 2} for i in range(2, 22)]
    all20 = [{"id": "Grid-%d" % i, "visible": True} for i in range(20)]
    if visible_docs(one, 20) != ({"docs": 20, "visible": 1, "want": 20}, False) or visible_docs(all20, 20)[1] is not True:
        fails.append("B2 20창 표시 확인 계산 오류")
    if visible_docs(all20[:19], 20)[1] or visible_docs(None, 20)[1] or visible_docs([{"visible": "true"}] * 20, 20)[1]:
        fails.append("B2 표시 확인이 문서 부족·응답 없음·문자열 visible 을 통과시킴")
    if busy_idle({"scopes": [], "timers": {}}) or busy_idle({"scopes": [], "timers": None}):
        fails.append("타이머 값 없음이 idle 로 통과함")
    # B5 측정 시작점: 연결 직후 빈 상태의 idle(거짓 통과)을 잡지 않고, 문서 수 도달 뒤 첫 idle 까지 잰다
    s = lambda t, d, sig, idle: {"t": 100.0 + t, "docs": d, "signal": sig, "idle": idle}  # noqa: E731
    for name, samples, want, want_ms, want_ok in (
            ("빈 상태 idle 뒤 복원", [s(0.1, 0, None, True), s(0.5, 2, None, False), s(1.0, 4, "restored", False), s(2.5, 4, "restored", True)], 4, 1500, True),
            ("복원 뒤 2초 초과", [s(0.1, 0, None, True), s(1.0, 4, "restored", False), s(3.2, 4, "restored", True)], 4, 2200, False),
            ("신호가 문서보다 먼저", [s(0.1, 0, "restored", True), s(0.9, 4, "restored", False), s(1.4, 4, "restored", True)], 4, 500, True),
            ("문서 수 미도달", [s(0.1, 0, None, True), s(1.0, 3, "restored", True)], 4, None, False),
            ("복원 뒤 idle 없음", [s(0.1, 0, None, True), s(1.0, 4, "restored", False)], 4, None, False),
            ("첫 표본이 이미 완료(판정 불가)", [s(0.2, 4, "restored", True)], 4, 0, False),
            ("완료 표본이 곧 idle", [s(0.1, 2, None, False), s(0.2, 4, "restored", True)], 4, 0, True)):
        row, ok = restore_timing(samples, want, 100.0, 100.05)
        if row["ms_restored_to_idle"] != want_ms or ok is not want_ok or (want_ms is not None and row["ms_launch_to_restored"] is None):
            fails.append("B5 시작점 판정 오류 %s: ms=%s ok=%s" % (name, row["ms_restored_to_idle"], ok))
    with tempfile.TemporaryDirectory(prefix="pd_ex2_") as d:
        if scan_signal(d)[1]:
            fails.append("로그 0건이 통과함(없음은 실패여야 한다)")
        with open(os.path.join(d, "app_1.log"), "w", encoding="utf-8") as f:
            f.write("allow-duplicate=True\n")
        if not scan_signal(d)[1]:
            fails.append("신호 없는 로그가 실패함")
        with open(os.path.join(d, "product_2.log"), "w", encoding="utf-8") as f:
            f.write("single-instance already running channel=dev signalled=True\n")
        if scan_signal(d)[1]:
            fails.append("신호 줄을 못 찾음")
        if scan_window_rule(d, "DISPLAY3")[1]:
            fails.append("기록 0건이 통과함")
        for name, mon in (("A.json", "\\\\.\\DISPLAY3"), ("B.json", "\\\\.\\DISPLAY2")):
            with open(os.path.join(d, name), "w", encoding="utf-8") as f:
                json.dump({"ac_id": name, "notes": [{"test_window": {"monitor": mon}}]}, f)
        if scan_window_rule(d, "DISPLAY3")[1]:
            fails.append("다른 모니터를 못 찾음")
        ns = argparse.Namespace(phase="final", writer="otest-x")
        root = os.path.join(d, "repo")
        tool = os.path.join(root, "t.py")
        os.makedirs(root)
        with open(tool, "w", encoding="utf-8") as f:
            f.write("x\n")
        out = MeasureOut("B5", REQ["B5"], ns, ri.script_info(tool, root), None, root=root)
        out.write()
        with open(os.path.join(out.dir, "extra", "B5.measure.json"), encoding="utf-8") as f:
            row = json.load(f)["measurements"][0]
        if row["pass"] is not False or not row["source"] or row["value"] != {"unmeasured": True}:
            fails.append("미측정 값이 실패로 남지 않음")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--snap", default="off,on", help="B2S(H-07) Snap 회차 순서 off,on 중 쉼표 목록(기본 off,on). 다른 항목에는 쓰지 않는다")
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    script = os.path.abspath(__file__)
    names = [n.strip() for n in (args.items or "").split(",") if n.strip()]
    code = 0
    if "S2" in names:
        if not args.writer.startswith("otest"):
            print("거부: 결과 writer 는 otest 로 시작해야 한다")
            return 3
        code = s2(args, script)
        names.remove("S2")
    if names:
        args.items = ",".join(names)
        return ri.drive(args, dict(ITEMS, B2S=b2s), dict(REQ, B2S=B2S_REQ), "desktop_work", script, result_cls=MeasureOut) or code
    return code


if __name__ == "__main__":
    sys.exit(main())
