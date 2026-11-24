#!/usr/bin/env python3
# AC-028 끌기 중 그림자 옵션 — 이미 정식 기동된 자기 인스턴스를 실입력으로 끌며 숨김(기본)→유지→숨김 세 상태를 잰다
"""
사용
  drag_shadow_option_input.py --info <격리 data-dir/remote-control.json> --owner <PD_SLOT_OWNER> \
      --away-state <pc_away.json> --out <결과.json> --monitor <DISPLAYn> --onscreen-approved '<누가·언제>'
  drag_shadow_option_input.py --self-test      # 앱·입력 없이 판정 함수만 시험

앱을 띄우거나 끝내지 않는다. 앱은 run_desktop_bg.sh 로만 `--multi-instance on --data-dir <절대경로 격리>`·화면 안(승인된 PD_ALLOW_ONSCREEN)에 새 프로필로 먼저 띄운다.
안전 전제(하나라도 어긋나거나 조회에 실패하면 입력하지 않는다)
  1. 대상 PID 는 run_desktop_bg.sh 가 --owner 이름으로 기록한 PID 이고, 인자 값과 앱 로그 layout-file 이 격리 폴더다(perf_effect_common.verify_launch).
  2. 입력 직전마다 자기 PID 를 뺀 xkxk.frameworks.ui.*(및 LEGACY 옛 이름 Pd.Poc.*) 프로세스가 0개다.
  3. 입력 직전마다 pc_away_monitor 가 away 이고 user_away_guard.check 를 통과한다.
  4. 전경 게이트·모니터 경계·safe_click 의 ExpectedProcessId 소유권(시작·끝점)은 retest_real_input.Rig 것을 그대로 쓴다.
  키·이동·터치·휠 입력은 쓰지 않는다(safe_click 왼쪽 끌기만).

판정: 끄는 동안(시작·끝 어느 자리도 아닌 표본)의 PART_OuterBorder BoxShadow 가 숨김이면 전부 없음, 유지면 전부 정지 때와 같음.
값만으로 AC PASS 가 아니다. OS 합성 화면(drag_shadow_os_capture)이 세 상태 모두 남으면 rc 0(판독 전), 없으면 rc 4(값 확인 PASS / OS 합성 화면 미확인).
rc 0=값 PASS+OS 캡처(판독 필요) 1=FAIL 2=준비 실패 3=전제 미충족 4=판정 불가(표본 없음·정지 그림자 없음) 5=사용자 복귀·가드 불가(중단)
"""
import argparse
import csv
import json
import os
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import mdi_drag_app as app  # noqa: E402  도우미(win·ps·main_hwnd)만 쓴다 — launch 는 부르지 않는다
import drag_shadow_os_capture as oscap  # noqa: E402
import pc_away_monitor  # noqa: E402
import perf_effect_common as perf  # noqa: E402  첫 조작 전 기동 확인만 쓴다
import retest_maximize_monitor_fg as fg_tool  # noqa: E402  전경·커서 저장/복원
import retest_real_input as real  # noqa: E402  게이트·클릭만 쓴다 — Rig.start 는 부르지 않는다
import user_away_guard as away  # noqa: E402

UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
COMBO = "settings.cmbMoveShadow"
STEPS = (("default_hide", None, "hide"), ("keep", 1, "keep"), ("back_to_hide", 0, "hide"))  # 이름·콤보 순번·기대
DRAG_DIP = (240, 120)
MOVED_DIP = 3.0


class Precondition(Exception):
    """안전 전제가 깨졌다. 입력을 보내지 않는다."""


def other_pd(tasklist_csv, own):
    """tasklist CSV 에서 자기 PID 를 뺀 xkxk.frameworks.ui.*(및 LEGACY 옛 이름 Pd.Poc.*) PID. 자기 PID 조차 없으면 조회를 믿지 않는다."""
    pids = [int(r[1]) for r in csv.reader(tasklist_csv.splitlines())
            if len(r) > 1 and r[0].lower().startswith(("xkxk.frameworks.ui.", "pd.poc")) and r[1].isdigit()]  # "pd.poc" 는 LEGACY(사용자 Pd 앱 Pd.Poc.Desktop.exe) 이름 접두
    if own not in pids:
        raise Precondition("프로세스 목록에 자기 PID %s 가 없다 — 조회를 믿을 수 없다" % own)
    return [p for p in pids if p != own]


def no_shadow(value):
    return (value or "").strip().lower() in ("", "none")


def judge(expect, idle, end, samples):
    """(판정, 사유). samples 는 [{x, y, shadow}], idle·end 도 같은 모양이다."""
    if no_shadow(idle["shadow"]):
        return "UNDET", "정지 상태에 그림자가 없어 숨김과 유지를 가를 수 없다"

    def away_from(s, p):
        return abs(s["x"] - p["x"]) + abs(s["y"] - p["y"]) > MOVED_DIP

    if not away_from(end, idle):
        return "FAIL", "끌기 뒤에도 창이 움직이지 않았다"
    moving = [s for s in samples if away_from(s, idle) and away_from(s, end)]
    if not moving:
        return "UNDET", "끄는 동안의 표본이 없다"
    if expect == "hide":
        bad = [s for s in moving if not no_shadow(s["shadow"])]
    else:
        bad = [s for s in moving if s["shadow"] != idle["shadow"]]
    if bad:
        return "FAIL", "끄는 동안 표본 %d/%d 가 기대(%s)와 다르다" % (len(bad), len(moving), expect)
    if end["shadow"] != idle["shadow"]:
        return "FAIL", "끌기 뒤 정지 그림자로 돌아오지 않았다"
    return "PASS", "끄는 동안 표본 %d개가 기대와 같다" % len(moving)


class OwnRig(real.Rig):
    """정식 기동된 자기 인스턴스에 붙는 Rig. 게이트는 더하기만 한다."""

    def attach(self, info_path):
        self.data = os.path.dirname(os.path.abspath(info_path))
        if os.path.dirname(self.data) != os.path.join(UI, "logs", "proddata"):
            raise Precondition("격리 폴더(logs/proddata/<이름>)의 연결 정보만 받는다: %s" % self.data)
        with open(info_path, encoding="utf-8") as f:
            if int(json.load(f)["port"]) in (5180, 5220):
                raise Precondition("사용자 포트 5180·5220 에는 붙지 않는다")
        os.environ["PD_REMOTE_INFO"] = os.path.abspath(info_path)
        from retest_remote import Remote
        self.r = Remote()
        pid = (self.r.call("status") or {}).get("pid")
        record = os.path.join(UI, "logs", "run_desktop_bg_pids", "%s.txt" % self.args.owner)
        with open(record, encoding="utf-8") as f:
            logs = [p[1] for p in (line.split() for line in f) if len(p) == 2 and p[0] == str(pid)]
        if not logs:
            raise Precondition("PID %s 는 run_desktop_bg.sh 가 %s 이름으로 띄운 기록에 없다" % (pid, self.args.owner))
        cmdline = app.ps('(Get-CimInstance Win32_Process -Filter "ProcessId=%d").CommandLine' % int(pid))
        if logs[-1] not in cmdline or app.win(self.data) not in cmdline:
            raise Precondition("PID %s 명령줄이 자기 기동 기록·격리 폴더와 다르다" % pid)
        if problem := perf.verify_launch(self.r, info_path):
            raise Precondition("기동 확인 실패(조작 없음, 종료는 띄운 쪽): " + problem)
        self.pid = int(pid)
        self.only_own_pd()
        self.main = app.main_hwnd(self.pid)
        rect, work = (self.win(self.main) or {}).get("rect"), self.monitor["work"]
        if not rect or not (work["l"] <= rect["l"] < rect["r"] <= work["r"] and work["t"] <= rect["t"] < rect["b"] <= work["b"]):
            raise Precondition("메인 창이 시험 모니터 작업 영역 안이 아니다: %s" % rect)
        return rect

    def only_own_pd(self):
        done = subprocess.run(["tasklist.exe", "/FO", "CSV", "/NH"], capture_output=True, timeout=30)
        if done.returncode != 0:
            raise Precondition("프로세스 조회 실패 rc=%d" % done.returncode)
        others = other_pd(done.stdout.decode("cp949", "replace"), self.pid)
        if others:
            raise Precondition("다른 Pd 프로세스가 있다: %s" % others)

    def gate(self, hwnd=None):
        self.only_own_pd()
        if pc_away_monitor.check(argparse.Namespace(state=self.args.away_state, max_age=60)) != 0:
            raise away.UserReturned("pc_away_monitor 가 away 가 아니다")
        super().gate(hwnd)

    def need(self, cmd, **kw):
        result = self.r.call(cmd, **kw)
        if result is None:
            raise RuntimeError("원격 명령 실패: %s %s" % (cmd, kw))
        return result

    def border(self, doc):
        found = self.r.call("ui.find", id="PART_OuterBorder", doc=doc, props="BoxShadow")
        if not found:
            return None
        return {"t": time.time(), "x": found["bounds"]["x"], "y": found["bounds"]["y"], "shadow": found["props"]["BoxShadow"]}


def choose(rig, index):
    """설정 창에서 값을 바꾼다(AC-028). index 가 None 이면 읽기만 한다. 돌려주는 값은 콤보 순번."""
    rig.need("sys.open", view="Settings")
    try:
        rig.need("settings.window", category="Window")
        if index is not None:
            rig.need("ui.select", id=COMBO, index=index)
        return rig.need("ui.find", id=COMBO).get("selectedIndex")
    finally:
        rig.need("sys.close", name="settings")  # 설정 창이 남으면 끌기 시작점을 가린다
        time.sleep(real.SETTLE_S)


def drag_once(rig, doc, expect, sign, shot):
    rig.need("docs.activate", doc=doc)
    time.sleep(real.SETTLE_S)
    idle = rig.border(doc)
    title = rig.need("ui.find", id="PART_Grid", doc=doc)
    if not idle:
        return {"verdict": "UNDET", "reason": "정지 상태를 읽지 못했다"}
    x, y = rig.point(rig.main, title["bounds"], 0.4, 0.5)
    scale = rig.win(rig.main)["dpi"] / 96.0
    to = (int(x + sign * DRAG_DIP[0] * scale), int(y + sign * DRAG_DIP[1] * scale))

    samples, done, began = [], threading.Event(), time.time()  # began 보다 오래된 캡처는 이번 증거가 아니다

    def sample():
        captured = False
        while not done.is_set():
            s = rig.border(doc)
            if not s:
                continue
            samples.append(s)
            if not captured and abs(s["x"] - idle["x"]) + abs(s["y"] - idle["y"]) > MOVED_DIP:
                rig.r.call("screenshot", window=0, path=app.win(shot))
                captured = True

    worker = threading.Thread(target=sample, daemon=True)
    worker.start()
    grab = oscap.OsCapture(rig.pid, os.path.dirname(shot), os.path.basename(shot)[:-4])
    try:
        with grab:
            rig.click(x, y, hwnd=rig.main, drag=to)
    finally:
        done.set()
        worker.join(15)
    time.sleep(real.SETTLE_S)
    end = rig.border(doc)
    if not end:
        return {"verdict": "UNDET", "reason": "끌기 뒤 상태를 읽지 못했다", "samples": samples}
    verdict, reason = judge(expect, idle, end, samples)
    os_shots = grab.evidence([s["t"] for s in samples if (s["x"], s["y"]) not in ((idle["x"], idle["y"]), (end["x"], end["y"]))])
    return {"verdict": verdict, "reason": reason, "from": (x, y), "to": to, "idle": idle, "end": end, "os_captures": os_shots,
            "samples": samples, "capture": shot if os.path.exists(shot) and os.path.getmtime(shot) >= began else None}


def restore_desktop(saved):
    """시작 전 전경·커서로 되돌린다."""
    parts = (saved or "").split("|")
    if len(parts) != 4:
        return "저장값 없음"
    return fg_tool.run_ps(["-Action", "restore", "-Hwnd", parts[0], "-X", parts[2], "-Y", parts[3]])


def measure(args, res):
    base = os.path.splitext(os.path.abspath(args.out))[0]
    rig = OwnRig(args, os.path.dirname(base))
    res["window_rect"], res["pid"] = rig.attach(args.info), rig.pid
    away.check()
    saved = fg_tool.run_ps(["-Action", "read"])
    try:
        before = {d.get("id") for d in rig.need("docs.open", kind="grid", count=2)}
        doc = (rig.need("status").get("active") or {}).get("id")
        if doc not in before:
            raise RuntimeError("방금 연 문서가 활성이 아니다: %s" % doc)
        for number, (name, index, expect) in enumerate(STEPS):
            row = {"step": name, "expect": expect}
            res["steps"].append(row)
            row["selected"] = choose(rig, index)
            if row["selected"] != (1 if expect == "keep" else 0):
                row.update(verdict="FAIL", reason="설정 창 값이 기대와 다르다")
                continue
            row.update(drag_once(rig, doc, expect, -1 if number % 2 else 1, "%s_%s.png" % (base, name)))
            row["foreground_leaks"] = len(rig.leaks)
    finally:
        res["restored"] = restore_desktop(saved)


def self_test():
    idle = {"x": 0, "y": 0, "shadow": "0 4 12 0 #40000000"}
    end = dict(idle, x=100)
    mid_off, mid_on = dict(idle, x=50, shadow="none"), dict(idle, x=50)
    cases = [
        judge("hide", idle, end, [mid_off])[0] == "PASS",
        judge("hide", idle, end, [mid_off, mid_on])[0] == "FAIL",
        judge("keep", idle, end, [mid_on])[0] == "PASS",
        judge("keep", idle, end, [mid_off])[0] == "FAIL",
        judge("hide", idle, end, [idle, end])[0] == "UNDET",     # 시작·끝 표본만으로는 통과하지 않는다
        judge("hide", idle, end, [])[0] == "UNDET",
        judge("hide", dict(idle, shadow="none"), end, [mid_off])[0] == "UNDET",
        judge("hide", idle, idle, [mid_off])[0] == "FAIL",
        judge("hide", idle, dict(end, shadow="none"), [mid_off])[0] == "FAIL",
        other_pd('"xkxk.frameworks.ui.desktop.exe","7"\n"cmd.exe","9"', 7) == [],
        other_pd('"xkxk.frameworks.ui.desktop.exe","7"\n"Pd.Poc.Other.exe","8"', 7) == [8],
    ]
    try:
        other_pd("", 7)
        cases.append(False)
    except Precondition:
        cases.append(True)
    print("self-test %s %d/%d" % ("PASS" if all(cases) else "FAIL", sum(cases), len(cases)))
    return 0 if all(cases) else 1


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ("--info", "--owner", "--away-state", "--out", "--monitor", "--onscreen-approved"):
        p.add_argument(name)
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()
    if args.self_test:
        return self_test()
    if not all((args.info, args.owner, args.away_state, args.out, args.monitor, args.onscreen_approved)):
        print("거부: --info·--owner·--away-state·--out·--monitor·--onscreen-approved 가 모두 필요하다")
        return 3
    args.user_pid = None  # 다른 Pd 는 gate 가 매번 0개로 확인한다(safe_key 는 쓰지 않는다)
    res, code = {"ac": "AC-028", "approved": args.onscreen_approved, "steps": []}, 2
    try:
        measure(args, res)
        verdicts = [s.get("verdict") for s in res["steps"]]
        values, seen = verdicts == ["PASS"] * len(STEPS), all(s.get("os_captures") for s in res["steps"])
        code = 1 if "FAIL" in verdicts else 0 if values and seen else 4
        res["result"] = ("값 확인 PASS / " + ("OS 캡처 판독 필요" if seen else "OS 합성 화면 미확인")) if values else "값 확인 미통과"
    except (away.UserReturned, away.GuardUnavailable) as e:
        res["stopped"], code = repr(e), 5
    except (Precondition, real.InputRefused) as e:
        res["refused"], code = str(e), 3
    except (OSError, ValueError, KeyError, RuntimeError, SystemExit, subprocess.SubprocessError) as e:
        res["error"], code = repr(e), 2
    res["rc"] = code
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    summary = [(s["step"], s.get("verdict"), s.get("reason")) for s in res["steps"]]
    print(json.dumps({"rc": code, "steps": summary, "stop": res.get("stopped") or res.get("refused") or res.get("error")}, ensure_ascii=False))
    return code


if __name__ == "__main__":
    sys.exit(main())
