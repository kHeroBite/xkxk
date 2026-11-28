#!/usr/bin/env python3
# PC 비움 실입력 측정(A03·A04·A05·A08·A09·A10·A11·A12) — 진단판 복사본을 임시 폴더로 화면에 띄워 safe_click·safe_key 로 실제 입력을 보내고 pcaway_gate 형식 결과를 쓴다. 기동·입력 게이트·결과 기록은 retest_product_launch·retest_title_strip 도 함께 쓴다
"""
사용:
  python3 -B retest_real_input.py --items A03,A05 --run-dir <bin/run/<복사본>/net10.0> --phase discovery --writer otest-d
  python3 -B retest_real_input.py --annotate <결과.json> --capture <파일 이름|all> --read-by otest-d --finding '<판독 내용 10자 이상>'
  python3 -B retest_real_input.py --self-test
결과: --phase final 은 logs/pcaway/<ID>.json(판정기 대상), discovery 는 logs/pcaway/discovery/<ID>.json(판정기 대상 아님). 캡처는 그 아래 shots/<run_id>/.
캡처의 read_by·finding 은 비워서 쓴다. PNG 를 실제로 읽은 otest 가 --annotate 로 채운다. 그 전에는 판정기가 FAIL 이다.
안전(계획서 2절): 항목마다 새 임시 data·profile·--multi-instance 로 띄우고 자기 PID 만 종료한다. 조작 앞마다 user_away_guard.check, 입력 직전마다 전경 pid 확인.
safe_click 은 주입 기록이 없어 호출 앞뒤를 injection 으로 감싼다. 사용자 Pd 로 입력하지 않고, 모니터 경계 100px 안 좌표는 거부한다.
사용자 복귀 판단이면 즉시 멈추고 그 항목을 FAIL(safety.user_returned=true)로 남긴다. 한 호출로 여러 항목을 돌리면 수 분 걸리므로 setsid nohup 으로 분리해 실행한다.
종료 코드: 0=전 항목 측정 통과, 1=실패 있음, 2=기동·연결 실패, 3=거부, 5=사용자 복귀.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import mdi_drag_app as app  # noqa: E402
import retest_maximize_monitor as base  # noqa: E402
import retest_panel_title_drag as drag_lib  # noqa: E402
import user_away_guard as away  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
AREA = os.path.abspath(os.path.join(HERE, "..", "..", ".."))  # 영역 루트(frameworks/ui). ROOT 는 저장소 루트 의미 그대로 둔다
AREA_REL = os.path.relpath(AREA, ROOT)  # 저장소 루트 기준 영역 상대경로("frameworks/ui"). 옛 Pd 레이아웃의 "poc/ui" 자리(자기시험 임시 트리도 이 구조를 흉내 낸다)
SAFE_CLICK = os.path.abspath(os.path.join(HERE, "..", "..", "..", "tools", "safe_click.ps1"))  # 영역 tools/ 사본(옛 Pd:.claude/scripts, 2026-11-24 이전)
SAFE_KEY = os.path.join(HERE, "safe_key.ps1")
FG_INFO = os.path.join(HERE, "..", "..", "common", "measure", "fg_info.ps1")
GRAB = os.path.join(HERE, "..", "..", "common", "measure", "screen_grab_client.ps1")
USER_PD_PATH_MARK = "\\bin\\run\\otest_final\\"  # 사용자가 쓰는 Pd 의 실행 경로 조각(소문자). otest_final3 같은 복사본과 구별하려고 끝 구분자까지 본다
EDGE_MARGIN = 100
MAIN_W, MAIN_H = 1400, 900
SETTLE_S = 0.8
MAX_SPINNER_DIP, MAX_OPACITY = 32.0, 0.5  # desktop/judge/ui_req2_8_rules.py 와 같은 기준
LP, BP, NP = drag_lib.LP, drag_lib.BP, drag_lib.NP
HUMAN = {"A11": ["H-2-1 커서 체감", "H-2-2 오버레이가 눈에 띄는 정도"], "A12": ["H-3-1 버튼 사용 감각", "H-4-1 재결합 위치 최종 눈 확인", "분할선·끌기 체감"]}
REQ = {
    "A03": ["popup_main_title", "popup_sdi_title", "popup_mdi_title", "popup_title_tab", "nc_rbutton_pd_menu_only"],
    "A04": ["minimized_help_restores", "tray_hidden_help_restores", "window_visible_observed"],
    "A05": ["f1_main", "f1_mdi", "f1_sdi", "esc_ends_tour", "foreground_gate_all_passed"],
    "A08": ["real_rclick_main", "real_rclick_sdi", "real_rclick_mdi", "real_rclick_tab", "alt_space_menu", "caption_min", "caption_max", "caption_close"],
    "A09": ["real_drag_explorer", "real_drag_log", "real_drag_notify", "dock_guide_visible", "splitter_drag", "panel_kept_after_drop"],
    "A10": ["sdi_not_pushed_by_panel", "spinner_hidden_outside_window", "tab_drag_to_other_window", "panel_only_window_minimize", "panel_only_window_maximize", "panel_only_window_menu"],
    "A11": ["h21_cursor_is_spinner_on_busy", "h21_cursor_restored", "h22_overlay_size_opacity"],
    "A12": ["h31_button_real_click_expand", "h31_button_real_click_restore", "h41_redock_same_position_real"],
}


def timers_zero(timers):
    """AC-2-06 과 같은 키(ui_req2_8_rules.TIMER_KEYS)만 본다. spinnerFrames 는 누적 프레임 수라 타이머가 아니다. 값이 없으면 거짓이다."""
    from ui_req2_8_rules import TIMER_KEYS
    return bool(timers) and all(timers.get(k) == 0 for k in TIMER_KEYS)


class InputRefused(Exception):
    """입력을 보내지 않았다(전경 불일치·좌표 금지·safe_click 거부). 그 측정만 실패로 남기고 다음으로 간다."""


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def now_iso():
    return time.strftime("%Y-%m-%dT%H:%M:%S%z")[:-2] + ":" + time.strftime("%z")[-2:]


def rel(path, root=ROOT):
    return os.path.relpath(os.path.abspath(path), root).replace(os.sep, "/")


def run_ps_file(script, *args, timeout=90):
    return subprocess.run([app.PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", app.win(script)] + [str(a) for a in args],
                          stdin=subprocess.DEVNULL, capture_output=True, encoding="utf-8", errors="replace", timeout=timeout)


def ps_encoded(script, timeout=60):
    """한글·따옴표가 안전하도록 UTF-16 인코딩으로 넘긴다."""
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    return subprocess.run([app.PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
                          stdin=subprocess.DEVNULL, capture_output=True, encoding="utf-8", errors="replace", timeout=timeout).stdout.strip()


NATIVE_PS = r"""
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PdN {
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct CI { public int cb, flags; public IntPtr h; public PT p; }
  [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CI c);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
  [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
}
'@
try { [void][PdN]::SetProcessDpiAwareness(2) } catch { }
"""

GUIDE_PS = r"""
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PdG {
  public delegate bool E(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(E e, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
  public static int Count(uint pid) { int n = 0; EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
}
'@
try { [void][PdG]::SetProcessDpiAwareness(2) } catch { }
$b = [PdG]::Count(%(pid)d)
[Console]::Out.WriteLine('READY'); [Console]::Out.Flush()
$sw = [Diagnostics.Stopwatch]::StartNew(); $seen = $false; $max = $b
while ($sw.ElapsedMilliseconds -lt %(limit)d) {
  $c = [PdG]::Count(%(pid)d); if ($c -gt $max) { $max = $c }
  if ($c -gt $b) { $seen = $true; Start-Sleep -Milliseconds 200; break }
  Start-Sleep -Milliseconds 25
}
if ($seen) {
  $bmp = New-Object System.Drawing.Bitmap %(w)d, %(h)d; $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(%(x)d, %(y)d, 0, 0, $bmp.Size); $bmp.Save('%(path)s', [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
[Console]::Out.WriteLine(('{"seen":' + $seen.ToString().ToLower() + ',"base":' + $b + ',"max":' + $max + ',"ms":' + $sw.ElapsedMilliseconds + '}'))
"""


def dry_endpoint_verdict(end, client, owner, pid, work, allow_outside_main=False):
    """dry-run 사전 판정(순수 함수). 소유자가 시험 앱이어야 하고, 메인 창 클라이언트 안이거나(--allow-outside-main 이면) 시험 모니터 작업 영역 안이어야 통과한다. 작업 영역 밖은 항상 거부"""
    inside = bool(client) and client["l"] <= end[0] < client["r"] and client["t"] <= end[1] < client["b"]
    in_work = bool(work) and work["l"] <= end[0] < work["r"] and work["t"] <= end[1] < work["b"]
    own = owner == pid
    ok = own and (inside or (allow_outside_main and in_work))
    return ok, inside, in_work


class Rig:
    """자기 앱 1개를 화면에 띄우고, 게이트를 거친 실입력·읽기·캡처를 제공한다."""

    def __init__(self, args, work):
        self.args, self.work = args, os.path.abspath(work)
        self.pid = self.main = self.r = self.data = None
        self.gates, self.leaks, self.dry_points = [], [], []
        self.monitors = base.wp(0, ["monitors"])[0]["monitors"]
        self.monitor = next((m for m in self.monitors if args.monitor in m["device"]), None)
        if not self.monitor:
            raise SystemExit("모니터 %s 없음: %s" % (args.monitor, [m["device"] for m in self.monitors]))

    def start(self, extra=(), reuse=False):
        """reuse=True 면 직전 임시 data·profile 로 다시 띄운다(저장 레이아웃 복원 측정)."""
        run_dir = os.path.abspath(self.args.run_dir)
        exe = os.path.join(run_dir, _paths.get("desktop.exe_name"))
        if "/bin/run/" not in run_dir.replace("\\", "/") or not os.path.exists(exe):
            raise SystemExit("bin/run 복사본만 실행한다: %s" % run_dir)
        stamp = time.strftime("%H%M%S")
        if not reuse:
            self.data, self.profile = os.path.join(self.work, "data_" + stamp), os.path.join(self.work, "profile_" + stamp)
        else:
            info_old = os.path.join(self.data, "remote-control.json")
            if os.path.exists(info_old):
                os.remove(info_old)  # 이전 실행의 연결 정보로 붙지 않게 지운다(자기 임시 폴더)
        profile = self.profile
        os.makedirs(self.data, exist_ok=True)
        os.makedirs(profile, exist_ok=True)
        self.log = os.path.join(self.work, "app_%s.log" % stamp)
        cmd = [exe, "--multi-instance", "on", "--remote-control", "--data-dir", app.win(self.data), "--profile-dir", app.win(profile), "--log", app.win(self.log)] + list(extra)
        with open(os.path.join(self.work, "stdio_%s.log" % stamp), "ab") as out:
            subprocess.Popen(cmd, cwd=run_dir, stdin=subprocess.DEVNULL, stdout=out, stderr=out, start_new_session=True)
        info = os.path.join(self.data, "remote-control.json")
        for _ in range(90):
            bad = app.log_violation(self.log)
            if bad:
                raise SystemExit("단일 인스턴스 흔적 — 즉시 중단: " + bad)
            if os.path.exists(info):
                break
            time.sleep(1)
        else:
            raise SystemExit("연결 정보 파일이 생기지 않았다: " + info)
        os.environ["PD_REMOTE_INFO"] = info  # 사용자 인스턴스에 붙지 않게 임시 폴더 파일만 쓴다
        from retest_remote import Remote
        self.r = Remote()
        status = None
        for _ in range(60):
            status = self.r.call("status")
            if status and status.get("pid"):
                break
            time.sleep(1)
        self.pid = (status or {}).get("pid")
        if not self.pid or app.win(self.data) not in app.ps('(Get-CimInstance Win32_Process -Filter "ProcessId=%s").CommandLine' % self.pid):
            raise SystemExit("연결된 앱의 명령줄에 임시 폴더가 없다 — 남의 프로세스일 수 있어 중단: pid=%s" % self.pid)
        if self.pid == self.args.user_pid:
            raise SystemExit("사용자 Pd 에 연결됨 — 중단")
        self.main = app.main_hwnd(self.pid)
        work = self.monitor["work"]
        base.wp(self.pid, ["move:%d:%d:%d:%d:%d" % (self.main, work["r"] - MAIN_W - 180, work["t"] + 150, MAIN_W, MAIN_H)])
        time.sleep(SETTLE_S)
        self.acquire(self.main)

    def stop(self):
        if self.pid:
            print(app.stop(self.pid, os.path.basename(self.data)), flush=True)
            self.pid = None
            time.sleep(1.5)

    # 읽기 -----------------------------------------------------------------
    def windows(self):
        return base.wp(self.pid, ["list"])[0]["windows"]

    def win(self, hwnd):
        return next((w for w in self.windows() if w["hwnd"] == hwnd), None)

    def foreground(self):
        text = run_ps_file(FG_INFO).stdout
        m = re.search(r"fg hwnd=(-?\d+) pid=(\d+)", text)
        return {"hwnd": int(m.group(1)), "pid": int(m.group(2))} if m else {"hwnd": 0, "pid": 0}

    def acquire(self, hwnd):
        """전경 획득(AttachThreadInput→SetForegroundWindow). 사용자 Pd 창은 대상이 될 수 없다(자기 창 hwnd 만 넘긴다)."""
        run_ps_file(FG_INFO, "-Restore", hwnd)
        time.sleep(0.2)
        return self.foreground()

    def sdi_hwnd(self, doc):
        row = next((s for s in self.r.call("window.snapshot")["sdi"] if s.get("doc") == doc), None)
        if not row:
            return None
        for w in self.windows():
            if w["cls"] != base.SHADOW and abs(w["rect"]["l"] - row["x"]) <= 2 and abs(w["rect"]["t"] - row["y"]) <= 2:
                return w["hwnd"]
        return None

    def find(self, cid=None, **scope):
        return self.r.call("ui.find", id=cid, **scope) if cid else self.r.call("ui.find", **scope)

    def point(self, hwnd, bounds, fx=0.5, fy=0.5):
        """창 DIP 사각형 안 비율 위치 → 화면 물리 좌표."""
        w = self.win(hwnd)
        s = w["dpi"] / 96.0
        return int(w["client"]["l"] + (bounds["x"] + bounds["w"] * fx) * s), int(w["client"]["t"] + (bounds["y"] + bounds["h"] * fy) * s)

    def dip_point(self, hwnd, x, y):
        return self.point(hwnd, {"x": x, "y": y, "w": 0, "h": 0})

    def native(self, body):
        return ps_encoded(NATIVE_PS + body)

    def cursor_handle(self):
        text = self.native("$c = New-Object PdN+CI; $c.cb = [Runtime.InteropServices.Marshal]::SizeOf($c); [void][PdN]::GetCursorInfo([ref]$c); [int64]$c.h")
        return int(text.split()[-1]) if text else 0

    def point_owner(self, x, y):
        text = self.native("$p = New-Object PdN+PT; $p.X = %d; $p.Y = %d; $h = [PdN]::GetAncestor([PdN]::WindowFromPoint($p), 2); $o = 0; [void][PdN]::GetWindowThreadProcessId($h, [ref]$o); $o" % (x, y))
        return int(text.split()[-1]) if text else 0

    def show(self, hwnd, cmd=9):
        """자기 창 복원(SW_RESTORE). 최대화 뒤 버튼이 경계 100px 안에 들어가 실입력으로 못 되돌릴 때 쓴다."""
        if not self.win(hwnd):
            raise InputRefused("자기 창이 아니다: %s" % hwnd)
        self.native("[void][PdN]::ShowWindow([IntPtr]%d, %d)" % (hwnd, cmd))
        time.sleep(SETTLE_S)

    def pixel(self, x, y):
        return self.native("$d = [PdN]::GetDC([IntPtr]::Zero); $v = [PdN]::GetPixel($d, %d, %d); [void][PdN]::ReleaseDC([IntPtr]::Zero, $d); '{0:X6}' -f $v" % (x, y)).split()[-1:]

    # 입력 게이트 ------------------------------------------------------------
    def check_coord(self, x, y):
        for m in self.monitors:
            b = m["bounds"]
            if b["l"] <= x < b["r"] and b["t"] <= y < b["b"]:
                if min(x - b["l"], b["r"] - 1 - x, y - b["t"], b["b"] - 1 - y) < EDGE_MARGIN:
                    raise InputRefused("모니터 경계 %dpx 안 좌표 거부: (%d,%d) %s" % (EDGE_MARGIN, x, y, m["device"]))
                return
        raise InputRefused("어느 모니터에도 없는 좌표: (%d,%d)" % (x, y))

    def gate(self, hwnd=None):
        away.check()
        fg = self.foreground()
        if fg["pid"] != self.pid or (hwnd and fg["hwnd"] != hwnd):
            fg = self.acquire(hwnd or self.main)
        ok = fg["pid"] == self.pid
        self.gates.append({"t": time.strftime("%H:%M:%S"), "fg": fg, "pass": ok})
        if not ok:
            raise InputRefused("전경이 시험 앱이 아니다: %s" % fg)

    def after_input(self):
        fg = self.foreground()
        if fg["pid"] == self.args.user_pid:
            self.leaks.append({"t": time.strftime("%H:%M:%S"), "fg": fg})

    def click(self, x, y, button="Left", hwnd=None, drag=None, no_click=False, expected=None):
        """expected 는 --NoClick 커서 이동 때 그 점의 실제 소유자를 넘길 때만 쓴다. 클릭·끌기는 언제나 자기 PID 다."""
        self.check_coord(x, y)
        if drag:
            self.check_coord(*drag)
        if getattr(self.args, "dry_run_endpoints", False):
            # 사전 판정(dry-run): 입력을 보내지 않고 끝점이 메인 창 클라이언트 안인지·소유자가 시험 앱인지만 기록한다. safe_click·user_away_guard 는 건드리지 않는다
            end = drag or (x, y)
            cl = (self.win(self.main) or {}).get("client") or {}
            owner = self.point_owner(*end)
            ok, inside, in_work = dry_endpoint_verdict(end, cl, owner, self.pid, self.monitor.get("work"), getattr(self.args, "allow_outside_main", False))
            self.dry_points.append({"from": (x, y), "end": end, "kind": "drag" if drag else "click", "inside_main_client": inside, "inside_monitor_work": in_work, "allow_outside_main": bool(getattr(self.args, "allow_outside_main", False)), "owner_pid": owner, "own": owner == self.pid, "verdict": "pass" if ok else "refuse"})
            if not ok:
                raise InputRefused("dry-run 사전 판정: 끝점 %s 거부(inside_main=%s inside_work=%s owner=%s allow_outside_main=%s)" % (end, inside, in_work, owner, getattr(self.args, "allow_outside_main", False)))
            return
        if no_click:
            away.check()
        else:
            self.gate(hwnd)
        args = ["-ScreenX", x, "-ScreenY", y, "-ExpectedProcessId", expected if (no_click and expected) else self.pid, "-Button", button]
        if drag:
            args += ["-DragToX", drag[0], "-DragToY", drag[1]]
        if no_click:
            args.append("-NoClick")
        end = drag or (x, y)
        kind = "move" if no_click else ("drag" if drag else "click")
        with away.injection(kind, end[0], end[1]):
            done = run_ps_file(SAFE_CLICK, *args)
            if done.returncode != 0:
                raise InputRefused("safe_click rc=%s: %s" % (done.returncode, (done.stderr or done.stdout)[-300:]))
        if not no_click:
            self.after_input()
        time.sleep(0.4)

    def key(self, name, hwnd=None):
        self.gate(hwnd)
        # 주입 기록은 safe_key 안에서 한다. 사용자 Pd 가 없으면 금지할 PID 가 없고 safe_key 는 1 이상을 요구하므로 System(4, 입력 대상이 될 수 없는 PID)을 넣는다
        done = run_ps_file(SAFE_KEY, "-Key", name, "-ExpectedProcessId", self.pid, "-ForbiddenProcessId", self.args.user_pid or 4)
        if done.returncode != 0:
            raise InputRefused("safe_key rc=%s: %s" % (done.returncode, (done.stderr or done.stdout)[-300:]))
        self.after_input()
        time.sleep(0.5)

    def click_ctl(self, hwnd, button="Left", **find):
        b = self.find(**find)
        if not b or not b.get("effectivelyVisible"):
            raise InputRefused("대상 컨트롤을 찾지 못함: %s" % (find,))
        x, y = self.point(hwnd, b["bounds"])
        self.click(x, y, button=button, hwnd=hwnd)
        return x, y

    def outside_point(self):
        """시험 앱·사용자 Pd 어느 쪽도 아닌 창 위의 점. 없으면 None. (점, 소유자 PID)"""
        b = self.monitor["bounds"]
        for x in range(b["l"] + EDGE_MARGIN + 20, b["r"] - EDGE_MARGIN, 120):
            for y in range(b["t"] + EDGE_MARGIN + 20, b["b"] - EDGE_MARGIN, 240):
                owner = self.point_owner(x, y)
                if owner and owner not in (self.pid, self.args.user_pid):
                    return (x, y), owner
        return None, None

    # 캡처 -----------------------------------------------------------------
    def grab(self, res, tag):
        if self.foreground()["pid"] != self.pid:
            self.acquire(self.main)
        os.makedirs(res.shots, exist_ok=True)
        text = run_ps_file(GRAB, "-OutDir", app.win(res.shots), "-Tag", tag, "-ProcessId", self.pid).stdout
        shadow = {w["hwnd"] for w in self.windows() if w["cls"] == base.SHADOW}
        for m in re.finditer(r"hwnd=(-?\d+) .*?file=(.+\.png)", text):
            path = subprocess.check_output(["wslpath", "-u", m.group(2).strip()], text=True).strip()
            if int(m.group(1)) in shadow:
                os.remove(path)
            else:
                res.capture(path)

    def watch_guide(self, res, tag, limit_ms=15000):
        """끌기 동안 새로 뜨는 안내·미리 보기 창을 PowerShell 안에서 곧바로 잡아 캡처한다(safe_click 의 끌기는 1.5초 안에 끝난다)."""
        os.makedirs(res.shots, exist_ok=True)
        c = self.win(self.main)["client"]
        path = os.path.join(res.shots, tag + ".png")
        script = GUIDE_PS % {"pid": self.pid, "limit": limit_ms, "w": c["r"] - c["l"], "h": c["b"] - c["t"], "x": c["l"], "y": c["t"], "path": app.win(path)}
        encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
        proc = subprocess.Popen([app.PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, encoding="utf-8", errors="replace")
        proc.stdout.readline()  # READY: 기준 창 수를 센 뒤에 끌기를 시작한다
        return proc, path

    def finish_guide(self, proc, path, res):
        try:
            out, _ = proc.communicate(timeout=30)
        except subprocess.TimeoutExpired:
            proc.kill()
            return {"seen": False, "error": "watcher timeout"}
        try:
            value = json.loads(out.strip().splitlines()[-1])
        except (ValueError, IndexError):
            value = {"seen": False, "raw": out[-200:]}
        if value.get("seen") and os.path.exists(path):
            res.capture(path)
        return value


SCALAR_KEYS = ("ratio", "h")  # 수치 기준 측정의 상세값에서 판정 값으로 쓰는 키(T3 strip_ratio·tab_ratio 는 ratio, *_height_before 는 h)
_EXPECT_CACHE = {}


def gate_expect(session, item):
    """판정기 SPEC 의 expect(이름 → (op, 기준)). 판정기가 없거나 항목이 없으면 None."""
    if not session:
        return None
    if session not in _EXPECT_CACHE:
        try:
            _EXPECT_CACHE[session] = gate_module(session)
        except Exception:  # noqa: BLE001 — 판정기 적재 실패는 변환 생략으로 둔다(값은 원래대로 남아 판정기가 FAIL 한다)
            _EXPECT_CACHE[session] = None
    module = _EXPECT_CACHE[session]
    spec = module.SPEC.get(item) if module else None
    return dict(spec.get("expect") or {}) if spec else None


class Result:
    """pcaway_gate 형식 결과 1건. 측정은 add 로만 넣고, 빠진 필수 이름은 실패로 채운다."""

    def __init__(self, ac, req, args, script, build, root=ROOT, human=None, out_dir=None):
        self.ac, self.req, self.args, self.root = ac, list(req), args, root
        self.measurements, self.captures, self.notes = [], [], []
        self.human = list(human or [])
        self.run_id = "%s_%s_%d" % (ac, time.strftime("%Y%m%dT%H%M%S"), os.getpid())
        self.started_at = now_iso()
        self.script, self.build = script, build
        sub = "" if args.phase == "final" else "discovery"
        self.dir = out_dir or os.path.join(root, AREA_REL, "logs", "pcaway", sub)  # root 기본값 ROOT 면 AREA/logs/pcaway
        self.shots = os.path.join(self.dir, "shots", self.run_id)
        self.safety = {"user_pd_untouched": False, "user_files_same": False, "input_leak": True, "user_returned": away.user_returned()}
        self.expect = gate_expect(getattr(args, "session", None), ac)

    def shaped(self, m):
        """판정기 expect 가 있는 측정만 판정기 형식 {value, source} 로 감싼다(otest-9 11-07). source 는 도구가 남긴 상세값 그대로다.
        불리언 기준(== True)의 value 는 도구 자신의 통과 판단(pass), 수치 기준은 상세값의 ratio·h 다. 기준값은 바꾸지 않으며 못 고르면 그대로 둔다."""
        rule, value = (self.expect or {}).get(m["name"]), m["value"]
        if not rule or (isinstance(value, dict) and set(value) == {"value", "source"}):
            return m
        want = rule[1]
        if isinstance(want, bool):
            return dict(m, value={"value": m["pass"] is True, "source": value})
        if isinstance(value, dict):
            scalar = next((value[k] for k in SCALAR_KEYS if isinstance(value.get(k), (int, float)) and not isinstance(value.get(k), bool)), None)
            if scalar is not None:
                return dict(m, value={"value": scalar, "source": value})
        return m

    def add(self, name, value, ok):
        self.measurements = [m for m in self.measurements if m["name"] != name]
        self.measurements.append({"name": name, "value": value, "pass": bool(ok)})
        print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(value, ensure_ascii=False, default=str)[:300], flush=True)

    def measure(self, name, fn):
        """fn 은 (값, 통과) 를 돌려준다. 입력 거부는 그 측정의 실패로 남긴다. 복귀 판단(BaseException)은 그대로 올린다."""
        try:
            value, ok = fn()
        except InputRefused as e:
            value, ok = {"refused": str(e)}, False
        except (KeyError, TypeError, IndexError, ValueError, AttributeError, subprocess.SubprocessError, OSError) as e:
            value, ok = {"error": repr(e)}, False
        self.add(name, value, ok)
        return ok

    def capture(self, path):
        self.captures.append({"path": rel(path, self.root), "sha256": sha256(path), "read_by": "", "finding": ""})

    def write(self, build_required=True):
        names = {m["name"] for m in self.measurements}
        for name in self.req:
            if name not in names:
                self.add(name, {"unmeasured": True}, False)
        passed = all(m["pass"] for m in self.measurements) and bool(self.measurements)
        doc = {"ac_id": self.ac, "verdict": "PASS" if passed and not self.safety["user_returned"] else "FAIL", "writer": self.args.writer, "run_id": self.run_id,
               "started_at": self.started_at, "finished_at": now_iso(), "script": self.script, "build": self.build, "measurements": [self.shaped(m) for m in self.measurements],
               "captures": self.captures, "human_remaining": self.human, "safety": self.safety, "phase": self.args.phase, "notes": self.notes}
        os.makedirs(self.dir, exist_ok=True)
        path = os.path.join(self.dir, self.ac + ".json")
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
        os.replace(tmp, path)
        print("결과 %s verdict=%s 캡처 %d장(판독 전)" % (path, doc["verdict"], len(self.captures)), flush=True)
        return passed


def script_info(path, root=ROOT):
    return {"path": rel(path, root), "sha256": sha256(path)}


def build_info(run_dir, role, session, root=ROOT, name=_paths.get("desktop.dll_name"), final=False):
    """최종 빌드 목록에 그 역할이 있으면 그 값을 쓰고, 실행 복사본의 같은 파일과 sha 가 다르면 거부한다."""
    local = os.path.join(os.path.abspath(run_dir), name)
    manifest = None
    try:
        with open(os.path.join(session or "", "plans", "pcaway_final_build.json"), encoding="utf-8") as f:
            manifest = json.load(f).get(role)
    except (OSError, ValueError):
        pass
    if manifest:
        target = os.path.join(root, manifest["path"]) if not os.path.isabs(manifest["path"]) else manifest["path"]
        same = os.path.exists(local) and sha256(local) == manifest["sha256"]
        if final and not same:
            raise SystemExit("실행 복사본이 최종 빌드(%s)와 다르다: %s" % (role, local))
        return {"path": rel(target, root), "sha256": manifest["sha256"]}
    if final:
        raise SystemExit("최종 빌드 목록(%s)이 없어 final 결과를 쓸 수 없다" % role)
    return {"path": rel(local, root), "sha256": sha256(local)} if os.path.exists(local) else {"path": rel(local, root), "sha256": ""}


class UserPdUnknown(Exception):
    """사용자 Pd 를 하나로 확정하지 못했다. 입력을 보낼 수 없다."""


def pick_user_pid(rows, explicit=None):
    """데스크톱 호스트 프로세스 목록에서 사용자 Pd 를 고른다. 시험 인스턴스(--data-dir)는 제외한다. 0개면 None(사용자 Pd 없음), 2개 이상·지정 PID 불일치는 UserPdUnknown 이다."""
    found = [r["ProcessId"] for r in rows if USER_PD_PATH_MARK in str(r.get("ExecutablePath") or "").lower() and "--data-dir" not in str(r.get("CommandLine") or "")]
    if explicit is not None:
        if explicit not in found:
            raise UserPdUnknown("지정 PID %s 가 사용자 Pd 경로의 프로세스가 아니다(찾은 PID %s)" % (explicit, found))
        return explicit
    if not found:
        return None  # 사용자 Pd 없음(team-lead 계약 해석 11-07): 입력 게이트는 언제나 자기 PID 만 통과시킨다
    if len(found) != 1:
        raise UserPdUnknown("경로 %s 의 프로세스가 %d개다(%s)" % (USER_PD_PATH_MARK, len(found), found))
    return found[0]


def user_pd_path_pids(rows):
    """사용자 Pd 실행 경로의 프로세스 전부(시험 인스턴스 포함). absent_both 판정은 이것이 시작·끝 모두 빈 목록일 때만 인정한다."""
    return sorted(r["ProcessId"] for r in rows if USER_PD_PATH_MARK in str(r.get("ExecutablePath") or "").lower())


def query_user_pd_rows():
    out = app.ps("Get-CimInstance Win32_Process -Filter \"Name='%s'\" | Select-Object" % _paths.get("desktop.exe_name") + " ProcessId,ExecutablePath,CommandLine,@{n='CreationDate';e={$_.CreationDate.ToString('o')}} | ConvertTo-Json -Compress").strip()
    if not out:
        return []
    rows = json.loads(out)
    return rows if isinstance(rows, list) else [rows]


def resolve_user_pid(args, query=query_user_pd_rows):
    """args.user_pid 를 확정한다. 확정하지 못하면 False 이고 호출자는 시험을 거부한다."""
    try:
        args.user_pid = pick_user_pid(query(), args.user_pid)
    except (UserPdUnknown, ValueError, OSError, subprocess.SubprocessError) as e:
        print("거부: 사용자 Pd 를 확정하지 못해 실입력 시험을 하지 않는다: %s" % e)
        return False
    if args.user_pid is None:
        print("사용자 Pd 없음(경로 %s 프로세스 0개) — 입력 대상은 매번 자기가 띄운 PID 의 창인지 확인한다" % USER_PD_PATH_MARK)
    else:
        print("사용자 Pd PID %d" % args.user_pid)
    return True


def snapshot_user(user_pid, query=None):
    """사용자 Pd 상태. pid·start(CreationDate)·present(사용자 Pd 경로 프로세스 전부)·창 사각형·사용자 파일 md5. 조회 실패면 present=None(판정 불가)."""
    try:
        procs = (query or query_user_pd_rows)()
        present = user_pd_path_pids(procs)
        start = next((str(r.get("CreationDate") or "") or None for r in procs if user_pid and r["ProcessId"] == user_pid), None)
    except (ValueError, OSError, subprocess.SubprocessError, KeyError, TypeError):
        present, start = None, None
    rows = None
    if user_pid:
        try:
            rows = base.wp(user_pid, ["list"])[0]["windows"]
        except (RuntimeError, ValueError, subprocess.SubprocessError):
            rows = None
    return {"t_ms": int(time.time() * 1000), "pid": user_pid or None, "start": start, "present": present,
            "alive": rows is not None and bool(rows), "rects": sorted((w["hwnd"], w["rect"]["l"], w["rect"]["t"], w["rect"]["r"], w["rect"]["b"]) for w in rows or []), "files": base.user_files_md5()}


USER_PD_LOG_START = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+ \S+ System host start")


def user_pd_launches(t0_ms, t1_ms, log_dir=None):
    """측정 구간 [t0, t1] 안에 사용자 데이터 폴더(%LOCALAPPDATA% 아래 데이터 폴더의 logs) 로그에 남은 'host start' 시각 목록. 읽을 수 없으면 None(판정 불가).
    한계: 로그를 남기지 않고 떴다 사라진 프로세스는 이 기록으로 잡지 못한다(시작·끝 present 로만 본다)."""
    if not t0_ms or not t1_ms:
        return None
    try:
        if log_dir is None:
            local = subprocess.run(["cmd.exe", "/c", "echo %LOCALAPPDATA%"], capture_output=True, text=True, timeout=30).stdout.strip()
            log_dir = os.path.join(_paths.data_dir_under(subprocess.run(["wslpath", local], capture_output=True, text=True, timeout=30).stdout.strip()), "logs")
        found = []
        for day in sorted({time.strftime("%Y%m%d", time.localtime(t / 1000.0)) for t in (t0_ms, t1_ms)}):
            path = os.path.join(log_dir, "pd-%s.log" % day)
            if not os.path.exists(path):
                continue
            with open(path, encoding="utf-8", errors="replace") as f:
                for line in f:
                    m = USER_PD_LOG_START.match(line)
                    if m and t0_ms - 2000 <= time.mktime(time.strptime(m.group(1), "%Y-%m-%d %H:%M:%S")) * 1000 <= t1_ms + 2000:
                        found.append(m.group(1))
        return found
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def user_pd_case(before, after, launches):
    """team-lead 계약 해석(11-07): present_untouched = 시작 때 살아 있던 같은 PID 가 같은 시작 시각·같은 창 사각형으로 끝까지 살아 있음.
    absent_both = 시작·끝 모두 사용자 Pd 경로 프로세스 0개이고 측정 중 실행 기록 없음. 그 밖은 None(불충족)."""
    if before.get("pid") and before.get("pid") == after.get("pid") and before.get("alive") and after.get("alive") \
            and before.get("start") and before.get("start") == after.get("start") and [list(r) for r in before["rects"]] == [list(r) for r in after["rects"]]:
        return "present_untouched"
    if not before.get("pid") and not after.get("pid") and before.get("present") == [] and after.get("present") == [] and launches == []:
        return "absent_both"
    return None


def user_pd_safety(before, after, launches="auto"):
    if launches == "auto":
        launches = user_pd_launches(before.get("t_ms"), after.get("t_ms"))
    case = user_pd_case(before, after, launches)
    pick = lambda s: {"pid": s.get("pid"), "start": s.get("start"), "present": s.get("present"), "rects": [list(r) for r in s.get("rects") or []]}  # noqa: E731
    return {"user_pd_untouched": case is not None, "user_pd_case": case, "user_pd_before": pick(before), "user_pd_after": pick(after), "user_pd_launches": launches}


def fill_safety(res, before, after, rig=None):
    leaks = (rig.leaks if rig else [])
    res.safety = dict(user_pd_safety(before, after), user_files_same=before["files"] == after["files"] and all(before["files"].values()),
                      input_leak=bool(leaks), user_returned=away.user_returned())
    res.notes.append({"user_pd_before": before["rects"], "user_pd_after": after["rects"], "user_files": after["files"], "leaks": leaks,
                      "gates": len(rig.gates) if rig else 0, "gate_fails": [g for g in (rig.gates if rig else []) if not g["pass"]]})


# 측정 공용 --------------------------------------------------------------
def doc_ids(rig):
    return [d["id"] for d in rig.r.call("docs.list") or []]


def open_docs(rig, kind="grid", count=1, **kw):
    before = set(doc_ids(rig))
    for _ in range(count):
        rig.r.call("docs.open", kind=kind, allowDuplicate=True, **kw)
        time.sleep(0.4)
    return [d for d in doc_ids(rig) if d not in before]


def make_sdi(rig, doc, x=None, y=None, w=420, h=300):
    rig.r.call("docs.sdi", doc=doc, on=True)
    time.sleep(1.2)
    m = rig.win(rig.main)["rect"]
    rig.r.call("sdi.setBounds", doc=doc, x=x if x is not None else m["r"] - w - 120, y=y if y is not None else m["b"] - h - 120, w=w, h=h)
    time.sleep(SETTLE_S)
    return rig.sdi_hwnd(doc)


def rclick_menu(rig, res, tag, x, y, hwnd, host=None):
    """실제 우클릭 뒤 Pd 메뉴(ContextMenu isOpen 또는 새 팝업 창)만 열렸고 OS 메뉴(#32768)는 없어야 한다."""
    before = {w["hwnd"] for w in rig.windows()}
    rig.click(x, y, button="Right", hwnd=hwnd)
    time.sleep(0.5)
    now = rig.windows()
    added = [w["cls"] for w in now if w["hwnd"] not in before]
    native = [w["cls"] for w in now if w["cls"] == "#32768"]
    read = rig.r.call("ui.contextMenu", id=host) if host else None
    is_open = bool(read and read.get("isOpen"))
    rig.grab(res, tag)
    value = {"at": (x, y), "host": host, "isOpen": is_open, "newWindows": added, "native": native,
             "items": [i.get("id") or i.get("header") for i in (read or {}).get("items", [])][:12]}
    rig.key("Esc")  # 전경이 팝업 창이다. hwnd 를 주면 게이트가 전경을 옮겨 Esc 전에 팝업이 닫힌다
    return value, (is_open if host else bool(added)) and not native


def menu_targets(rig):
    a, b, c = open_docs(rig, count=3)
    sdi = make_sdi(rig, b)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    return a, b, c, sdi


def title_of(rig, doc):
    # chrome.title 은 SDI 제목줄(SdiTitleBar.axaml:24)에만 있다. MDI 창 틀 제목은 이름 PART_DocumentHeader 뿐이라(MdiDocumentWindowTheme.axaml:59) 이름으로 찾는다
    return (rig.find("chrome.title", doc=doc) or rig.find("PART_DocumentHeader", doc=doc))["bounds"]


def rclick_four(rig, res, names, prefix):
    a, b, c, sdi = menu_targets(rig)
    rig.acquire(rig.main)
    d = rig.find("shell.titleDragArea")["bounds"]
    res.measure(names[0], lambda: rclick_menu(rig, res, prefix + "_main", *rig.point(rig.main, d), rig.main, "SystemMenu"))
    res.measure(names[1], lambda: rclick_menu(rig, res, prefix + "_sdi", *rig.point(sdi, title_of(rig, b)), sdi, "SdiSystemMenu"))
    res.measure(names[2], lambda: rclick_menu(rig, res, prefix + "_mdi", *rig.point(rig.main, title_of(rig, a)), rig.main))
    tab = rig.find("shell.tab." + c)["bounds"]
    res.measure(names[3], lambda: rclick_menu(rig, res, prefix + "_tab", *rig.point(rig.main, tab), rig.main, "shell.tabStrip"))
    return a, b, c, sdi


def sessions(rig):
    state = rig.r.call("tour.state")
    return (state.get("sessions") or []) if isinstance(state, dict) else (state or [])


def f1_case(rig, res, tag, hwnd):
    rig.key("F1", hwnd)
    time.sleep(0.8)
    rows = sessions(rig)
    rig.grab(res, tag)
    return {"sessions": rows}, bool(rows)


def end_tour(rig, hwnd):
    rig.key("Esc", hwnd)
    time.sleep(0.6)
    return sessions(rig)


# 항목 ---------------------------------------------------------------------
def a03(rig, res):
    rclick_four(rig, res, REQ["A03"][:4], "a03")

    def nc():
        d = rig.find("shell.titleDragArea")["bounds"]
        return rclick_menu(rig, res, "a03_nc", *rig.point(rig.main, d), rig.main, "SystemMenu")
    res.measure("nc_rbutton_pd_menu_only", nc)


class TrayObserver:
    """measure_hidden_main 이 쓰는 원격 객체를 감싼다. 트레이 도움말 앞뒤로 OS 창 상태를 읽고 복원된 화면을 캡처한다. 원격 호출은 그대로 넘긴다."""

    def __init__(self, rig, res):
        self.rig, self.res, self.seen = rig, res, []

    def os_state(self):
        w = self.rig.win(self.rig.main)  # win_place 는 보이는 창만 낸다. 숨은 메인은 None 이다
        return {"listed": w is not None, "iconic": (w or {}).get("iconic")}

    def call(self, cmd, **kw):
        if not cmd.startswith("tray."):
            return self.rig.r.call(cmd, **kw)
        away.check()
        if cmd == "tray.invoke" and kw.get("item") == "help":
            before = self.os_state()
            out = self.rig.r.call(cmd, **kw)
            time.sleep(1.2)
            row = {"before": before, "after": self.os_state(), "fg": self.rig.foreground(), "sessions": sessions(self.rig)}
            self.rig.grab(self.res, "a04_help_%d" % len(self.seen))
            self.seen.append(row)
            return out
        return self.rig.r.call(cmd, **kw)


def a04(rig, res):
    """숨은 메인 도움말 복원. 값은 measure_hidden_main 재사용, 보임은 OS 창 상태로 따로 읽는다(화면 표시 기동이라 잴 수 있다)."""
    from retest_tour_cases import measure_hidden_main
    open_docs(rig, count=1)
    enabled = rig.r.call("tray.enable", on=True)
    res.notes.append({"trayEnable": enabled})
    obs = TrayObserver(rig, res)
    away.check()
    raw = measure_hidden_main(obs)
    res.notes.append({"hiddenMain": raw})

    def case(name):
        row = raw[name]
        before, after = (row.get("before") or {}).get("main") or {}, (row.get("after") or {}).get("main") or {}
        value = {"stateBefore": before.get("state"), "stateAfter": after.get("state"), "sessions": row.get("sessions"), "overlay": row.get("overlay")}
        return value, after.get("state") not in (None, "Minimized") and bool(row.get("sessions")) and row.get("overlay") is True
    res.measure("minimized_help_restores", lambda: (lambda v: (v[0], v[1] and v[0]["stateBefore"] == "Minimized"))(case("minimized")))
    res.measure("tray_hidden_help_restores", lambda: case("hidden"))
    # 제품 설계: 트레이 아이콘이 보이면 최소화한 메인을 트레이로 숨긴다(ShellPlatform.cs:43-47). 최소화 직전은 작업 표시줄 아이콘(iconic) 또는 숨김(listed=False)이다
    res.add("window_visible_observed", obs.seen, len(obs.seen) == 2 and all(r["after"]["listed"] and r["after"]["iconic"] is False for r in obs.seen)
            and (obs.seen[0]["before"]["iconic"] is True or obs.seen[0]["before"]["listed"] is False) and obs.seen[1]["before"]["listed"] is False)


def a05(rig, res):
    a, b = open_docs(rig, count=2)
    sdi = make_sdi(rig, b)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    ends = []

    def case(name, tag, hwnd, click=None):
        def body():
            if click:
                rig.click(*click(), hwnd=hwnd)
            value, ok = f1_case(rig, res, tag, hwnd)
            left = end_tour(rig, hwnd)
            ends.append({"case": name, "left": left})
            return value, ok
        res.measure(name, body)
    case("f1_main", "a05_main", rig.main)
    case("f1_mdi", "a05_mdi", rig.main, lambda: rig.point(rig.main, title_of(rig, a)))
    case("f1_sdi", "a05_sdi", sdi)
    res.add("esc_ends_tour", ends, len(ends) == 3 and not any(e["left"] for e in ends))
    res.add("foreground_gate_all_passed", {"gates": len(rig.gates), "fails": [g for g in rig.gates if not g["pass"]]}, bool(rig.gates) and all(g["pass"] for g in rig.gates))


def a08(rig, res):
    a, b, c, sdi = rclick_four(rig, res, ["real_rclick_main", "real_rclick_sdi", "real_rclick_mdi", "real_rclick_tab"], "a08")

    def alt_space():
        rig.key("Alt+Space", rig.main)
        time.sleep(0.5)
        read = rig.r.call("ui.contextMenu", id="SystemMenu")
        now = rig.windows()
        rig.grab(res, "a08_altspace")
        value = {"isOpen": bool(read and read.get("isOpen")), "native": [w["cls"] for w in now if w["cls"] == "#32768"]}
        rig.key("Esc")
        return value, value["isOpen"] and not value["native"]
    res.measure("alt_space_menu", alt_space)

    def state_of(hwnd):
        w = rig.win(hwnd) or {}
        return {"iconic": w.get("iconic"), "zoomed": w.get("zoomed")}

    def minimize():
        rig.click_ctl(rig.main, id="shell.btnMinimize")
        time.sleep(SETTLE_S)
        main = state_of(rig.main)
        rig.show(rig.main)
        rig.acquire(rig.main)
        rig.click_ctl(sdi, id="chrome.btnMin", doc=b)
        time.sleep(SETTLE_S)
        win = state_of(sdi)
        rig.show(sdi)
        return {"main": main, "sdi": win}, bool(main["iconic"] and win["iconic"])
    res.measure("caption_min", minimize)

    def maximize():
        rig.acquire(rig.main)
        rig.click_ctl(rig.main, id="shell.btnMaximize")
        time.sleep(SETTLE_S)
        main = state_of(rig.main)
        rig.grab(res, "a08_max")
        rig.show(rig.main)  # 최대화 뒤 버튼이 모니터 경계 100px 안이라 실입력으로 되돌리지 않는다
        sdi_now = rig.sdi_hwnd(b)
        rig.acquire(sdi_now)
        rig.click_ctl(sdi_now, id="chrome.btnMax", doc=b)
        time.sleep(SETTLE_S)
        win = state_of(sdi_now)
        rig.show(sdi_now)
        return {"main": main, "sdi": win}, bool(main["zoomed"] and win["zoomed"])
    res.measure("caption_max", maximize)

    def close():
        hwnd = rig.sdi_hwnd(b)
        rig.acquire(hwnd)
        rig.click_ctl(hwnd, id="chrome.btnClose", doc=b)
        time.sleep(SETTLE_S)
        return {"docs": doc_ids(rig), "closed": b}, b not in doc_ids(rig) and a in doc_ids(rig)
    res.measure("caption_close", close)


def keep_panels(rig):
    """패널 소실 없음: 세 도구가 보이는 패널이나 문서에 있고 보이는 패널마다 메뉴가 열리며 빈 패널이 없다."""
    now = drag_lib.panels(rig.r)
    where = {tool: [i for i, p in now.items() if tool in drag_lib.tabs_of(p) and p.get("visible")] for tool, _ in drag_lib.HOME.values()}
    docs = doc_ids(rig)
    lost = [t for t, at in where.items() if not at and t not in docs]
    menus = {i: (rig.r.call("panelmenu.read", pane=i) or {}).get("visible") for i, p in now.items() if p.get("visible")}
    empty = [i for i, p in now.items() if p.get("visible") and not drag_lib.tabs_of(p)]
    return {"where": where, "lost": lost, "menus": menus, "empty": empty}, not lost and all(menus.values()) and not empty


def reset_panels(rig):
    for pane, (tool, side) in drag_lib.HOME.items():
        if tool not in drag_lib.tabs_of(drag_lib.panels(rig.r).get(pane, {})):
            rig.r.call("panelmenu.click", tab=tool, target=pane)
            time.sleep(0.4)
        rig.r.call("panel.dock", id=pane, side=side)
    time.sleep(0.5)


def panel_drag_points(rig, pane, target):
    b = drag_lib.pane_bounds(rig.r, pane)
    frm = drag_lib.title_point(b)
    to = drag_lib.target_point(rig.r, target)
    return rig.dip_point(rig.main, *frm), rig.dip_point(rig.main, *to)


def a09(rig, res):
    keeps = []
    guide = {}

    def drag_case(name, pane, target, want, watch=False):
        def body():
            frm, to = panel_drag_points(rig, pane, target)
            watcher = rig.watch_guide(res, "a09_guide") if watch else None
            try:
                rig.click(*frm, drag=to, hwnd=rig.main)
            finally:
                if watcher:
                    guide.update(rig.finish_guide(watcher[0], watcher[1], res))
            time.sleep(SETTLE_S)
            side = drag_lib.panels(rig.r)[pane]["side"]
            keeps.append(keep_panels(rig))
            rig.grab(res, "a09_" + name)
            reset_panels(rig)
            return {"from": frm, "to": to, "side": side, "want": want}, side == want
        res.measure(name, body)
    drag_case("real_drag_explorer", LP, "cross.right", "Right", watch=True)
    drag_case("real_drag_log", BP, "cross.top", "Top")
    drag_case("real_drag_notify", NP, "cross.left", "Left")
    res.add("dock_guide_visible", guide, bool(guide.get("seen")))

    def splitter():
        bounds = rig.r.call("panel.bounds")
        left, docs = bounds[LP], bounds["Documents"]
        x = (left["x"] + left["w"] + docs["x"]) / 2
        y = docs["y"] + docs["h"] / 2
        frm = rig.dip_point(rig.main, x, y)
        s = rig.win(rig.main)["dpi"] / 96.0
        rig.click(*frm, drag=(frm[0] + int(80 * s), frm[1]), hwnd=rig.main)
        time.sleep(SETTLE_S)
        after = rig.r.call("panel.bounds")[LP]
        delta = after["w"] - left["w"]
        return {"before": left["w"], "after": after["w"], "delta": delta}, 60 <= delta <= 100
    res.measure("splitter_drag", splitter)
    res.add("panel_kept_after_drop", keeps, len(keeps) == 3 and all(k[1] for k in keeps))


def rect_of(rig, hwnd):
    r = (rig.win(hwnd) or {}).get("rect") or {}
    return [r.get(k) for k in ("l", "t", "r", "b")]


def panel_only_window(rig, tool="Log"):
    rig.r.call("docs.close", all=True)
    doc = open_docs(rig)[0]
    make_sdi(rig, doc)
    rig.r.call("split.do", target="doc:" + doc, tool=tool, op="Fill")
    time.sleep(1.0)
    rig.r.call("docs.close", doc=doc)
    time.sleep(1.0)
    win = next((w for w in rig.r.call("windows.list") or [] if not w.get("main")), None)
    hwnd = next((w["hwnd"] for w in rig.windows() if w["hwnd"] != rig.main and w["cls"] != base.SHADOW and w["rect"]["r"] - w["rect"]["l"] > 100), None)
    return win, hwnd


def a10(rig, res):
    a, b = open_docs(rig, count=2)
    sdi = make_sdi(rig, b)

    def not_pushed():
        before = rect_of(rig, sdi)
        frm, to = panel_drag_points(rig, LP, "cross.top")
        rig.click(*frm, drag=to, hwnd=rig.main)
        time.sleep(SETTLE_S)
        after = rect_of(rig, sdi)
        rig.grab(res, "a10_panel_moved")
        reset_panels(rig)
        return {"before": before, "after": after, "side": drag_lib.panels(rig.r)[LP]["side"]}, before == after
    res.measure("sdi_not_pushed_by_panel", not_pushed)

    def spinner():
        d = rig.find("shell.dock")["bounds"]
        inside = rig.point(rig.main, d)
        rig.click(*inside, no_click=True)
        idle = rig.cursor_handle()
        started = rig.r.call("busy.begin", scope="main", reason="a10")
        time.sleep(1.0)
        rig.click(inside[0] + 12, inside[1] + 12, no_click=True)
        time.sleep(0.4)
        state_in = rig.r.call("busy.state")
        hand_in = rig.cursor_handle()
        out, owner = rig.outside_point()
        if not out:
            rig.r.call("busy.end", token=started.get("token"))
            return {"error": "시험 앱·사용자 Pd 밖의 점이 없다"}, False
        rig.click(*out, no_click=True, expected=owner)
        time.sleep(0.5)
        state_out = rig.r.call("busy.state")
        hand_out = rig.cursor_handle()
        rig.grab(res, "a10_spinner_outside")
        rig.r.call("busy.end", token=started.get("token"))
        follower = state_in.get("usesFollower")
        ok = (not state_out.get("followerVisible")) if follower else (hand_in != idle and hand_out != hand_in)
        return {"usesFollower": follower, "idle": idle, "inside": hand_in, "outside": hand_out, "outsideAt": out, "owner": owner, "followerOut": state_out.get("followerVisible")}, ok
    res.measure("spinner_hidden_outside_window", spinner)

    def tab_drag():
        rig.r.call("docs.close", all=True)
        x, y, z = open_docs(rig, count=3)
        hwnd = make_sdi(rig, x)
        rig.r.call("docs.arrange", mode="tabbed")
        time.sleep(1.2)
        title = next(d["title"] for d in rig.r.call("docs.list") if d["id"] == z)
        item, lookup = find_doc_tab(rig.r, title, z)
        if not item:
            return {"error": "문서 탭을 찾지 못함", "tabLookup": lookup}, False
        frm = rig.point(rig.main, item["bounds"])
        w = rig.win(hwnd)["client"]
        to = ((w["l"] + w["r"]) // 2, (w["t"] + w["b"]) // 2)
        rig.click(*frm, drag=to, hwnd=rig.main)
        time.sleep(1.2)
        rows = [r for r in rig.r.call("windows.list") or [] if not r.get("main")]
        docs = rows[0].get("docs") if rows else []
        rig.grab(res, "a10_tab_joined")
        return {"from": frm, "to": to, "docsInSdi": docs, "moved": z, "tabLookup": lookup}, any(z == (d.get("id") if isinstance(d, dict) else d) for d in docs or [])
    res.measure("tab_drag_to_other_window", tab_drag)

    state = {}

    def only_min():
        state["win"], state["hwnd"] = panel_only_window(rig)
        hwnd, index = state["hwnd"], (state["win"] or {}).get("index")
        rig.acquire(hwnd)
        rig.grab(res, "a10_panel_only")
        rig.click_ctl(hwnd, id="chrome.btnMin", window=index)
        time.sleep(SETTLE_S)
        iconic = (rig.win(hwnd) or {}).get("iconic")
        rig.show(hwnd)
        return {"window": state["win"], "iconic": iconic}, bool(iconic)
    res.measure("panel_only_window_minimize", only_min)

    def only_max():
        hwnd, index = state["hwnd"], state["win"]["index"]
        rig.acquire(hwnd)
        rig.click_ctl(hwnd, id="chrome.btnMax", window=index)
        time.sleep(SETTLE_S)
        zoomed = (rig.win(hwnd) or {}).get("zoomed")
        rig.grab(res, "a10_panel_only_max")
        rig.show(hwnd)
        return {"zoomed": zoomed}, bool(zoomed)
    res.measure("panel_only_window_maximize", only_max)

    def only_menu():
        hwnd, index = state["hwnd"], state["win"]["index"]
        rig.acquire(hwnd)
        b0 = rig.find("chrome.title", window=index)["bounds"]
        return rclick_menu(rig, res, "a10_panel_only_menu", *rig.point(hwnd, b0), hwnd)
    res.measure("panel_only_window_menu", only_menu)


def a11(rig, res):
    d = rig.find("shell.dock")["bounds"]
    inside = rig.point(rig.main, d)
    rig.click(*inside, no_click=True)
    time.sleep(0.3)
    idle = rig.cursor_handle()
    started = rig.r.call("busy.begin", scope="main", reason="a11")
    time.sleep(1.2)
    rig.click(inside[0] + 15, inside[1], no_click=True)
    time.sleep(0.5)
    state = rig.r.call("busy.state")
    hand = rig.cursor_handle()
    rig.grab(res, "a11_busy")
    res.add("h21_cursor_is_spinner_on_busy", {"idle": idle, "busy": hand, "mode": state.get("cursorMode"), "advances": state.get("cursorFrameAdvances"), "timers": state.get("timers")},
            hand != idle and bool(state.get("scopes")))
    overlays = [o for s in state.get("scopes") or [] for o in s.get("overlays") or []]

    def overlay():
        if not overlays:
            return {"overlays": 0}, False
        o = overlays[0]
        ok = max(o["spinnerWidth"], o["spinnerHeight"]) <= MAX_SPINNER_DIP and o["spinnerOpacity"] <= MAX_OPACITY and not o["hitTestVisible"] and not o["spinnerHitTest"]
        return o, ok
    res.measure("h22_overlay_size_opacity", overlay)
    rig.r.call("busy.end", token=(started or {}).get("token"))
    time.sleep(1.2)
    rig.click(inside[0], inside[1] + 15, no_click=True)
    time.sleep(0.5)
    after = rig.r.call("busy.state")
    back = rig.cursor_handle()
    rig.grab(res, "a11_restored")
    res.add("h21_cursor_restored", {"idle": idle, "after": back, "timers": after.get("timers")}, back == idle and not (after.get("timers") or {}).get("cursor"))


def corner_button(rig, button_id):
    """근접으로 나타나는 버튼: 커서를 패널 모서리로 옮긴 뒤 실클릭한다."""
    _, panel, corner = button_id.rsplit(".", 2)
    b = rig.r.call("panel.bounds")[panel]
    fx, fy = (0.0 if "Left" in corner else 1.0), (0.0 if "Top" in corner else 1.0)
    inset = (12 if fx == 0.0 else -12, 12 if fy == 0.0 else -12)
    near = rig.dip_point(rig.main, b["x"] + b["w"] * fx + inset[0], b["y"] + b["h"] * fy + inset[1])
    rig.click(*near, no_click=True)
    time.sleep(0.5)
    found = rig.find(button_id)
    if not found or not found.get("effectivelyVisible"):
        raise InputRefused("근접해도 버튼이 보이지 않음: %s" % button_id)
    x, y = rig.point(rig.main, found["bounds"])
    rig.click(x, y, hwnd=rig.main)
    time.sleep(SETTLE_S)


def a12(rig, res):
    before = rig.r.call("panel.bounds")
    layout = (rig.r.call("layout.get") or {}).get("Panels")

    def expand():
        corner_button(rig, "panel.corner.BottomPane.BottomLeft")
        now = rig.r.call("panel.bounds")
        rig.grab(res, "a12_expanded")
        return {"before": before[BP], "after": now[BP], "left": now[LP]}, abs(now[BP]["x"] - before[LP]["x"]) <= 2 and now[LP]["h"] < before[LP]["h"] - 2
    res.measure("h31_button_real_click_expand", expand)

    def restore():
        corner_button(rig, "panel.corner.LeftPane.BottomLeft")
        now = rig.r.call("panel.bounds")
        same = all(abs(before[k][c] - now[k][c]) <= 2 for k in (LP, BP, "Documents") for c in "xywh")
        rig.grab(res, "a12_restored")
        return {"same": same, "panelsEqual": layout == (rig.r.call("layout.get") or {}).get("Panels")}, same
    res.measure("h31_button_real_click_restore", restore)

    def redock():
        doc = open_docs(rig)[0]
        rig.r.call("placement.markPlaced", doc=doc, x=140, y=90, w=480, h=320)
        time.sleep(SETTLE_S)
        row = lambda: next(d for d in rig.r.call("placement.state")["docs"] if d["id"] == doc)
        start = row()["bounds"]
        rig.click_ctl(rig.main, id="chrome.btnSdi", doc=doc)
        time.sleep(1.2)
        hwnd = rig.sdi_hwnd(doc)
        rig.acquire(hwnd)
        rig.click_ctl(hwnd, id="chrome.btnSdi", doc=doc)
        time.sleep(1.2)
        end = row()["bounds"]
        rig.grab(res, "a12_redocked")
        return {"before": start, "after": end, "floating": hwnd}, bool(hwnd) and all(abs(start[k] - end[k]) <= 1 for k in start)
    res.measure("h41_redock_same_position_real", redock)


ITEMS = {"A03": a03, "A04": a04, "A05": a05, "A08": a08, "A09": a09, "A10": a10, "A11": a11, "A12": a12}


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


def find_doc_tab(r, title, doc_id=None):
    """문서 탭 위치 찾기(도구 단독). tree.get named=True 로 문서 제목과 text 가 같은 TextBlock 중 보이고 w>0 이며
    조상 중에 DocumentTabStripItem 이 있는 것만 고른다. 제목 줄 탭(shell.tab.* 하위)과 ShellStatusBar 하위(활성 문서 제목 TextBlock)는 뺀다.
    window=0 은 메인 창이라 SDI 창은 들어오지 않는다. 후보가 없으면 None(다른 탭으로 대신하지 않는다). (노드, 진단) 을 돌려준다.
    진단 matches_detail 에 제목이 같은 모든 TextBlock 의 bounds·조상 타입·id·채택 여부를 남긴다."""
    resp = r.call("tree.get", window=0, named=True, depth=120) or {}
    found, seen = [], []

    def go(node, skip, ancestors):
        if isinstance(node, list):
            for child in node:
                go(child, skip, ancestors)
            return
        if not isinstance(node, dict):
            return
        key = str(node.get("id") or node.get("name") or "")
        skip = skip or key.startswith("shell.tab")
        if node.get("type") == "TextBlock" and node.get("text") == title:
            types = [a.get("type") for a in ancestors]
            ids = [str(a.get("id") or a.get("name") or "") for a in ancestors if a.get("id") or a.get("name")]
            visible = bool(node.get("effectivelyVisible")) and (node.get("bounds") or {}).get("w", 0) > 0
            in_strip = "DocumentTabStripItem" in types
            in_status = "ShellStatusBar" in types
            take = visible and in_strip and not in_status and not skip
            seen.append({"bounds": node.get("bounds"), "id": node.get("id"), "ancestorTypes": types[-8:], "ancestorIds": ids[-6:],
                         "visible": visible, "inDocumentTabStripItem": in_strip, "inShellStatusBar": in_status, "inShellTab": skip, "taken": take})
            if take:
                found.append(node)
        for child in node.get("children") or []:
            go(child, skip, ancestors + [node])
    go(resp.get("tree"), False, [])
    info = {"nodes": resp.get("nodes"), "truncated": resp.get("truncated"), "matches": len(found), "textMatches": len(seen), "matches_detail": seen, "title": title}
    if doc_id:
        try:
            info["menuHeaders"] = r.call("dock.menuHeaders", target="tab", doc=doc_id)
        except Exception as e:  # 존재 확인 보조라 실패해도 측정은 계속한다
            info["menuHeaders"] = {"error": repr(e)}
    return (found[0] if found else None), info


# 실행 ---------------------------------------------------------------------
def add_common_args(p):
    p.add_argument("--items", help="쉼표 구분 항목")
    p.add_argument("--run-dir", help="bin/run/<복사본>/net10.0")
    p.add_argument("--phase", choices=("discovery", "final"), default="discovery")
    p.add_argument("--writer", default="", help="결과 writer(otest 로 시작)")
    p.add_argument("--user-pid", type=int, default=None, help="생략하면 실행 경로로 찾는다. 주면 그 경로의 프로세스인지 확인한다")
    p.add_argument("--monitor", default="DISPLAY3", help="시험 창 모니터(터미널 없는 모니터 중 큰 쪽)")
    p.add_argument("--session", default=os.environ.get("PCAWAY_SESSION") or os.path.join(os.path.expanduser("~"), ".claude", "session-env", os.environ.get("PIPELINE_UUID", "")))
    p.add_argument("--annotate", help="판독 기록을 넣을 결과 JSON")
    p.add_argument("--capture", help="파일 이름 또는 all")
    p.add_argument("--read-by")
    p.add_argument("--finding")
    p.add_argument("--self-test", action="store_true")
    p.add_argument("--dry-run-endpoints", action="store_true", help="입력을 보내지 않고 끌기·클릭 끝점이 메인 창 안이고 소유자가 시험 앱인지만 사전 판정해 rig.dry_points 에 남긴다")
    p.add_argument("--allow-outside-main", action="store_true", help="--dry-run-endpoints 사전 판정에서만, 끝점이 메인 창 밖이어도 시험 앱 소유이고 시험 모니터 작업 영역 안이면 통과시킨다(기본 꺼짐, 작업 영역 밖은 항상 거부). safe_click.ps1 가드는 그대로라 실제 클릭은 가드 판정을 따른다")


def annotate(args):
    if not str(args.read_by or "").startswith("otest") or len(str(args.finding or "").strip()) < 10:
        print("거부: --read-by 는 otest 로 시작하고 --finding 은 10자 이상이어야 한다")
        return 3
    with open(args.annotate, encoding="utf-8") as f:
        doc = json.load(f)
    hit = 0
    for c in doc.get("captures", []):
        if args.capture == "all" or os.path.basename(c["path"]) == args.capture:
            c["read_by"], c["finding"] = args.read_by, args.finding
            hit += 1
    if not hit:
        print("해당 캡처 없음: %s" % args.capture)
        return 3
    tmp = args.annotate + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    os.replace(tmp, args.annotate)
    print("판독 기록 %d건" % hit)
    return 0


def drive(args, table, req, role, script, human=None, result_cls=None):
    """항목마다 새 앱. 복귀 판단이면 즉시 멈추고 그 항목을 FAIL 로 남긴 뒤 rc=5."""
    if not args.writer.startswith("otest"):
        print("거부: 결과 writer 는 otest 로 시작해야 한다")
        return 3
    if not args.run_dir or not args.items:
        print("거부: --items·--run-dir 가 필요하다")
        return 3
    if away.user_returned():
        print("거부: 복귀 플래그가 있다. 재개는 사용자 재지시 뒤 arm 으로만 한다")
        return 5
    if not resolve_user_pid(args):
        return 3
    names = [n.strip() for n in args.items.split(",") if n.strip()]
    bad = [n for n in names if n not in table]
    if bad:
        print("미등록 항목: %s" % bad)
        return 3
    final = args.phase == "final"
    build = build_info(args.run_dir, role, args.session, final=final)
    info = script_info(script)
    code = 0
    for name in names:
        res = (result_cls or Result)(name, req[name], args, info, build, human=(human or {}).get(name))
        work = os.path.join(res.dir, "work", res.run_id)
        os.makedirs(work, exist_ok=True)
        before = snapshot_user(args.user_pid)
        rig = Rig(args, work)
        returned = False
        try:
            away.check()
            rig.start()
            res.notes.append({"test_window": {"pid": rig.pid, "monitor": (rig.win(rig.main) or {}).get("monitor"), "rect": (rig.win(rig.main) or {}).get("rect")}})
            table[name](rig, res)
        except (away.UserReturned, away.GuardUnavailable) as e:
            returned = True
            res.notes.append({"stopped": repr(e)})
            print("중단: %r" % (e,), flush=True)
        except SystemExit as e:
            res.notes.append({"launch_failed": str(e)})
            print("기동 실패: %s" % e, flush=True)
            code = code or 2
        finally:
            rig.stop()
            fill_safety(res, before, snapshot_user(args.user_pid), rig)
            if not res.write():
                code = code or 1
        if returned:
            return 5
    return code


GATE_REPO = os.path.join(AREA, "test", "desktop", "judge", "pcaway_gate.py")  # 저장소 사본(2026-11-24 Pd 보관본에서 복원)


def gate_file(session):
    """세션 폴더의 pcaway_gate.py 가 있으면 그것, 없으면 저장소 사본(test/desktop/judge/pcaway_gate.py)."""
    path = os.path.join(session or "", "pcaway_gate.py")
    return path if os.path.exists(path) else GATE_REPO


def gate_module(session):
    path = gate_file(session)
    if not os.path.exists(path):
        return None
    spec = importlib.util.spec_from_file_location("pcaway_gate_probe", path)
    module = importlib.util.module_from_spec(spec)
    argv, sys.argv = sys.argv, ["x"]
    try:
        spec.loader.exec_module(module)
    finally:
        sys.argv = argv
    return module


def _good_value(rule):
    """판정기 기준 (op, want) 을 만족하는 값. 기준이 없으면 1 이다(불리언 기준은 정확히 True 여야 통과)."""
    if not rule:
        return 1
    op, want = rule
    if op == "range":
        return (want[0] + want[1]) / 2
    return want


def self_test_gate(req, session, build_role, items, human=None):
    """임시 트리에서 Result 로 쓴 결과를 실제 판정기에 넘긴다: 판독 전 FAIL, 판독 후 PASS, 측정 실패·복귀·미측정 FAIL. 앱·입력은 쓰지 않는다."""
    gate_path = gate_file(session)
    if not os.path.exists(gate_path):
        print("SKIP 판정기 왕복(판정기 없음): %s" % gate_path)
        return []
    fails = []
    tmp = tempfile.mkdtemp(prefix="pd_ri_")
    try:
        root, sess = os.path.join(tmp, "repo"), os.path.join(tmp, "sess")
        os.makedirs(os.path.join(root, AREA_REL, "bin"))
        os.makedirs(os.path.join(sess, "plans"))
        tool, bin_file = os.path.join(root, AREA_REL, "tool.py"), os.path.join(root, AREA_REL, "bin", "app.dll")
        for p in (tool, bin_file):
            with open(p, "w", encoding="utf-8") as f:
                f.write("x\n")
        with open(os.path.join(sess, "plans", "pcaway_final_build.json"), "w", encoding="utf-8") as f:
            json.dump({build_role: {"path": rel(bin_file, root), "sha256": sha256(bin_file)}}, f)
        env = dict(os.environ, PCAWAY_ROOT=root, PCAWAY_SESSION=sess)
        args = argparse.Namespace(phase="final", writer="otest-x")
        for item in items:
            res = Result(item, req[item], args, script_info(tool, root), build_info(os.path.dirname(bin_file), build_role, sess, root, "app.dll", final=True), root=root, human=(human or {}).get(item))
            res.safety = {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False}
            spec_item = gate_module(session).SPEC[item]
            good = {n: _good_value(spec_item["expect"].get(n)) for n in req[item]}
            for f in list(spec_item["files"]) + [f for f, _ in spec_item["lacks"]]:  # 판정기가 임시 트리에서 먼저 보는 소스 파일
                fp = os.path.join(root, f)
                os.makedirs(os.path.dirname(fp), exist_ok=True)
                if not os.path.exists(fp):
                    with open(fp, "w", encoding="utf-8") as fh:
                        fh.write("x\n")
            for name in req[item]:
                res.add(name, good[name], True)
            os.makedirs(res.shots, exist_ok=True)
            for n in range(5):
                png = os.path.join(res.shots, "c%d.png" % n)
                with open(png, "wb") as f:
                    f.write(b"\x89PNG\r\n\x1a\nabc")
                res.capture(png)
            res.write()
            path = os.path.join(res.dir, item + ".json")

            def gate():
                return subprocess.run([sys.executable, "-I", gate_path, "item", item], env=env, capture_output=True, text=True)
            unread = gate()
            needs_read = gate_module(session).SPEC[item]["caps"] > 0  # 캡처를 요구하지 않는 항목(F11 등)은 판정기가 판독 기록을 보지 않는다
            if needs_read and (unread.returncode != 1 or "판독" not in unread.stdout):
                fails.append("%s 판독 전 FAIL 아님: %s" % (item, unread.stdout.strip()))
            annotate(argparse.Namespace(annotate=path, capture="all", read_by="otest-x", finding="캡처를 읽은 결과 기록"))
            for c in res.captures:  # 뒤의 음성 대조가 '판독 전' 사유로 통과하지 않게 메모리 값도 맞춘다
                c.update(read_by="otest-x", finding="캡처를 읽은 결과 기록")
            full = gate()
            if full.returncode != 0:
                fails.append("%s 판독 후 PASS 아님: %s" % (item, full.stdout.strip()))
            res.add(req[item][0], 0, False)
            res.write()
            if gate().returncode != 1:
                fails.append("%s 측정 실패인데 FAIL 아님" % item)
            res.add(req[item][0], good[req[item][0]], True)
            res.safety["user_returned"] = True
            res.write()
            if gate().returncode != 1:
                fails.append("%s 복귀인데 FAIL 아님" % item)
            res.safety["user_returned"] = False
            res.write()
            if gate().returncode != 0:
                fails.append("%s 복귀 음성 뒤 복원 PASS 아님(음성이 다른 사유로 FAIL 했을 수 있음)" % item)
            res.safety["user_returned"] = False
            res.measurements = res.measurements[1:]
            res.write()
            if json.load(open(path, encoding="utf-8"))["verdict"] != "FAIL" or gate().returncode != 1:
                fails.append("%s 미측정인데 FAIL 아님" % item)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return fails


def self_test_names(req, session):
    module = gate_module(session)
    if module is None:
        print("SKIP 측정 이름 대조(판정기 없음)")
        return []
    return ["%s 필수 측정 이름이 판정기와 다름: 도구=%s 판정기=%s" % (i, req[i], module.SPEC[i]["req"]) for i in req if req[i] != module.SPEC[i]["req"]]


def self_test_user_pd_cases(session):
    """사용자 Pd 4경우(살아있음 유지·살아있다 사라짐·둘 다 없음·없다가 생김)와 변형을 도구 판정과 판정기 check_user_pd_case 로 대조한다."""
    fails = []
    rect = [(11, 0, 0, 100, 100)]
    alive = {"pid": 352, "start": "2026-11-07T08:10:00.0000000+09:00", "present": [352], "alive": True, "rects": rect}
    absent = {"pid": None, "start": None, "present": [], "alive": False, "rects": []}
    cases = [("살아있음 유지", alive, dict(alive), [], "present_untouched"),
             ("살아있다 사라짐", alive, dict(alive, start=None, present=[], alive=False, rects=[]), [], None),
             ("같은 PID 재시작(시작 시각 다름)", alive, dict(alive, start="2026-11-07T09:00:00.0000000+09:00"), [], None),
             ("살아있고 창 이동", alive, dict(alive, rects=[(11, 5, 0, 105, 100)]), [], None),
             ("둘 다 없음", absent, dict(absent), [], "absent_both"),
             ("없다가 생김", absent, dict(absent, present=[77]), [], None),
             ("둘 다 없으나 측정 중 실행 기록", absent, dict(absent), ["2026-11-07 13:30:00"], None),
             ("둘 다 없으나 실행 기록 판정 불가", absent, dict(absent), None, None),
             ("시험 인스턴스만 있음", dict(absent, present=[8]), dict(absent, present=[8]), [], None),
             ("조회 실패", dict(absent, present=None), dict(absent, present=None), [], None)]
    module = gate_module(session)
    for name, b, a, launches, want in cases:
        sf = user_pd_safety(b, a, launches)
        if sf["user_pd_case"] != want or sf["user_pd_untouched"] is not (want is not None):
            fails.append("사용자 Pd 경우 도구 판정 오류 %s: got=%s want=%s" % (name, sf["user_pd_case"], want))
        if module is None:
            continue
        # 값만 위조해도(참·두 경우 이름) 근거 필드로 판정기가 거부해야 한다. 정상 경우는 자기 이름일 때만 통과
        probes = [(sf, want is not None)] + [(dict(sf, user_pd_untouched=True, user_pd_case=label), label == want) for label in ("present_untouched", "absent_both")]
        for probe, expect_ok in probes:
            try:
                module.check_user_pd_case(probe)
                ok = probe["user_pd_untouched"] is True
            except SystemExit:
                ok = False
            if ok != expect_ok:
                fails.append("사용자 Pd 경우 판정기 오류 %s: ok=%s want=%s case=%s" % (name, ok, expect_ok, probe.get("user_pd_case")))
    if module is None:
        print("SKIP 사용자 Pd 경우 판정기 대조(판정기 없음)")
    snap = snapshot_user(None, query=lambda: [])
    if snap["present"] != [] or snap["pid"] is not None:
        fails.append("사용자 Pd 없음 스냅샷 오류: %s" % snap)
    with tempfile.TemporaryDirectory() as d:
        with open(os.path.join(d, "pd-20261107.log"), "w", encoding="utf-8") as f:
            f.write("2026-11-07 13:30:00.100 INF System host start channel=dev\n2026-11-07 15:00:00.000 INF System host start channel=dev\n")
        t0 = int(time.mktime(time.strptime("2026-11-07 13:29:00", "%Y-%m-%d %H:%M:%S")) * 1000)
        if user_pd_launches(t0, t0 + 120000, d) != ["2026-11-07 13:30:00"] or user_pd_launches(t0 - 3600000, t0 - 3000000, d) != []:
            fails.append("사용자 Pd 실행 기록 구간 판정 오류")
    print("사용자 Pd 경우 자가시험 %d경우, 실패 %d건" % (len(cases), len(fails)))
    return fails


def self_test(args):
    fails = self_test_names(REQ, args.session) + self_test_gate(REQ, args.session, "desktop_work", list(REQ), HUMAN)
    args2 = argparse.Namespace(monitor="NOPE", run_dir=".")
    rig = Rig.__new__(Rig)
    rig.args = args2
    rig.monitors = [{"device": "M", "bounds": {"l": 0, "t": 0, "r": 1000, "b": 1000}}]
    for pt, refused in (((50, 500), True), ((500, 50), True), ((950, 500), True), ((5000, 500), True), ((100, 100), False), ((899, 899), False), ((900, 900), True)):
        try:
            rig.check_coord(*pt)
            got = False
        except InputRefused:
            got = True
        if got != refused:
            fails.append("경계 좌표 판정 오류 %s" % (pt,))
    exe_name = _paths.get("desktop.exe_name")
    user = {"ProcessId": 352, "ExecutablePath": "C:\\P\\bin\\run\\otest_final\\net10.0\\" + exe_name, "CommandLine": "x.exe"}
    copy3 = {"ProcessId": 7, "ExecutablePath": "C:\\P\\bin\\run\\otest_final3\\net10.0\\" + exe_name, "CommandLine": "x.exe"}
    test = dict(user, ProcessId=8, CommandLine="x.exe --data-dir C:\\t")
    for rows, given, want in (([user], None, 352), ([user, copy3, test], None, 352), ([dict(user, ExecutablePath=user["ExecutablePath"].upper())], None, 352), ([user], 352, 352),
                              ([], None, None), ([copy3], None, None), ([test], None, None), ([user, dict(user, ProcessId=9)], None, None), ([user], 41312, None), ([{"ProcessId": 5}], None, None)):
        try:
            got = pick_user_pid(rows, given)
        except UserPdUnknown:
            got = None
        if got != want:
            fails.append("사용자 Pd 판정 오류 rows=%s given=%s got=%s" % ([r["ProcessId"] for r in rows], given, got))

    def broken():
        raise ValueError("깨진 출력")

    ns = argparse.Namespace(user_pid=None)
    if resolve_user_pid(ns, broken) or ns.user_pid is not None:
        fails.append("사용자 Pd 조회 실패가 거부되지 않음")
    if resolve_user_pid(ns, lambda: [user, dict(user, ProcessId=9)]) or ns.user_pid is not None:
        fails.append("사용자 Pd 2개 이상이 거부되지 않음")
    if not resolve_user_pid(ns, lambda: []) or ns.user_pid is not None:
        fails.append("사용자 Pd 0개가 '없음'으로 진행되지 않음")
    if not resolve_user_pid(ns, lambda: [user]) or ns.user_pid != 352:
        fails.append("사용자 Pd 확정 실패")
    fails += self_test_user_pd_cases(args.session)
    cl_t, wk_t = {"l": 100, "t": 100, "r": 500, "b": 400}, {"l": 0, "t": 0, "r": 1000, "b": 1000}
    for end, own_pid, allow, want in (((200, 200), 1, False, True), ((700, 700), 1, False, False), ((700, 700), 1, True, True),
                                      ((1500, 700), 1, True, False), ((1500, 700), 1, False, False), ((700, 700), 2, True, False), ((200, 200), 2, False, False)):
        if dry_endpoint_verdict(end, cl_t, own_pid, 1, wk_t, allow)[0] != want:
            fails.append("dry-run 끝점 판정 오류 end=%s owner=%s allow=%s" % (end, own_pid, allow))
    p2 = argparse.ArgumentParser()
    add_common_args(p2)
    if p2.parse_args([]).allow_outside_main or not p2.parse_args(["--allow-outside-main"]).allow_outside_main:
        fails.append("--allow-outside-main 기본값/지정 오류")
    parser = argparse.ArgumentParser()
    add_common_args(parser)
    if parser.parse_args([]).user_pid is not None:
        fails.append("--user-pid 기본값이 비어 있지 않음")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_common_args(p)
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return annotate(args)
    return drive(args, ITEMS, REQ, "desktop_work", os.path.abspath(__file__), HUMAN)


if __name__ == "__main__":
    sys.exit(main())
