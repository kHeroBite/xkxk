#!/usr/bin/env python3
# A01 새 판 — 시험 앱이 기동 직후 전경이 되는 것을 허용·기록하고 측정 뒤 전경 창과 커서를 원래대로 복원하는 retest_maximize_monitor 래퍼
"""
사유(11-09): 원본 retest_maximize_monitor 는 조작마다 자기 앱이 전경이면 rc=4 로 중단한다. 11-09 08:28 과 09:35 두 번 모두 기동 직후
첫 조회에서 전경이 자기 앱(hadNoActivate=True 인데도)이라 최대화 측정에 못 들어갔다. 사용자가 PC 를 비운 측정에서만 전경을 허용하고
(Session.allow_foreground=True) 허용 횟수를 기록하며, 측정 앞뒤로 전경 창 hwnd·커서를 읽어 끝나면 복원한다. 원본은 수정하지 않는다.
사용법은 원본과 같다(--conf --out-dir --onscreen-approved ...). 결과 <out-dir>/maximize.json 옆에 maximize_fg.json 을 남긴다.
측정 앞에 user_away_guard.check() 가 복귀를 감지하면 시작하지 않는다(rc=5).
"""
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_maximize_monitor as m  # noqa: E402

PS = r'''
param([string]$Action, [long]$Hwnd = 0, [int]$X = 0, [int]$Y = 0)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class FgCur {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@
if ($Action -eq "read") {
  $h = [FgCur]::GetForegroundWindow(); $p = New-Object FgCur+POINT; [FgCur]::GetCursorPos([ref]$p) | Out-Null
  $pid2 = 0; [FgCur]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
  "{0}|{1}|{2}|{3}" -f $h.ToInt64(), $pid2, $p.X, $p.Y
} elseif ($Action -eq "restore") {
  $ok = [FgCur]::SetForegroundWindow([IntPtr]$Hwnd); [FgCur]::SetCursorPos($X, $Y) | Out-Null
  "restored|$ok"
}
'''


def run_ps(args):
    """-Action restore 는 SetCursorPos 로 커서를 옮기므로 user_away_guard 주입 기록(직전 시각·직후 좌표)으로 감싼다(D-9).
    기록이 없으면 다음 도구의 가드가 '마지막 주입 위치에서 커서 이탈'로 거짓 복귀를 세운다(2026-11-25 실측: 복원 (370,698), 마지막 주입 (989,327))."""
    if len(args) > 1 and args[0] == "-Action" and args[1] == "restore" and "-X" in args and "-Y" in args:
        import user_away_guard as guard
        x, y = int(args[args.index("-X") + 1]), int(args[args.index("-Y") + 1])
        try:
            with guard.injection("cursor_restore", x, y):
                return _run_ps(args)
        except guard.GuardUnavailable as e:  # 기록을 못 남겨도 사용자 화면 복원은 한다(그 뒤 가드는 안전 쪽으로 복귀 판단할 수 있다)
            print("주입 기록 불가 — 기록 없이 복원한다:", e, file=sys.stderr)
            return _run_ps(args)
    return _run_ps(args)


def _run_ps(args):
    with tempfile.NamedTemporaryFile("w", suffix=".ps1", delete=False, encoding="utf-8") as f:
        f.write(PS)
        path = f.name
    try:
        win = subprocess.check_output(["wslpath", "-w", path], text=True).strip()
        return subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", win] + args, capture_output=True, text=True, timeout=60).stdout.strip()
    finally:
        os.unlink(path)


def main():
    import user_away_guard as guard
    try:
        guard.check()
    except (guard.UserReturned, guard.GuardUnavailable) as e:
        print("복귀 감지 또는 가드 불가 — 시작하지 않는다:", e)
        return 5
    before = run_ps(["-Action", "read"])
    print("전경·커서 저장:", before)
    sessions = []
    orig_init = m.Session.__init__

    def init(self, *a, **k):
        orig_init(self, *a, **k)
        self.allow_foreground = True
        sessions.append(self)

    m.Session.__init__ = init
    try:
        code = m.main()
    finally:
        parts = before.split("|")
        after_restore = run_ps(["-Action", "restore", "-Hwnd", parts[0], "-X", parts[2], "-Y", parts[3]]) if len(parts) == 4 else "저장값 없음"
        print("전경·커서 복원:", after_restore)
    out_dir = None
    for i, a in enumerate(sys.argv):
        if a == "--out-dir" and i + 1 < len(sys.argv):
            out_dir = os.path.abspath(sys.argv[i + 1])
    if out_dir:
        hits = [h for s in sessions for h in s.foreground_hits]
        with open(os.path.join(out_dir, "maximize_fg.json"), "w", encoding="utf-8") as f:
            json.dump({"foreground_before": before, "foreground_allowed_hits": len(hits), "first_hits": hits[:10], "restore": after_restore}, f, ensure_ascii=False, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
