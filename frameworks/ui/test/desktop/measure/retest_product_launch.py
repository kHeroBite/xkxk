#!/usr/bin/env python3
# Desktop 제품판 안전 기동 측정(A06·F11) — 임시 data 폴더에 중복 허용 설정을 미리 두고 사용자 Pd 에 신호 없이 띄워 로그·창·사용자 파일 불변과 로그 패널 숨김을 pcaway_gate 형식으로 남긴다
"""
사용:
  python3 -B retest_product_launch.py --items A06,F11 --run-dir <bin/run/<제품판 복사본>/net10.0> --phase discovery --writer otest-d
  python3 -B retest_product_launch.py --annotate <결과.json> --capture all --read-by otest-d --finding '<판독 내용 10자 이상>'
  python3 -B retest_product_launch.py --self-test
제품판은 --multi-instance 가 컴파일에서 빠진다. 그래서 settings.json {"AllowDuplicateInstance":true} 를 --data-dir 에 미리 둔다(Program.AcquireSingleInstance 가 layout.json 과 같은 폴더의 값을 읽고 TryAcquire·Signal·Listen 을 부르지 않는다).
기동 전에 settings.json 을 쓰지 못하면 띄우지 않는다. 로그에 allow-duplicate=True 가 없거나 single-instance 줄이 있으면 즉시 자기 PID 를 끝내고 실패로 남긴다.
측정: 로그 allow-duplicate=True, single-instance 줄 없음, 사용자 Pd 창 사각형·전경 불변, 사용자 layout.json·settings.json md5 불변, 원격 제어 없음, 첫 화면, 정상 종료.
제품판은 원격 명령이 없어 UIA·실클릭으로 읽는다. 사용자 복귀 판단이면 멈추고 FAIL 로 남긴다.
종료 코드: 0=통과, 1=실패 있음, 2=기동 실패, 3=거부, 5=사용자 복귀.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
from retest_real_input import app, base, away  # noqa: E402

REQ = {
    "A06": ["product_launch_alive", "log_allow_duplicate_true", "mutex_not_taken", "diag_remote_absent", "first_frame_rendered", "clean_exit"],
    "F11": ["product_log_panel_hidden", "view_menu_consistent", "doc_03_records_exception"],
}
UIA = os.path.join(HERE, "uia_invoke.ps1")
DOC_03 = os.path.join(ri.ROOT, "plan", "09_화면설계", "03_데스크톱_화면설계.md")
ALIVE_WAIT_S = 8
CLOSE_WAIT_S = 20
LOG_NAMES = "'로그','Log'"
EXCEPTION_RX = r"제품판[^\n]*로그[^\n]*(숨|예외)|로그[^\n]*제품판[^\n]*(숨|예외)"
FIRST_FRAME_MIN_COLORS = 8

UIA_NAMES_PS = r"""
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$E = [System.Windows.Automation.AutomationElement]
$c = [System.Windows.Automation.PropertyCondition]::new($E::ProcessIdProperty, %(pid)d)
$hit = @()
foreach ($el in $E::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
  if (@(%(names)s) -contains $el.Current.Name) { $hit += $el.Current.ControlType.ProgrammaticName + ':' + $el.Current.Name + ':' + $el.Current.IsOffscreen }
}
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::Out.WriteLine(($hit | ConvertTo-Json -Compress))
"""


UIA_RECT_PS = r"""
Add-Type -Namespace Pd -Name D -MemberDefinition '[DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);'
try { [void][Pd.D]::SetProcessDpiAwareness(2) } catch { }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$E = [System.Windows.Automation.AutomationElement]
$a = [System.Windows.Automation.PropertyCondition]::new($E::ProcessIdProperty, %(pid)d)
$b = [System.Windows.Automation.PropertyCondition]::new($E::AutomationIdProperty, '%(id)s')
$el = $E::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.AndCondition]::new($a, $b))
if ($el) { $r = $el.Current.BoundingRectangle; [Console]::Out.WriteLine(('{0} {1}' -f ($r.X + $r.Width / 2), ($r.Y + $r.Height / 2))) }
"""


class Product:
    """제품판 1개. 원격 명령이 없어 PID 는 명령줄의 임시 폴더로 찾는다. Rig 의 입력 게이트를 함께 쓴다."""

    def __init__(self, args, work):
        self.rig = ri.Rig(args, work)
        self.args, self.work = args, os.path.abspath(work)

    def start(self):
        run_dir = os.path.abspath(self.args.run_dir)
        exe = os.path.join(run_dir, "Pd.Poc.Desktop.exe")
        if "/bin/run/" not in run_dir.replace("\\", "/") or not os.path.exists(exe):
            raise SystemExit("bin/run 복사본만 실행한다: %s" % run_dir)
        stamp = time.strftime("%H%M%S")
        self.data, profile = os.path.join(self.work, "data_" + stamp), os.path.join(self.work, "profile_" + stamp)
        os.makedirs(self.data, exist_ok=True)
        os.makedirs(profile, exist_ok=True)
        with open(os.path.join(self.data, "settings.json"), "w", encoding="utf-8") as f:
            json.dump({"AllowDuplicateInstance": True}, f)
        self.log = os.path.join(self.work, "product_%s.log" % stamp)
        cmd = [exe, "--data-dir", app.win(self.data), "--profile-dir", app.win(profile), "--log", app.win(self.log), "--remote-control"]  # 제품판에서 --remote-control 은 무시돼야 한다
        with open(os.path.join(self.work, "stdio_%s.log" % stamp), "ab") as out:
            subprocess.Popen(cmd, cwd=run_dir, stdin=subprocess.DEVNULL, stdout=out, stderr=out, start_new_session=True)
        for _ in range(45):
            text = self.log_text()
            if "allow-duplicate=" in text:
                break
            time.sleep(1)
        else:
            raise SystemExit("로그에 allow-duplicate 줄이 없다: " + self.log)
        self.rig.pid = self.find_pid()
        self.rig.data = self.data
        if not self.rig.pid:
            raise SystemExit("자기 임시 폴더를 쓰는 프로세스를 찾지 못함 — 사용자 Pd 신호 여부를 로그로 확인할 것: %s" % self.log)
        if self.signal_lines():
            self.stop()
            raise SystemExit("single-instance 줄이 있어 즉시 중단: %s" % self.signal_lines())

    def log_text(self):
        try:
            with open(self.log, encoding="utf-8", errors="replace") as f:
                return f.read()
        except OSError:
            return ""

    def signal_lines(self):
        return [ln.strip() for ln in self.log_text().splitlines() if "single-instance" in ln]

    def find_pid(self):
        text = app.ps("(Get-CimInstance Win32_Process -Filter \"Name='Pd.Poc.Desktop.exe'\" | Where-Object { $_.CommandLine -like '*%s*' }).ProcessId" % app.win(self.data))
        ids = [int(v) for v in text.split() if v.isdigit()]
        return ids[0] if len(ids) == 1 and ids[0] != self.args.user_pid else None

    def alive(self):
        return bool(self.rig.pid) and self.find_pid() == self.rig.pid

    def wait_window(self):
        for _ in range(30):
            hwnd = app.main_hwnd(self.rig.pid)
            if hwnd:
                return hwnd
            time.sleep(1)
        return 0

    def place(self):
        rig = self.rig
        rig.main = self.wait_window()
        if not rig.main:
            return False
        work = rig.monitor["work"]
        base.wp(rig.pid, ["move:%d:%d:%d:%d:%d" % (rig.main, work["r"] - ri.MAIN_W - 180, work["t"] + 150, ri.MAIN_W, ri.MAIN_H)])
        time.sleep(ri.SETTLE_S)
        rig.acquire(rig.main)
        return True

    def stop(self):
        self.rig.stop()


def distinct_colors(path):
    from PIL import Image
    with Image.open(path) as im:
        return len(im.convert("RGB").resize((200, 120)).getcolors(24000) or [])


def graceful_close(p):
    """창 닫기 메시지로 끝낸다. 시간 안에 안 끝나면 실패로 남기고 자기 PID 를 강제 종료한다."""
    rig = p.rig
    rig.native("Add-Type -Namespace Pd -Name C -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'; [void][Pd.C]::PostMessage([IntPtr]%d, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)" % rig.main)
    for _ in range(CLOSE_WAIT_S):
        time.sleep(1)
        if not p.alive():
            rig.pid = None
            text = p.log_text()
            codes = re.findall(r"host exit code=(-?\d+)", text)
            return {"exit": codes[-1:] or None, "fatal": [ln for ln in text.splitlines() if "FATAL" in ln][:3]}, codes[-1:] == ["0"] and "FATAL" not in text
    return {"timeout_s": CLOSE_WAIT_S}, False


def a06(p, res):
    fg_before = p.rig.foreground()
    alive = False
    try:
        p.start()
        time.sleep(ALIVE_WAIT_S)
        alive = p.alive()
    finally:
        res.add("product_launch_alive", {"pid": p.rig.pid, "afterS": ALIVE_WAIT_S}, alive)
    fg_after = p.rig.foreground()
    text = p.log_text()
    allow = re.findall(r"allow-duplicate=(\w+) channel=(\w+)", text)
    res.add("log_allow_duplicate_true", allow[-1:], bool(allow) and allow[-1][0] == "True")
    lines = p.signal_lines()
    user_fg_changed = (fg_after["pid"] == p.args.user_pid) and (fg_before["pid"] != p.args.user_pid)
    res.add("mutex_not_taken", {"singleInstanceLines": lines, "fgBefore": fg_before, "fgAfter": fg_after, "userPdGotForeground": user_fg_changed}, not lines and not user_fg_changed)
    remote = os.path.join(p.data, "remote-control.json")
    res.add("diag_remote_absent", {"remoteControlJson": os.path.exists(remote), "bound": [ln for ln in text.splitlines() if "remote" in ln.lower() and "args:" not in ln][:3]},
            not os.path.exists(remote) and not [ln for ln in text.splitlines() if "remote" in ln.lower() and "args:" not in ln])

    def frame():
        placed = p.place()
        before = len(res.captures)
        p.rig.grab(res, "a06_first_frame")
        shots = [os.path.join(ri.ROOT, c["path"]) for c in res.captures[before:]]
        colors = [distinct_colors(s) for s in shots]
        return {"placed": placed, "colors": colors}, placed and bool(colors) and max(colors) >= FIRST_FRAME_MIN_COLORS
    res.measure("first_frame_rendered", frame)
    res.measure("clean_exit", lambda: graceful_close(p))


def f11(p, res):
    p.start()
    res.notes.append({"allowDuplicate": re.findall(r"allow-duplicate=(\w+)", p.log_text())[-1:], "singleInstance": p.signal_lines()})
    placed = p.place()
    rig = p.rig
    rig.grab(res, "f11_main")

    def uia_names():
        text = ri.ps_encoded(UIA_NAMES_PS % {"pid": rig.pid, "names": LOG_NAMES}, timeout=120)
        return json.loads(text) if text else []
    res.measure("product_log_panel_hidden", lambda: (lambda hits: ({"placed": placed, "uiaLogNames": hits}, placed and not hits))(uia_names()))

    def uia_id(cid):
        return json.loads(ri.run_ps_file(UIA, "-OwnPid", rig.pid, "-Id", cid).stdout.strip().splitlines()[-1])

    def view_menu():
        away.check()
        text = ri.ps_encoded(UIA_RECT_PS % {"pid": rig.pid, "id": "shell.mnuView"}, timeout=120)
        if not text or " " not in text:
            return {"menu": text}, False
        x, y = (int(float(v)) for v in text.split()[:2])
        rig.click(x, y, hwnd=rig.main)
        time.sleep(0.6)
        log, explorer = uia_id("shell.mnuShowLog"), uia_id("shell.mnuShowExplorer")
        rig.grab(res, "f11_view_menu")
        rig.key("Esc")
        return {"at": (x, y), "showLog": log, "showExplorer": explorer}, (not log.get("found")) and bool(explorer.get("found"))
    res.measure("view_menu_consistent", view_menu)

    def doc():
        with open(DOC_03, encoding="utf-8") as f:
            text = f.read()
        m = re.search(EXCEPTION_RX, text)
        return {"line": m.group(0)[:160] if m else None}, bool(m)
    res.measure("doc_03_records_exception", doc)
    res.notes.append({"clean_exit": graceful_close(p)})


ITEMS = {"A06": a06, "F11": f11}


def drive(args):
    if not args.writer.startswith("otest"):
        print("거부: 결과 writer 는 otest 로 시작해야 한다")
        return 3
    if not args.run_dir or not args.items:
        print("거부: --items·--run-dir 가 필요하다")
        return 3
    if away.user_returned():
        print("거부: 복귀 플래그가 있다. 재개는 사용자 재지시 뒤 arm 으로만 한다")
        return 5
    if not ri.resolve_user_pid(args):
        return 3
    names = [n.strip() for n in args.items.split(",") if n.strip()]
    bad = [n for n in names if n not in ITEMS]
    if bad:
        print("미등록 항목: %s" % bad)
        return 3
    build = ri.build_info(args.run_dir, "desktop_product", args.session, final=args.phase == "final")
    info = ri.script_info(os.path.abspath(__file__))
    code = 0
    for name in names:
        res = ri.Result(name, REQ[name], args, info, build)
        work = os.path.join(res.dir, "work", res.run_id)
        os.makedirs(work, exist_ok=True)
        before = ri.snapshot_user(args.user_pid)
        product = Product(args, work)
        returned = False
        try:
            away.check()
            ITEMS[name](product, res)
        except (away.UserReturned, away.GuardUnavailable) as e:
            returned = True
            res.notes.append({"stopped": repr(e)})
        except SystemExit as e:
            res.notes.append({"launch_failed": str(e)})
            print("기동 실패: %s" % e, flush=True)
            code = code or 2
        finally:
            product.stop()
            ri.fill_safety(res, before, ri.snapshot_user(args.user_pid), product.rig)
            if not res.write():
                code = code or 1
        if returned:
            return 5
    return code


def self_test(args):
    fails = ri.self_test_names(REQ, args.session) + ri.self_test_gate(REQ, args.session, "desktop_product", list(REQ))
    for good in ("제품판은 로그 패널을 숨긴다", "로그 패널은 제품판에서 예외로 둔다"):
        if not re.search(EXCEPTION_RX, good):
            fails.append("예외 문구 판정 오류: " + good)
    if re.search(EXCEPTION_RX, "제품판 기동 절차\n로그 숨김"):
        fails.append("예외 문구 판정이 줄을 넘어 맞음")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return drive(args)


if __name__ == "__main__":
    sys.exit(main())
