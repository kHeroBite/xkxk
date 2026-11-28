#!/usr/bin/env python3
# retest_mdi_drag 의 격리 기동·종료·창 메시지 끌기 도구 — bin/run 복사본을 임시 폴더·화면 밖으로 직접 띄우고 자기 PID 만 다룬다
"""
사용(단독 실행 = 기동만):
  python3 -B mdi_drag_app.py --run-dir <bin/run/<이름>/net10.0> --work <logs/<에이전트>/<회차>>
run_desktop_bg.sh 를 쓰지 않는 이유: 그 스크립트는 logs/user_files/pending 을 다시 쓰고 RESULT·창 사각형이 남의 프로세스 것일 수 있다.
안전: --multi-instance 로 단일 인스턴스 신호를 막고, 연결 정보는 임시 폴더 파일만 읽으며, 로그에 단일 인스턴스 흔적이 있으면 즉시 중단한다.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()

HERE = os.path.dirname(os.path.abspath(__file__))
BG_POS = "21500,100"
PRIMARY = "18000,0,6000,1440"
FORBIDDEN = ("single-instance already running", "signalled=True")
PS = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"


def win(path):
    return subprocess.check_output(["wslpath", "-w", path], text=True).strip()


def ps(script, timeout=40):
    return subprocess.run([PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script], capture_output=True,
                          encoding="utf-8", errors="replace", timeout=timeout).stdout.replace("\r", "")  # 콘솔 출력이 cp949 일 수 있다


def virtual_screen():
    text = ps("Add-Type -AssemblyName System.Windows.Forms; $v=[System.Windows.Forms.SystemInformation]::VirtualScreen; \"$($v.X) $($v.Y) $($v.Width) $($v.Height)\"")
    x, y, w, h = (int(v) for v in text.split())
    return x, y, w, h


def log_violation(log_path):
    """앱 로그에 단일 인스턴스 흔적이 있으면 그 줄을 돌려준다."""
    if not os.path.exists(log_path):
        return None
    with open(log_path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if any(k in line for k in FORBIDDEN):
                return line.strip()
    return None


def launch(run_dir, work, extra=(), seed_layout=None):
    """기동해 (Windows PID, 연결 정보 파일, 로그 경로)를 돌려준다. 어긋나면 SystemExit. seed_layout 은 기동 전에 임시 프로필에 넣을 layout.json 내용(문자열)이다."""
    run_dir = os.path.abspath(run_dir)
    exe = os.path.join(run_dir, _paths.get("desktop.exe_name"))
    if "/bin/run/" not in run_dir.replace("\\", "/") or not os.path.exists(exe):
        sys.exit("bin/run 복사본만 실행한다: %s" % run_dir)
    vx, _, vw, _ = virtual_screen()
    if 21500 < vx + vw:
        sys.exit("화면 밖 좌표가 가상 화면과 겹친다 — 중단")
    stamp = time.strftime("%H%M%S")
    data = os.path.abspath(os.path.join(work, "data_" + stamp))
    profile = os.path.abspath(os.path.join(work, "profile_" + stamp))
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    if seed_layout is not None:
        with open(os.path.join(profile, "layout.json"), "w", encoding="utf-8") as f:
            f.write(seed_layout)
    log_path = os.path.join(os.path.abspath(work), "app_%s.log" % stamp)
    args = [exe, "--multi-instance", "on", "--remote-control", "--data-dir", win(data), "--profile-dir", win(profile),
            "--bg-pos", BG_POS, "--primary-override", PRIMARY, "--offscreen-guard", "on", "--log", win(log_path)] + list(extra)
    # 가상 모니터 2대(18000~21000·21000~24000, 메인은 2번 위): 복구를 켜도 메인이 '안 보이는 창'으로 판정돼 옮겨지지 않는다(retest_monitor_change 와 같은 조건)
    env = dict(os.environ, PD_VIRTUAL_MONITORS="2", WSLENV=os.environ.get("WSLENV", "") + ":PD_VIRTUAL_MONITORS")
    with open(os.path.join(os.path.abspath(work), "stdio_%s.log" % stamp), "ab") as out:
        subprocess.Popen(args, cwd=run_dir, env=env, stdin=subprocess.DEVNULL, stdout=out, stderr=out, start_new_session=True)
    info = os.path.join(data, "remote-control.json")
    for _ in range(90):
        bad = log_violation(log_path)
        if bad:
            sys.exit("단일 인스턴스 흔적 — 즉시 중단: " + bad)
        if os.path.exists(info):
            break
        time.sleep(1)
    else:
        sys.exit("연결 정보 파일이 생기지 않았다: " + info)
    os.environ["PD_REMOTE_INFO"] = info  # 사용자 인스턴스에 붙지 않게 임시 폴더 파일만 쓴다
    from retest_remote import Remote
    remote = Remote()
    status = None
    for _ in range(60):
        status = remote.call("status")
        if status and status.get("pid"):
            break
        time.sleep(1)
    pid = (status or {}).get("pid")
    cmd = ps("(Get-CimInstance Win32_Process -Filter \"ProcessId=%s\").CommandLine" % pid) if pid else ""
    if win(data) not in cmd:
        sys.exit("연결된 앱의 명령줄에 임시 폴더가 없다 — 남의 프로세스일 수 있어 중단: pid=%s" % pid)
    bad = log_violation(log_path)
    if bad:
        sys.exit("단일 인스턴스 흔적 — 즉시 중단: " + bad)
    return pid, info, log_path


def stop(pid, data_marker):
    """명령줄에 자기 임시 폴더가 있는 PID 만 종료한다."""
    cmd = ps("(Get-CimInstance Win32_Process -Filter \"ProcessId=%s\").CommandLine" % pid)
    if data_marker not in cmd:
        return "명령줄 불일치 — 종료하지 않음(pid=%s)" % pid
    done = subprocess.run(["/mnt/c/Windows/System32/taskkill.exe", "/PID", str(pid), "/F"], capture_output=True, timeout=30)
    return "종료 요청 pid=%s rc=%s" % (pid, done.returncode)


def main_hwnd(pid):
    """그 PID 의 보이는 최상위 창 중 가장 큰 창(메인). 화면 밖 창은 Process.MainWindowHandle 이 0 일 수 있어 직접 열거한다."""
    script = ("Add-Type @\"\nusing System;using System.Runtime.InteropServices;using System.Collections.Generic;\n"
              "public class MW{[StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,RR,B;}public delegate bool E(IntPtr h,IntPtr l);"
              "[DllImport(\"user32.dll\")]static extern bool EnumWindows(E e,IntPtr l);[DllImport(\"user32.dll\")]static extern bool IsWindowVisible(IntPtr h);"
              "[DllImport(\"user32.dll\")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);[DllImport(\"user32.dll\")]static extern bool GetWindowRect(IntPtr h,out R r);"
              "public static long Find(uint pid){long best=0,area=0;EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);R r;"
              "if(p==pid&&IsWindowVisible(h)&&GetWindowRect(h,out r)){long a=(long)(r.RR-r.L)*(r.B-r.T);if(a>area){area=a;best=h.ToInt64();}}return true;},IntPtr.Zero);return best;}}\n\"@\n"
              "[MW]::Find(%s)" % pid)
    return int(ps(script).strip() or 0)


def client_origin(hwnd):
    """클라이언트 (0,0) 의 화면 물리 좌표."""
    script = ("Add-Type @\"\nusing System;using System.Runtime.InteropServices;\npublic class CO{[StructLayout(LayoutKind.Sequential)]public struct P{public int X,Y;}"
              "[DllImport(\"user32.dll\")]public static extern bool ClientToScreen(IntPtr h,ref P p);}\n\"@\n"
              "$p=New-Object CO+P;[void][CO]::ClientToScreen([IntPtr]%d,[ref]$p);\"$($p.X) $($p.Y)\"" % hwnd)
    x, y = ps(script).split()
    return int(x), int(y)


def foreground_pid():
    script = ("Add-Type @\"\nusing System;using System.Runtime.InteropServices;\npublic class FG{[DllImport(\"user32.dll\")]public static extern IntPtr GetForegroundWindow();"
              "[DllImport(\"user32.dll\")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);}\n\"@\n"
              "$p=0;[void][FG]::GetWindowThreadProcessId([FG]::GetForegroundWindow(),[ref]$p);$p")
    return int(ps(script).strip() or 0)


def post_drag(hwnd, x1, y1, x2, y2, steps=20):
    """L2 끌기 — 화면 물리 좌표가 아니라 클라이언트 물리 좌표. 커서·전경을 건드리지 않는다."""
    tool = win(os.path.join(HERE, "post_mouse.ps1"))
    return subprocess.run([PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tool, "-Hwnd", str(hwnd), "-X1", str(int(x1)), "-Y1", str(int(y1)),
                           "-X2", str(int(x2)), "-Y2", str(int(y2)), "-Steps", str(steps), "-DelayMs", "15"],
                          capture_output=True, encoding="utf-8", errors="replace", timeout=60).stdout.strip()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-dir", required=True)
    parser.add_argument("--work", required=True)
    parsed = parser.parse_args()
    os.makedirs(parsed.work, exist_ok=True)
    print(json.dumps(dict(zip(("pid", "info", "log"), launch(parsed.run_dir, parsed.work))), ensure_ascii=False))
