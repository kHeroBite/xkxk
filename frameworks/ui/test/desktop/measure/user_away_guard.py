#!/usr/bin/env python3
# PC 비움 시험 중 사용자 복귀를 감지해 실입력·토폴로지·성능 측정을 멈추게 하는 공용 가드
"""
판단 규칙
    GetLastInputInfo 의 마지막 입력 시각 > 자기 주입 기록의 마지막 시각 + 1,500ms  → 복귀
    커서가 마지막 주입 위치에서 CURSOR_TOLERANCE_PX 를 넘게 벗어남                  → 복귀 정황
    복귀로 판단하면 logs/pcaway/user_returned.flag 를 남기고 UserReturned 를 던진다.
    유령 입력 보조 판정(2026-11-24 D-6): '주입 뒤 입력' 하나만 걸렸고 감시 데몬(pc_away_monitor run) 상태가 mode=phantom_input_fallback
    away 이며 최근 20초까지 덮고 있으면, 0.6초 확인 창(커서 정지·눌린 키 0·전경 불변)을 다시 보고 깨끗할 때만 통과시킨다.
    결과에 mode·phantom 근거를 남긴다. 커서 이탈은 보조 판정으로 덮지 않는다. 끄기는 PD_PHANTOM_FALLBACK=0.

주입 시각 기록 책임 (이 책임이 빠지면 자기 주입이 복귀로 오판돼 시험이 멈춘다)
    safe_key.ps1·safe_touch.ps1  스크립트 안에서 직접 기록한다. 호출자는 아무것도 하지 않는다.
    safe_click.ps1               기록 코드가 없다. 호출자(retest_real_input.py 등)가 아래처럼 감싼다.

        from user_away_guard import check, injection, UserReturned
        check()                                  # 조작 앞마다 부른다
        with injection("click", x, y):           # 직전·직후 기록. 드래그면 끝점을 넘긴다
            run_safe_click(...)                  # 실패하면 위치 없이 시각만 남는다

재개
    플래그만 지우면 재개되지 않는다. 마지막 입력이 여전히 기준보다 늦어 다음 check() 가 다시 복귀로 판단한다.
    재개는 사용자의 재지시를 받은 뒤 arm("지시 내용") 으로만 한다. 이력은 injections.jsonl 에 남는다.

한계
    주입 뒤 1,500ms 안의 사용자 입력은 구분하지 못한다(커서 이탈은 마우스만 보강한다).
    같은 PC 의 다른 세션이 주입한 입력은 복귀로 판단한다(안전 쪽 오판).
    check() 는 PowerShell 을 띄우므로 1회에 수 초 걸릴 수 있다.
"""
import argparse
import base64
import contextlib
import json
import os
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
POC_UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
LOG_DIR = os.path.join(POC_UI, "logs", "pcaway")
FLAG_PATH = os.path.join(LOG_DIR, "user_returned.flag")
INJECTION_LOG = os.path.join(LOG_DIR, "injections.jsonl")

GRACE_MS = 1500
CURSOR_TOLERANCE_PX = 8  # safe_click 실측 잔차 ±2px 의 여유값
ARM_MIN_IDLE_MS = 1500
TAIL_BYTES = 65536
PROBE_TIMEOUT_S = 60  # 시스템이 밀리면 PowerShell 기동이 수십 초 걸린다
KIND_ARM = "arm"
KIND_RETURNED = "returned"
RC_AWAY, RC_UNAVAILABLE, RC_RETURNED = 0, 2, 3

PROBE_PS = r"""
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PdAwayProbe {
    [StructLayout(LayoutKind.Sequential)] public struct LII { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LII p);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("kernel32.dll")] public static extern uint GetTickCount();
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
    public static string Read() {
        LII l = new LII(); l.cbSize = (uint)Marshal.SizeOf(typeof(LII));
        if (!GetLastInputInfo(ref l)) throw new Exception("GetLastInputInfo");
        PT c; if (!GetCursorPos(out c)) throw new Exception("GetCursorPos");
        uint idle = unchecked(GetTickCount() - l.dwTime);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return "{\"win_now_ms\":" + now + ",\"idle_ms\":" + idle + ",\"cursor_x\":" + c.X + ",\"cursor_y\":" + c.Y + "}";
    }
}
'@
try { [void][PdAwayProbe]::SetProcessDpiAwareness(2) } catch { }
[Console]::Out.WriteLine([PdAwayProbe]::Read())
"""

_clock_offset_ms = None


class UserReturned(BaseException):
    """측정 도구의 '실패도 기록하고 계속' 처리에 삼켜지지 않게 BaseException 에서 파생한다."""


class GuardUnavailable(BaseException):
    """상태를 읽지 못했다. 안전을 확인할 수 없으므로 호출자는 복귀와 똑같이 멈춘다. 플래그는 남기지 않는다."""


DIAG_PS = r"""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class PdAwayDiag {
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
    public static string Foreground() {
        IntPtr h = GetForegroundWindow(); uint pid = 0; if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid);
        StringBuilder t = new StringBuilder(200), c = new StringBuilder(100);
        if (h != IntPtr.Zero) { GetWindowText(h, t, 200); GetClassName(h, c, 100); }
        return (long)h + "|" + pid + "|" + c + "|" + t;
    }
}
'@
try { [void][PdAwayDiag]::SetProcessDpiAwareness(2) } catch { }
$n = [int]$args[0]
$samples = @()
for ($i = 0; $i -lt $n; $i++) {
    $p = New-Object PdAwayDiag+PT; [void][PdAwayDiag]::GetCursorPos([ref] $p)
    $samples += ,@($p.X, $p.Y)
    if ($i -lt $n - 1) { Start-Sleep -Milliseconds 100 }
}
$mons = ([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { '{0}:{1},{2},{3},{4}:{5}' -f $_.DeviceName, $_.Bounds.X, $_.Bounds.Y, $_.Bounds.Width, $_.Bounds.Height, $_.Primary }) -join ';'
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($mons)) | ForEach-Object { $_.ToString('x2') })
[Console]::Out.WriteLine((@{ cursor_samples_100ms = $samples; foreground = [PdAwayDiag]::Foreground(); monitors = $mons; monitor_hash = $hash.Substring(0, 16) } | ConvertTo-Json -Compress))
"""


def collect_diag(samples=5):
    """복귀 표지·시작 기준을 남길 때 곁들이는 진단(읽기 전용). 커서 표본·전경 창·모니터 구성 해시를 모은다. 판단에는 쓰지 않으며 실패해도 오류 값만 남긴다."""
    encoded = base64.b64encode(DIAG_PS.replace("$args[0]", str(int(samples))).encode("utf-16-le")).decode("ascii")
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded]
    try:
        done = subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True, errors="replace", timeout=20)
        return json.loads(done.stdout.strip().splitlines()[-1]) if done.returncode == 0 else {"error": "rc=%s %s" % (done.returncode, done.stderr[-200:])}
    except (OSError, subprocess.SubprocessError, ValueError, IndexError) as e:
        return {"error": repr(e)}


def read_probe():
    """Windows 의 마지막 입력·커서·현재 시각을 읽는다. 읽기 전용이며 입력을 만들지 않는다."""
    global _clock_offset_ms
    encoded = base64.b64encode(PROBE_PS.encode("utf-16-le")).decode("ascii")
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded]
    try:
        done = subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True, errors="replace", timeout=PROBE_TIMEOUT_S)
        probe = json.loads(done.stdout.strip().splitlines()[-1]) if done.returncode == 0 else None
    except (OSError, subprocess.SubprocessError, ValueError, IndexError) as e:
        raise GuardUnavailable("Windows 입력 상태를 읽지 못했다: %r" % (e,))
    if not probe:
        raise GuardUnavailable("Windows 입력 상태를 읽지 못했다: rc=%s %s" % (done.returncode, done.stderr[-300:]))
    _clock_offset_ms = probe["win_now_ms"] - int(time.time() * 1000)
    return probe


def _now_ms():
    """주입 기록용 Windows 기준 시각. WSL 시계가 어긋나도 판단이 틀어지지 않게 차이를 보정한다."""
    if _clock_offset_ms is None:
        read_probe()
    return int(time.time() * 1000) + _clock_offset_ms


def _append(record, log_path):
    os.makedirs(os.path.dirname(log_path), exist_ok=True)
    fd = os.open(log_path, os.O_WRONLY | os.O_APPEND | os.O_CREAT, 0o644)
    try:
        os.write(fd, (json.dumps(record, ensure_ascii=False) + "\n").encode("utf-8"))
    finally:
        os.close(fd)


def record_injection(kind, x=None, y=None, log_path=None):
    """주입 시각을 공용 파일에 남긴다. x,y 는 주입 뒤 커서가 있어야 할 위치이며, 모르면 생략한다."""
    if kind in (KIND_ARM, KIND_RETURNED):
        raise ValueError("예약된 기록 종류다: %s" % kind)
    _append({"t_ms": _now_ms(), "kind": kind, "x": x, "y": y, "source": "python"}, log_path or INJECTION_LOG)


@contextlib.contextmanager
def injection(kind, x=None, y=None, log_path=None):
    """safe_click 호출을 감싼다. 직전은 시각만, 직후는 위치까지 남긴다. 실패하면 커서가 안 움직였을 수 있어 위치를 남기지 않는다."""
    record_injection(kind + "_pre", log_path=log_path)
    try:
        yield
    except BaseException:
        record_injection(kind + "_failed", log_path=log_path)
        raise
    record_injection(kind, x, y, log_path=log_path)


def read_records(log_path=None):
    path = log_path or INJECTION_LOG
    try:
        with open(path, "rb") as f:
            f.seek(0, os.SEEK_END)
            f.seek(max(0, f.tell() - TAIL_BYTES))
            raw = f.read()
    except FileNotFoundError:
        return []
    records = []
    for line in raw.decode("utf-8", "replace").splitlines():
        try:
            rec = json.loads(line)
        except ValueError:
            continue  # 꼬리 절단·동시 쓰기로 깨진 줄은 건너뛴다
        if isinstance(rec, dict) and isinstance(rec.get("t_ms"), (int, float)):
            records.append(rec)
    return records


def judge(probe, records):
    """순수 판단. 기록이 없으면 armed=False 를 돌려준다. reasons 가 비면 사용자가 없는 것이다."""
    marks = [r for r in records if r.get("kind") != KIND_RETURNED]
    last_input_ms = probe["win_now_ms"] - probe["idle_ms"]
    result = {"last_input_ms": last_input_ms, "idle_ms": probe["idle_ms"], "cursor": [probe["cursor_x"], probe["cursor_y"]],
              "armed": bool(marks), "last_injection_ms": None, "expected_cursor": None, "reasons": []}
    if not marks:
        return result
    last_ms = max(r["t_ms"] for r in marks)
    result["last_injection_ms"] = last_ms
    if last_input_ms > last_ms + GRACE_MS:
        result["reasons"].append("input_after_injection")
    placed = [r for r in marks if r.get("x") is not None and r.get("y") is not None]
    if placed:
        anchor = max(placed, key=lambda r: r["t_ms"])
        result["expected_cursor"] = [anchor["x"], anchor["y"]]
        # 터치는 앱이 마우스로 승격할 때만 커서가 옮겨 가고 시점도 늦다(실측). 그래서 alt 위치도 인정한다
        allowed = [(anchor["x"], anchor["y"])]
        if anchor.get("alt_x") is not None and anchor.get("alt_y") is not None:
            allowed.append((anchor["alt_x"], anchor["alt_y"]))
        if not any(abs(probe["cursor_x"] - ax) <= CURSOR_TOLERANCE_PX and abs(probe["cursor_y"] - ay) <= CURSOR_TOLERANCE_PX for ax, ay in allowed):
            result["reasons"].append("cursor_moved")
    return result


# ── 유령 입력 보조 판정(phantom_input_fallback, 2026-11-24 D-6) ─────────────────────────────
# 어떤 장치가 약 60Hz 로 입력을 내면 GetLastInputInfo 유휴가 늘 수 ms 라 위 판단이 항상 '입력 있음'이 된다.
# 그때만 쓰는 보수적 보조 판정이다. 1순위는 언제나 유휴 기준이고, 보조 판정은 아래 셋을 연속 창 동안 모두 만족할 때만 away 다.
#   ① 커서 완전 정지(시험 주입 구간·주입 기록 끝점·창 시작 커서로의 복원은 예외)
#   ② GetAsyncKeyState 로 본 눌린 키·마우스 버튼 0(주입 구간 예외)
#   ③ 전경 창이 시작 창이거나 시험 프로세스(run_desktop_bg_pids 기록)뿐
# 연속 감시는 pc_away_monitor run 이 하고(창 길이 기본 5분, --phantom-window-min), 결과를 상태 파일 mode·phantom 에 남긴다.
# 끄기: 환경변수 PD_PHANTOM_FALLBACK=0 또는 pc_away_monitor run --no-phantom-fallback(그러면 옛 판정과 같다).
PHANTOM_ENABLED = os.environ.get("PD_PHANTOM_FALLBACK", "1") != "0"
PHANTOM_STATE = os.environ.get("PD_AWAY_STATE")  # pc_away_monitor.check(state=...) 가 채운다
PHANTOM_MODE = "phantom_input_fallback"
PHANTOM_FRESH_MS = 20000   # 데몬 감시가 지금으로부터 이만큼 안까지 덮고 있어야 믿는다
PHANTOM_GAP_MAX_MS = 15000  # 감시 창 사이 공백이 이보다 길면 연속성이 끊긴 것으로 본다
PRE_MAX_MS = 10000          # 끝나지 않은 주입(*_pre) 구간 상한(pc_away_monitor 와 같은 값)
PID_DIR = os.path.join(POC_UI, "logs", "run_desktop_bg_pids")

WATCH_PS = r"""
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Text; using System.Threading; using System.Runtime.InteropServices;
public static class PdPhantomWatch {
    [StructLayout(LayoutKind.Sequential)] public struct LII { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X; public int Y; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LII p);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
    static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    public static string Watch(int durMs, int stepMs) {
        long t0 = Now(); int n = 0; uint idleMin = uint.MaxValue, idleMax = 0, idleLast = 0;
        StringBuilder cur = new StringBuilder(), keys = new StringBuilder(), fg = new StringBuilder();
        int lx = int.MinValue, ly = int.MinValue, nkeys = 0; IntPtr lfg = new IntPtr(-1); string lkeys = "";
        while (true) {
            long t = Now();
            LII l = new LII(); l.cbSize = (uint)Marshal.SizeOf(typeof(LII));
            if (GetLastInputInfo(ref l)) { idleLast = unchecked(GetTickCount() - l.dwTime); idleMin = Math.Min(idleMin, idleLast); idleMax = Math.Max(idleMax, idleLast); }
            PT c; if (GetCursorPos(out c) && (c.X != lx || c.Y != ly)) { cur.Append((cur.Length > 0 ? "," : "") + "[" + t + "," + c.X + "," + c.Y + "]"); lx = c.X; ly = c.Y; }
            StringBuilder down = new StringBuilder();
            for (int vk = 1; vk < 255; vk++) if ((GetAsyncKeyState(vk) & 0x8000) != 0) down.Append((down.Length > 0 ? "," : "") + vk);
            string ds = down.ToString();
            if (ds.Length > 0 && ds != lkeys && nkeys < 200) { keys.Append((keys.Length > 0 ? "," : "") + "[" + t + ",[" + ds + "]]"); nkeys++; }
            lkeys = ds;
            IntPtr h = GetForegroundWindow();
            if (h != lfg) { uint pid = 0; if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid); fg.Append((fg.Length > 0 ? "," : "") + "[" + t + "," + (long)h + "," + pid + "]"); lfg = h; }
            n++;
            if (Now() - t0 >= durMs) break;
            Thread.Sleep(stepMs);
        }
        return "{\"win_start_ms\":" + t0 + ",\"win_end_ms\":" + Now() + ",\"samples\":" + n + ",\"step_ms\":" + stepMs
            + ",\"idle_min_ms\":" + (idleMin == uint.MaxValue ? 0 : idleMin) + ",\"idle_max_ms\":" + idleMax + ",\"idle_ms\":" + idleLast
            + ",\"cursor\":[" + cur + "],\"keys\":[" + keys + "],\"fg\":[" + fg + "]}";
    }
}
'@
try { [void][PdPhantomWatch]::SetProcessDpiAwareness(2) } catch { }
[Console]::Out.WriteLine([PdPhantomWatch]::Watch(__DUR__, __STEP__))
"""


def read_window(duration_ms=1000, step_ms=20):
    """duration_ms 동안 step_ms 간격으로 유휴·커서·눌린 키·전경을 읽는다(읽기 전용, 입력을 만들지 않는다).
    cursor·fg 는 바뀔 때만(창 첫 표본은 항상) [t,x,y]·[t,hwnd,pid], keys 는 눌린 키 집합이 바뀔 때 [t,[vk...]] 다."""
    script = WATCH_PS.replace("__DUR__", str(int(duration_ms))).replace("__STEP__", str(int(step_ms)))
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded]
    try:
        done = subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True, errors="replace", timeout=PROBE_TIMEOUT_S + duration_ms / 1000.0)
        win = json.loads(done.stdout.strip().splitlines()[-1]) if done.returncode == 0 else None
    except (OSError, subprocess.SubprocessError, ValueError, IndexError) as e:
        raise GuardUnavailable("감시 창을 읽지 못했다: %r" % (e,))
    if not win:
        raise GuardUnavailable("감시 창을 읽지 못했다: rc=%s %s" % (done.returncode, done.stderr[-300:]))
    return win


def test_pids(pid_dir=None, max_age_s=2 * 86400):
    """run_desktop_bg.sh 가 띄운 시험 인스턴스 PID(최근 이틀 기록). 전경이 이 PID 로 바뀌는 것은 사용자 복귀가 아니다."""
    pids, d = set(), pid_dir or PID_DIR
    try:
        names = os.listdir(d)
    except OSError:
        return pids
    for name in names:
        p = os.path.join(d, name)
        try:
            if time.time() - os.path.getmtime(p) > max_age_s:
                continue
            with open(p, encoding="utf-8") as f:
                pids.update(int(line.split()[0]) for line in f if line.split() and line.split()[0].isdigit())
        except OSError:
            continue
    return pids


def _injected_at(t, records):
    """t 가 시험 주입 구간 안인가. 완료 기록은 t_ms~+GRACE_MS, *_pre 는 같은 이름의 완료(또는 _failed) 기록까지,
    완료 기록이 없으면(끝나지 않은 주입) t_ms~+PRE_MAX_MS."""
    marks = [r for r in records if r.get("kind") not in (KIND_RETURNED, KIND_ARM) and isinstance(r.get("t_ms"), (int, float))]
    for r in marks:
        kind = str(r.get("kind", ""))
        if kind.endswith("_pre"):
            ends = [m["t_ms"] for m in marks if m.get("kind") in (kind[:-4], kind[:-4] + "_failed") and m["t_ms"] >= r["t_ms"]]
            end = min(ends) if ends else r["t_ms"] + PRE_MAX_MS
        else:
            end = r["t_ms"] + GRACE_MS
        if r["t_ms"] - 200 <= t <= end:  # 기록과 실제 입력 사이 수백 ms 어긋남 여유
            return True
    return False


def _near(x, y, points):
    return any(abs(x - px) <= CURSOR_TOLERANCE_PX and abs(y - py) <= CURSOR_TOLERANCE_PX for px, py in points)


def phantom_scan(windows, records, pids, base=None):
    """감시 창 목록(시간순)에서 사용자 입력으로 볼 사건을 찾는 순수 판단.
    base={"cursor":[x,y], "fg_hwnd":h} 가 없으면 첫 창 첫 표본으로 잡는다. 돌려주는 값의 violations 가 비어야 깨끗한 구간이다."""
    out = {"violations": [], "samples": 0, "start_ms": None, "end_ms": None, "base": dict(base) if base else None, "cursor": None}
    prev_end = None
    for w in windows:
        cur, fg = w.get("cursor") or [], w.get("fg") or []
        if out["base"] is None:
            out["base"] = {"cursor": None, "fg_hwnd": None}
        if out["base"].get("cursor") is None and cur:  # 기준이 비었으면 이 창 첫 표본을 기준으로 잡는다(첫 표본을 위반으로 세지 않게)
            out["base"]["cursor"] = list(cur[0][1:3])
        if out["base"].get("fg_hwnd") is None and fg:
            out["base"]["fg_hwnd"] = fg[0][1]
        if out["start_ms"] is None:
            out["start_ms"] = w["win_start_ms"]
        if prev_end is not None and w["win_start_ms"] - prev_end > PHANTOM_GAP_MAX_MS:
            out["violations"].append({"t": w["win_start_ms"], "kind": "gap", "detail": w["win_start_ms"] - prev_end})
        prev_end, out["end_ms"] = w["win_end_ms"], w["win_end_ms"]
        out["samples"] += int(w.get("samples") or 0)
        bc = out["base"].get("cursor")
        for t, x, y in cur:
            out["cursor"] = [x, y]
            if _injected_at(t, records):
                continue
            placed = [(r["x"], r["y"]) for r in records if r.get("x") is not None and r.get("y") is not None and r.get("kind") not in (KIND_RETURNED, KIND_ARM) and r["t_ms"] <= t]
            # 허용: 창 시작 위치로의 정확한 복원(SetCursorPos 라 같은 좌표), 마지막 주입 끝점 ±CURSOR_TOLERANCE_PX(safe_click 잔차). 그 밖은 1px 도 위반
            if not ((bc and [x, y] == list(bc)) or _near(x, y, placed[-1:])):
                out["violations"].append({"t": t, "kind": "cursor", "detail": [x, y]})
        for t, vks in w.get("keys") or []:
            if not _injected_at(t, records):
                out["violations"].append({"t": t, "kind": "key", "detail": vks})
        for t, h, pid in fg:
            if h != out["base"].get("fg_hwnd") and pid not in pids and not _injected_at(t, records):
                out["violations"].append({"t": t, "kind": "fg", "detail": [h, pid]})
    return out


def phantom_ok(now_ms=None, state_path=None):
    """감시 데몬 상태가 '보조 판정으로 away' 이고 지금까지 덮고 있으면 그 근거(dict), 아니면 None. 끈 상태면 언제나 None."""
    path = state_path or PHANTOM_STATE
    if not PHANTOM_ENABLED or not path:
        return None
    try:
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
    except (OSError, ValueError):
        return None
    ph = d.get("phantom") or {}
    now_ms = now_ms if now_ms is not None else _now_ms()
    if d.get("state") != "away" or d.get("mode") != PHANTOM_MODE or not isinstance(ph.get("covered_until_ms"), (int, float)):
        return None
    if ph["covered_until_ms"] < now_ms - PHANTOM_FRESH_MS:
        return None
    return dict(ph, mode=PHANTOM_MODE, state_path=path)


def _phantom_accept(result, probe, records, watch_fn, now_ms):
    """조작 직전 확인(check)에서 '주입 뒤 입력' 하나만 걸렸을 때, 데몬 근거 + 짧은 창(키·커서·전경)으로 다시 본다. 받아들이면 근거를 결과에 남긴다."""
    ev = phantom_ok(now_ms)
    if ev is None:
        return False
    win = (watch_fn or (lambda: read_window(600, 20)))()
    # 짧은 창은 전경 기준이 없어 창 첫 전경을 기준으로 창 안 변화만 본다
    base = {"cursor": result.get("expected_cursor") or [probe["cursor_x"], probe["cursor_y"]], "fg_hwnd": (win.get("fg") or [[0, None]])[0][1]}
    scan = phantom_scan([win], records, test_pids(), base)
    bad = list(scan["violations"])
    still = {(x, y) for t, x, y in win.get("cursor") or [] if not _injected_at(t, records)}
    if len(still) > 1:  # 확인 창(주입 없음) 안에서는 커서가 1px 이라도 바뀌면 위반이다
        bad.append({"t": win["win_start_ms"], "kind": "cursor", "detail": sorted(still)})
    if bad:
        result["phantom_rejected"] = bad[:5]
        return False
    result["mode"] = PHANTOM_MODE
    result["phantom"] = {"daemon": ev, "check_window": {"samples": win.get("samples"), "window_ms": win["win_end_ms"] - win["win_start_ms"], "cursor": scan["cursor"]}}
    return True


def _raise_flag(detail, flag_path, log_path, diag=False):
    """처음 판단한 시각을 보존하려고 이미 있는 플래그는 덮어쓰지 않는다. 판정기가 mtime 을 기준으로 쓴다.
    diag=True 면 판단 값은 그대로 두고 진단(커서 표본·전경 창·모니터 구성 해시)만 덧붙인다."""
    if diag:
        detail = dict(detail, diag=collect_diag())
    os.makedirs(os.path.dirname(flag_path), exist_ok=True)
    try:
        fd = os.open(flag_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
    except FileExistsError:
        return
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(detail, f, ensure_ascii=False)
    record = {"t_ms": _now_ms(), "kind": KIND_RETURNED, "reasons": detail.get("reasons")}
    if detail.get("diag") is not None:
        record["diag"] = detail["diag"]
    _append(record, log_path)


def check(flag_path=None, log_path=None, probe_fn=None, watch_fn=None):
    """조작 앞마다 부른다. 사용자가 없으면 판단 값을 돌려주고, 복귀면 UserReturned, 판단 불가면 GuardUnavailable 을 던진다.
    유휴 기준이 '주입 뒤 입력' 하나만 걸렸으면 유령 입력 보조 판정(_phantom_accept)으로 다시 본다. 커서 이탈은 보조 판정으로 덮지 않는다."""
    flag_path = flag_path or FLAG_PATH
    log_path = log_path or INJECTION_LOG
    if os.path.exists(flag_path):
        raise UserReturned("복귀 플래그가 있다. 재개는 사용자 재지시 뒤 arm 으로만 한다: " + flag_path)
    probe = (probe_fn or read_probe)()
    records = read_records(log_path)
    result = judge(probe, records)
    if result["reasons"] == ["input_after_injection"] and _phantom_accept(result, probe, records, watch_fn, probe["win_now_ms"]):
        result["reasons"] = []
    if not result["armed"]:
        if probe["idle_ms"] < ARM_MIN_IDLE_MS and not _phantom_accept(result, probe, records, watch_fn, probe["win_now_ms"]):
            result["reasons"].append("active_at_arm")
        else:
            rec = {"t_ms": probe["win_now_ms"], "kind": KIND_ARM, "x": probe["cursor_x"], "y": probe["cursor_y"], "reason": "자동 시작"}
            if probe_fn is None:
                rec["diag"] = collect_diag(1)
            _append(rec, log_path)
            result["armed"] = True
            return result
    if result["reasons"]:
        _raise_flag(result, flag_path, log_path, diag=probe_fn is None)
        raise UserReturned("사용자 복귀 판단: %s" % ",".join(result["reasons"]))
    return result


def arm(user_instruction, flag_path=None, log_path=None, probe_fn=None):
    """사용자 재지시를 받은 뒤에만 부른다. 기준을 지금으로 다시 잡고 플래그를 치운다. 지금도 입력 중이면 거부한다."""
    flag_path = flag_path or FLAG_PATH
    log_path = log_path or INJECTION_LOG
    if not user_instruction or not user_instruction.strip():
        raise ValueError("사용자 재지시 내용이 필요하다")
    probe = (probe_fn or read_probe)()
    if probe["idle_ms"] < ARM_MIN_IDLE_MS and phantom_ok(probe["win_now_ms"]) is None:  # 유령 입력 보조 판정이 away 면 유휴가 작아도 받는다
        raise UserReturned("지금 입력이 있어 시작 기준을 잡지 않는다: idle=%dms" % probe["idle_ms"])
    rec = {"t_ms": probe["win_now_ms"], "kind": KIND_ARM, "x": probe["cursor_x"], "y": probe["cursor_y"], "reason": user_instruction.strip()}
    if probe_fn is None:
        rec["diag"] = collect_diag(1)  # 복귀 표지 때 모니터 구성 해시와 비교하는 기준
    _append(rec, log_path)
    with contextlib.suppress(FileNotFoundError):
        os.remove(flag_path)
    return probe


def user_returned(flag_path=None):
    """결과 JSON 의 safety.user_returned 값. 플래그 존재 여부만 본다."""
    return os.path.exists(flag_path or FLAG_PATH)


def _ps_run(script, *args):
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
           subprocess.check_output(["wslpath", "-w", os.path.join(HERE, script)], text=True).strip()] + list(args)
    return subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True, errors="replace", timeout=PROBE_TIMEOUT_S)


def _ps_rc(script, *args):
    return _ps_run(script, *args).returncode


def self_test():
    """가짜 상태로 판단 규칙을 재고, 실제 호출은 입력을 만들지 않는 읽기·거부 경로만 잰다."""
    global _clock_offset_ms
    failures = []

    def expect(name, ok):
        print(("PASS " if ok else "FAIL ") + name)
        if not ok:
            failures.append(name)

    def probe(idle, now=1000000, x=100, y=100):
        return {"win_now_ms": now, "idle_ms": idle, "cursor_x": x, "cursor_y": y}

    inj = lambda t, x=None, y=None, kind="click": {"t_ms": t, "kind": kind, "x": x, "y": y}
    now = 1000000
    expect("기록 없음은 시작 전", judge(probe(5000), [])["armed"] is False)
    expect("주입 직후 입력은 자기 입력", not judge(probe(0, now), [inj(now - 100)])["reasons"])
    expect("경계 +1500ms 는 복귀 아님", not judge(probe(0, now), [inj(now - GRACE_MS)])["reasons"])
    expect("+1501ms 입력은 복귀", judge(probe(0, now), [inj(now - GRACE_MS - 1)])["reasons"] == ["input_after_injection"])
    expect("기록 없는 주입 뒤 입력은 복귀", "input_after_injection" in judge(probe(10, now), [inj(now - 60000)])["reasons"])
    expect("주입 전 입력은 복귀 아님", not judge(probe(90000, now), [inj(now - 60000)])["reasons"])
    expect("커서 이탈은 복귀", "cursor_moved" in judge(probe(9000, now, 200, 100), [inj(now - 100, 100, 100)])["reasons"])
    expect("허용 오차 안 커서는 복귀 아님", not judge(probe(9000, now, 100 + CURSOR_TOLERANCE_PX, 100), [inj(now - 100, 100, 100)])["reasons"])
    expect("위치 없는 키 기록은 커서 기준을 안 바꿈",
           judge(probe(9000, now, 100, 100), [inj(now - 500, 100, 100), inj(now - 100, kind="key")])["expected_cursor"] == [100, 100])
    touch = dict(inj(now - 100, 100, 100, "touch"), alt_x=500, alt_y=500)
    expect("터치 뒤 커서는 끝점 허용", not judge(probe(9000, now, 100, 100), [touch])["reasons"])
    expect("터치 뒤 커서는 이전 위치 허용", not judge(probe(9000, now, 500, 500), [touch])["reasons"])
    expect("터치 뒤 다른 위치는 복귀", "cursor_moved" in judge(probe(9000, now, 900, 900), [touch])["reasons"])
    expect("복귀 기록은 주입 기준이 아님", "input_after_injection" in judge(probe(10, now), [inj(now - 60000), inj(now - 5, kind=KIND_RETURNED)])["reasons"])

    with tempfile.TemporaryDirectory(prefix="pd_away_") as d:
        flag, log = os.path.join(d, "flag"), os.path.join(d, "log.jsonl")
        _clock_offset_ms = now - int(time.time() * 1000)  # 가짜 시각으로 맞춰 실제 PowerShell 호출을 막는다
        fake = lambda idle, x=100, y=100: (lambda: probe(idle, now, x, y))

        def raises(exc, fn):
            try:
                fn()
            except exc:
                return True
            return False

        expect("입력 중 자동 시작은 복귀", raises(UserReturned, lambda: check(flag, log, fake(10))) and os.path.exists(flag))
        os.remove(flag)
        os.remove(log)
        expect("유휴 중 자동 시작", check(flag, log, fake(5000))["armed"] and read_records(log)[-1]["kind"] == KIND_ARM)
        with injection("click", 100, 100, log_path=log):
            pass
        kinds = [r["kind"] for r in read_records(log)]
        expect("injection 은 직전·직후를 남김", kinds[-2:] == ["click_pre", "click"] and read_records(log)[-2]["x"] is None and read_records(log)[-1]["x"] == 100)
        with contextlib.suppress(RuntimeError):
            with injection("click", 999, 999, log_path=log):
                raise RuntimeError("safe_click 실패")
        expect("실패한 주입은 위치를 남기지 않음", read_records(log)[-1]["kind"] == "click_failed" and read_records(log)[-1]["x"] is None)
        with open(log, "ab") as f:
            f.write(b"{broken\n")
        expect("깨진 줄은 건너뜀", read_records(log)[-1]["kind"] == "click_failed")
        expect("사용자 없음은 통과", not check(flag, log, fake(5000))["reasons"])
        expect("복귀면 멈춤과 플래그", raises(UserReturned, lambda: check(flag, log, lambda: probe(0, now + 60000))) and user_returned(flag))
        first = os.path.getmtime(flag)
        expect("플래그가 있으면 읽기 전에 거부", raises(UserReturned, lambda: check(flag, log, lambda: 1 / 0)))
        _raise_flag({"reasons": ["again"]}, flag, log)
        expect("플래그는 덮어쓰지 않음", os.path.getmtime(flag) == first and "again" not in open(flag, encoding="utf-8").read())
        os.remove(flag)
        expect("플래그 삭제만으로는 재개 안 됨", raises(UserReturned, lambda: check(flag, log, lambda: probe(0, now + 60000))) and user_returned(flag))
        expect("재지시 없는 arm 은 거부", raises(ValueError, lambda: arm("  ", flag, log, fake(5000))) and user_returned(flag))
        expect("입력 중 arm 은 거부", raises(UserReturned, lambda: arm("다시 시작", flag, log, fake(10))) and user_returned(flag))
        arm("다시 시작", flag, log, lambda: probe(5000, now + 70000))
        expect("arm 뒤 재개", not user_returned(flag) and not check(flag, log, lambda: probe(5000, now + 70100))["reasons"])

        # 유령 입력 보조 판정(D-6): 유휴는 늘 0 인데 데몬 근거가 phantom away 이고 짧은 창이 깨끗하면 통과, 아니면 지금처럼 복귀
        global PHANTOM_STATE, PHANTOM_ENABLED
        saved_state, saved_enabled = PHANTOM_STATE, PHANTOM_ENABLED
        st = os.path.join(d, "pc_away.json")
        t1 = now + 200000

        def write_state(covered, mode=PHANTOM_MODE, state="away"):
            with open(st, "w", encoding="utf-8") as f:
                json.dump({"state": state, "mode": mode, "phantom": {"window_min": 5, "clean_ms": 300000, "covered_until_ms": covered, "samples": 15000, "cursor": [100, 100]}}, f)

        def watch(keys=None, cursor=None, fg=None):
            return lambda: {"win_start_ms": t1, "win_end_ms": t1 + 600, "samples": 30, "idle_ms": 3, "cursor": cursor or [[t1, 100, 100]], "keys": keys or [], "fg": fg or [[t1, 111, 999]]}

        def ph_check(name, want_pass, watch_fn, cursor=(100, 100)):
            f2, l2 = os.path.join(d, name + ".flag"), os.path.join(d, name + ".jsonl")
            _append({"t_ms": t1 - 60000, "kind": "click", "x": 100, "y": 100}, l2)
            try:
                res = check(f2, l2, lambda: probe(3, t1, *cursor), watch_fn)
                got = not res["reasons"] and res.get("mode") == PHANTOM_MODE and res["phantom"]["check_window"]["samples"] == 30
            except UserReturned:
                got = False
            expect("phantom " + name, got == want_pass)

        try:
            PHANTOM_STATE, PHANTOM_ENABLED = st, True
            write_state(t1 - 5000)
            ph_check("유령 입력+정지 통과", True, watch())
            ph_check("짧은 창 키 1개 복귀", False, watch(keys=[[t1 + 100, [65]]]))
            ph_check("짧은 창 커서 1px 복귀", False, watch(cursor=[[t1, 100, 100], [t1 + 100, 101, 100]]))
            ph_check("짧은 창 전경 변경 복귀", False, watch(fg=[[t1, 111, 999], [t1 + 100, 222, 777]]))
            ph_check("커서 이탈은 보조 판정으로 안 덮음", False, watch(), cursor=(300, 300))
            write_state(t1 - PHANTOM_FRESH_MS - 1)
            ph_check("데몬 근거 오래됨 복귀", False, watch())
            write_state(t1 - 5000, mode="idle", state="present")
            ph_check("데몬 present 복귀", False, watch())
            write_state(t1 - 5000)
            PHANTOM_ENABLED = False
            ph_check("끄면 옛 판정(복귀)", False, watch())
            PHANTOM_ENABLED, PHANTOM_STATE = True, None
            ph_check("상태 파일 모름 복귀", False, watch())
        finally:
            PHANTOM_STATE, PHANTOM_ENABLED = saved_state, saved_enabled
        _clock_offset_ms = None
        real_flag, real_log = os.path.join(d, "r_flag"), os.path.join(d, "r_log.jsonl")

        def broken():
            raise GuardUnavailable("읽기 실패")

        expect("판단 불가는 멈추되 플래그 없음", raises(GuardUnavailable, lambda: check(real_flag, real_log, broken)) and not user_returned(real_flag))

    try:
        real = read_probe()
        expect("실제 읽기 값 형식", real["idle_ms"] >= 0 and real["win_now_ms"] > 0 and isinstance(real["cursor_x"], int))
        print("실측 idle_ms=%d WSL-Windows 시계차=%dms" % (real["idle_ms"], _clock_offset_ms))
    except GuardUnavailable as e:
        expect("실제 읽기 값 형식: %s" % e, False)

    # 전경이 아닌 PID 와 금지 PID 는 입력 전에 거부돼야 한다. 둘 다 전송 없이 끝나는 경로만 쓴다
    with tempfile.TemporaryDirectory(prefix="pd_away_ps_", dir=HERE) as d:
        win_log = subprocess.check_output(["wslpath", "-w", os.path.join(d, "inj.jsonl")], text=True).strip()
        win_flag = subprocess.check_output(["wslpath", "-w", os.path.join(d, "none.flag")], text=True).strip()
        common = ["-InjectionLog", win_log, "-FlagPath", win_flag]
        pid = str(os.getpid() + 4000000)  # 존재하지 않는 Windows PID
        forbid = ["-ForbiddenProcessId", str(os.getpid() + 4000001)]  # 전경 판정까지 가도록 다른 없는 PID 를 금지로 준다
        expect("safe_key 전경 불일치 거부", _ps_rc("safe_key.ps1", "-Key", "F13", "-ExpectedProcessId", pid, *forbid, *common) != 0)
        expect("safe_key 금지 PID 거부", _ps_rc("safe_key.ps1", "-Key", "F13", "-ExpectedProcessId", pid, "-ForbiddenProcessId", pid, *common) != 0)
        expect("safe_key Alt+Tab 거부", _ps_rc("safe_key.ps1", "-Key", "Alt+Tab", "-ExpectedProcessId", pid, *forbid, *common) != 0)
        expect("safe_touch 전경 불일치 거부", _ps_rc("safe_touch.ps1", "-ScreenX", "-1500", "-ScreenY", "500", "-ExpectedProcessId", pid, *forbid, *common) != 0)
        for script, base in (("safe_key.ps1", ["-Key", "F13"]), ("safe_touch.ps1", ["-ScreenX", "-1500", "-ScreenY", "500"])):
            miss = _ps_run(script, *base, "-ExpectedProcessId", pid, *common)
            expect(script + " 금지 PID 누락 거부", miss.returncode != 0 and "MissingMandatoryParameter" in miss.stdout + miss.stderr)  # 콘솔 폭에서 줄바꿈되는 매개변수 이름 대신 오류 ID 를 본다
            zero = _ps_run(script, *base, "-ExpectedProcessId", pid, "-ForbiddenProcessId", "0", *common)
            expect(script + " 금지 PID 0 거부", zero.returncode != 0 and "ForbiddenProcessId" in zero.stdout + zero.stderr)
        expect("거부된 호출은 주입 기록 없음", not os.path.exists(os.path.join(d, "inj.jsonl")))
        with open(os.path.join(d, "on.flag"), "w", encoding="utf-8"):
            pass
        on_flag = subprocess.check_output(["wslpath", "-w", os.path.join(d, "on.flag")], text=True).strip()
        expect("safe_key 복귀 플래그 거부", _ps_rc("safe_key.ps1", "-Key", "F13", "-ExpectedProcessId", pid, *forbid, "-InjectionLog", win_log, "-FlagPath", on_flag) != 0)

    print("self-test %s, 실패 %d건" % ("PASS" if not failures else "FAIL", len(failures)))
    return 0 if not failures else 1


def main():
    parser = argparse.ArgumentParser(description="PC 비움 시험 사용자 복귀 감지")
    parser.add_argument("--self-test", action="store_true")
    sub = parser.add_subparsers(dest="cmd")
    sub.add_parser("check")
    rec = sub.add_parser("record")
    rec.add_argument("kind")
    rec.add_argument("x", type=int, nargs="?")
    rec.add_argument("y", type=int, nargs="?")
    armer = sub.add_parser("arm")
    armer.add_argument("--user-instruction", required=True)
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    try:
        if args.cmd == "check":
            print(json.dumps(check(), ensure_ascii=False))
        elif args.cmd == "record":
            record_injection(args.kind, args.x, args.y)
        elif args.cmd == "arm":
            print(json.dumps(arm(args.user_instruction), ensure_ascii=False))
        else:
            parser.print_help()
            return RC_UNAVAILABLE
    except UserReturned as e:
        print(str(e), file=sys.stderr)
        return RC_RETURNED
    except GuardUnavailable as e:
        print(str(e), file=sys.stderr)
        return RC_UNAVAILABLE
    return RC_AWAY


if __name__ == "__main__":
    sys.exit(main())
