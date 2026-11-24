#!/usr/bin/env python3
# Q2 #16 창 그림자 측정기 — 대상 창을 같은 크기·위치·바탕에서 활성/비활성으로 화면 캡처(CopyFromScreen)해 PNG·measure.json 만 남기는 측정 계층 도구
"""
측정 계층(D-LAYER)이다. 판정은 하지 않는다. 수치·기준 대조는 ../judge/shadow_q16_judge.py 가 맡는다.
근거 1회성 스크립트: logs/otest/c2/w_fg/q2_16/q16d.sh·q16.sh (+ mv.ps1·cap.ps1). 원본은 그대로 두고 여기로 승격했다.

사용:
  python3 -B shadow_q16.py --target-hwnd <hwnd> --target-pid <pid> --bg-hwnd <hwnd> --bg-pid <pid> \
      --own-pid <pid> [--own-pid <pid> ...] --out-dir <결과 폴더> [--label pd]
      [--rect 400,160,500,300] [--cap 360,120,580,380] [--settle 1.2] [--no-move]
  --target-hwnd/--target-pid : 그림자를 재는 창(예: Pd SDI 창, 또는 시험용으로 띄운 새 메모장)
  --bg-hwnd/--bg-pid         : 대상을 '비활성'으로 만들 때 전경으로 올리는 창(예: 같은 Pd 의 메인 창, 또는 도우미 창)
  --own-pid                  : 이 시험이 직접 띄운 프로세스 PID(반복 지정). 대상·전경 창의 PID 는 전부 이 목록 안이어야 한다.
  --label                    : 결과 파일 접두어(<label>_active.png · <label>_inactive.png). 기본 pd
  --rect L,T,W,H             : 대상 창을 옮길 사각형(창 전체 기준, 활성화 없이 이동). 기본 400,160,500,300
  --cap L,T,W,H              : 화면 캡처 사각형. 기본 360,120,580,380
  --no-move                  : 대상 창을 옮기지 않는다(이미 자리에 있을 때)
출력: <out-dir>/<label>_active.png, <label>_inactive.png, <label>_measure.json (프레임 사각형·캡처 원점·전경 기록)
rc: 0=캡처 완료, 2=인자 오류, 3=안전 검사 거부(사용자 창·사용자 메모장 의심, 실행 안 함), 4=PowerShell/캡처 실패

안전 규칙(README 공통 규칙 + 이번 도구)
 - 사용자 창·사용자 메모장은 만지지 않는다. --own-pid 에 없는 PID 의 창은 이동·전경·캡처 대상이 될 수 없다.
 - 대상/전경 창의 실제 PID(GetWindowThreadProcessId)를 인자와 대조하고, 프로세스가 notepad 이면 --own-pid 에 있을 때만 허용한다.
 - 대상이 현재 전경 창이어도 시작 시 거부하지 않지만, 끝나면 시작 전 전경 창을 복원한다(복원 대상이 own 이 아니면 복원하지 않고 기록만).
 - 실입력(SendInput·클릭·키)은 쓰지 않는다. 창 이동은 SWP_NOACTIVATE|SWP_NOZORDER, 전경은 AttachThreadInput→SetForegroundWindow.
 - 프로세스 종료는 하지 않는다.
"""
import argparse
import json
import os
import re
import subprocess
import sys

PS_MV = r"""# 지정 hwnd 창을 활성화 없이 지정 사각형(창 전체 기준)으로 옮기는 스크립트
param([long]$Hwnd,[int]$X,[int]$Y,[int]$W,[int]$H)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class MV { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int h2, uint f); }
"@
# SWP_NOACTIVATE|SWP_NOZORDER = 0x0014
$ok = [MV]::SetWindowPos([IntPtr]$Hwnd,[IntPtr]::Zero,$X,$Y,$W,$H,0x0014)
"move ok=$ok"
"""

PS_CAP = r"""# 화면 사각형을 CopyFromScreen 으로 캡처해 PNG 로 저장하는 스크립트(읽기 전용)
param([int]$L,[int]$T,[int]$W,[int]$H,[string]$Out)
Add-Type -AssemblyName System.Drawing
$b=New-Object System.Drawing.Bitmap($W,$H)
$g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($L,$T,0,0,(New-Object System.Drawing.Size($W,$H)))
$b.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $b.Dispose()
"cap ok=$Out"
"""

PS_INFO = r"""# 지정 hwnd 의 PID·프로세스 이름·제목·창 사각형·보이는 경계(DWM)를 한 줄로 출력하는 스크립트(읽기 전용)
param([long]$Hwnd)
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public class WI {
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
$h = [IntPtr]$Hwnd
if (-not [WI]::IsWindow($h)) { "info hwnd=$Hwnd exists=0"; exit 0 }
$p = 0; [void][WI]::GetWindowThreadProcessId($h, [ref]$p)
$sb = New-Object System.Text.StringBuilder 256; [void][WI]::GetWindowText($h, $sb, 256)
$wr = New-Object WI+RECT; [void][WI]::GetWindowRect($h, [ref]$wr)
$fr = New-Object WI+RECT; $hr = [WI]::DwmGetWindowAttribute($h, 9, [ref]$fr, 16)
$pn = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
"info hwnd=$Hwnd exists=1 pid=$p proc=$pn title='$($sb.ToString())' window=($($wr.L),$($wr.T))-($($wr.R),$($wr.B)) frame=($($fr.L),$($fr.T))-($($fr.R),$($fr.B)) hr=$hr"
"""

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
FG_INFO = os.path.join(_paths.TEST, "common", "measure", "fg_info.ps1")


def win_path(p):
    return subprocess.check_output(["wslpath", "-w", p], text=True).strip()


def ps1(script_win, *args, timeout=60):
    cmd = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script_win] + [str(a) for a in args]
    r = subprocess.run(cmd, capture_output=True, timeout=timeout)
    raw = r.stdout + r.stderr
    try:
        txt = raw.decode("cp949")
    except UnicodeDecodeError:
        txt = raw.decode("utf-8", "replace")
    return r.returncode, txt.replace("\r", "").strip()


def parse_info(txt):
    line = [ln for ln in txt.splitlines() if ln.startswith("info ")]
    if not line:
        return None
    s = line[-1]
    d = {}
    m = re.search(r"exists=(\d)", s)
    d["exists"] = bool(m and m.group(1) == "1")
    for k in ("pid",):
        m = re.search(k + r"=(\d+)", s)
        d[k] = int(m.group(1)) if m else None
    m = re.search(r"proc=(\S*)", s)
    d["proc"] = m.group(1) if m else ""
    m = re.search(r"title='(.*?)' window=", s)
    d["title"] = m.group(1) if m else ""
    m = re.search(r"frame=\((-?\d+),(-?\d+)\)-\((-?\d+),(-?\d+)\)", s)
    d["frame"] = [int(x) for x in m.groups()] if m else None
    m = re.search(r"window=\((-?\d+),(-?\d+)\)-\((-?\d+),(-?\d+)\)", s)
    d["window"] = [int(x) for x in m.groups()] if m else None
    return d


def parse_fg(txt):
    m = re.search(r"fg hwnd=(\d+) pid=(\d+) title='(.*?)' cursor=\((-?\d+),(-?\d+)\)", txt)
    if not m:
        return None
    return {"hwnd": int(m.group(1)), "pid": int(m.group(2)), "title": m.group(3), "cursor": [int(m.group(4)), int(m.group(5))]}


def ints(s, n, name):
    try:
        v = [int(x) for x in s.split(",")]
    except ValueError:
        v = []
    if len(v) != n:
        print("인자 오류: %s 는 정수 %d개(쉼표 구분)" % (name, n), file=sys.stderr)
        sys.exit(2)
    return v


def main():
    ap = argparse.ArgumentParser(description="Q2 #16 그림자 측정(캡처만, 판정 없음)")
    ap.add_argument("--target-hwnd", type=int, required=True)
    ap.add_argument("--target-pid", type=int, required=True)
    ap.add_argument("--bg-hwnd", type=int, required=True)
    ap.add_argument("--bg-pid", type=int, required=True)
    ap.add_argument("--own-pid", type=int, action="append", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--label", default="pd")
    ap.add_argument("--rect", default="400,160,500,300")
    ap.add_argument("--cap", default="360,120,580,380")
    ap.add_argument("--settle", type=float, default=1.2)
    ap.add_argument("--no-move", action="store_true")
    a = ap.parse_args()
    rect = ints(a.rect, 4, "--rect")
    cap = ints(a.cap, 4, "--cap")
    own = set(a.own_pid)
    os.makedirs(a.out_dir, exist_ok=True)
    tool_dir = os.path.join(a.out_dir, "_ps")
    os.makedirs(tool_dir, exist_ok=True)
    paths = {}
    for name, body in (("mv.ps1", PS_MV), ("cap.ps1", PS_CAP), ("info.ps1", PS_INFO)):
        p = os.path.join(tool_dir, name)
        with open(p, "w", encoding="utf-8-sig", newline="\r\n") as f:
            f.write(body)
        paths[name] = win_path(p)

    rec = {"label": a.label, "target_hwnd": a.target_hwnd, "target_pid": a.target_pid, "bg_hwnd": a.bg_hwnd, "bg_pid": a.bg_pid,
           "own_pids": sorted(own), "rect": rect, "cap": cap, "steps": []}

    # --- 안전 검사: 인자 PID ∈ own, 실제 PID == 인자 PID, notepad 는 own 일 때만 ---
    refuse = []
    for role, hwnd, pid in (("target", a.target_hwnd, a.target_pid), ("bg", a.bg_hwnd, a.bg_pid)):
        if pid not in own:
            refuse.append("%s PID %d 가 --own-pid 목록에 없음(사용자 창 의심)" % (role, pid))
        rc, txt = ps1(paths["info.ps1"], a.target_hwnd if role == "target" else a.bg_hwnd)
        info = parse_info(txt)
        rec[role + "_info_before"] = info
        if not info or not info["exists"]:
            refuse.append("%s hwnd %d 가 존재하지 않음" % (role, hwnd))
            continue
        if info["pid"] != pid:
            refuse.append("%s hwnd %d 의 실제 PID %s ≠ 인자 %d" % (role, hwnd, info["pid"], pid))
        if info["pid"] not in own:
            refuse.append("%s hwnd %d 의 실제 PID %s 가 --own-pid 밖(사용자 창)" % (role, hwnd, info["pid"]))
        if "notepad" in (info["proc"] or "").lower() and info["pid"] not in own:
            refuse.append("%s 가 own 이 아닌 메모장(사용자 메모장)" % role)
    if a.target_hwnd == a.bg_hwnd:
        refuse.append("대상과 전경 창이 같음(비활성을 만들 수 없음)")
    if refuse:
        rec["refused"] = refuse
        with open(os.path.join(a.out_dir, a.label + "_measure.json"), "w", encoding="utf-8") as f:
            json.dump(rec, f, ensure_ascii=False, indent=1)
        print("REFUSE:", "; ".join(refuse), file=sys.stderr)
        return 3

    # --- 시작 전 전경 기록 ---
    rc, txt = ps1(FG_INFO and win_path(FG_INFO))
    fg0 = parse_fg(txt)
    rec["fg_start"] = fg0

    def fg(h):
        rc, t = ps1(win_path(FG_INFO), "-Restore", h)
        import time
        time.sleep(a.settle)
        return parse_fg(t)

    def shot(tag):
        out = os.path.join(a.out_dir, "%s_%s.png" % (a.label, tag))
        rc, t = ps1(paths["cap.ps1"], "-L", cap[0], "-T", cap[1], "-W", cap[2], "-H", cap[3], "-Out", win_path(out))
        if not os.path.exists(out):
            print("캡처 실패: " + t, file=sys.stderr)
            return None
        return out

    if not a.no_move:
        ps1(paths["mv.ps1"], "-Hwnd", a.target_hwnd, "-X", rect[0], "-Y", rect[1], "-W", rect[2], "-H", rect[3])
        import time
        time.sleep(1)

    fgA = fg(a.target_hwnd)
    rec["fg_active"] = fgA
    if fgA and fgA["pid"] != a.target_pid:
        rec["steps"].append("경고: 활성 캡처 시 전경 PID %s ≠ 대상 PID" % fgA["pid"])
    pa = shot("active")
    _, t = ps1(paths["info.ps1"], a.target_hwnd)
    rec["target_info_active"] = parse_info(t)

    fgI = fg(a.bg_hwnd)
    rec["fg_inactive"] = fgI
    if fgI and fgI["pid"] != a.bg_pid:
        rec["steps"].append("경고: 비활성 캡처 시 전경 PID %s ≠ 전경용 창 PID" % fgI["pid"])
    pi = shot("inactive")
    _, t = ps1(paths["info.ps1"], a.target_hwnd)
    rec["target_info_inactive"] = parse_info(t)

    # 전경 복원(own 창일 때만)
    if fg0 and fg0["pid"] in own:
        ps1(win_path(FG_INFO), "-Restore", fg0["hwnd"])
        rec["fg_restored"] = True
    else:
        rec["fg_restored"] = False
        rec["steps"].append("시작 전 전경 창이 own 이 아니어서 복원하지 않음(사용자 창을 건드리지 않기 위해)")

    fa = rec["target_info_active"] and rec["target_info_active"]["frame"]
    fi = rec["target_info_inactive"] and rec["target_info_inactive"]["frame"]
    rec["frame"] = fa
    rec["frame_stable"] = bool(fa and fi and fa == fi)
    rec["origin"] = [cap[0], cap[1]]
    rec["files"] = {"active": pa, "inactive": pi}
    with open(os.path.join(a.out_dir, a.label + "_measure.json"), "w", encoding="utf-8") as f:
        json.dump(rec, f, ensure_ascii=False, indent=1)
    print("%s frame=%s stable=%s" % (a.label, fa, rec["frame_stable"]))
    return 0 if (pa and pi and fa) else 4


if __name__ == "__main__":
    sys.exit(main())
