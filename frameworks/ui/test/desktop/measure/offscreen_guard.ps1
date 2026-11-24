# 시험 앱 PID 의 보이는 최상위 창이 모니터와 겹치거나 전경이 되는지 감시하는 스크립트 — 겹치면 그 창만 비활성 이동으로 화면 밖에 되돌리고 줄을 출력한다(실입력·활성화 없음)
param(
  [Parameter(Mandatory = $true)][int]$TargetPid,
  [Parameter(Mandatory = $true)][string]$StopFile,
  [int]$IntervalMs = 100,
  [int]$MaxSeconds = 1800,
  [int]$Margin = 2000
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class OffGuard {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  public static List<IntPtr> ForPid(uint pid) {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
  public static uint ForegroundPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
}
"@
$SwpMove = 0x0001 -bor 0x0004 -bor 0x0010   # NOSIZE | NOZORDER | NOACTIVATE
$first = [System.Windows.Forms.SystemInformation]::VirtualScreen
[Console]::Out.WriteLine("READY pid=$TargetPid virtual=$($first.Left),$($first.Top),$($first.Right),$($first.Bottom)")
[Console]::Out.Flush()
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$samples = 0; $maxGap = 0; $last = 0; $breaches = 0
while ($clock.Elapsed.TotalSeconds -lt $MaxSeconds -and -not (Test-Path -LiteralPath $StopFile)) {
  if (-not (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue)) { [Console]::Out.WriteLine("EXITED pid=$TargetPid"); break }
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  foreach ($h in [OffGuard]::ForPid([uint32]$TargetPid)) {
    $r = New-Object OffGuard+RECT
    if (-not [OffGuard]::GetWindowRect($h, [ref]$r)) { continue }
    if ($r.R -le $r.L -or $r.B -le $r.T) { continue }
    if ($r.R -gt $vs.Left -and $r.L -lt $vs.Right -and $r.B -gt $vs.Top -and $r.T -lt $vs.Bottom) {
      $breaches++
      $ok = [OffGuard]::SetWindowPos($h, [IntPtr]::Zero, $vs.Left, $vs.Bottom + $Margin, 0, 0, [uint32]$SwpMove)
      [Console]::Out.WriteLine("OVERLAP t=$($clock.ElapsedMilliseconds) hwnd=$h rect=$($r.L),$($r.T),$($r.R),$($r.B) movedBack=$ok")
      [Console]::Out.Flush()
    }
  }
  if ([OffGuard]::ForegroundPid() -eq [uint32]$TargetPid) {
    $breaches++
    [Console]::Out.WriteLine("FOREGROUND t=$($clock.ElapsedMilliseconds) pid=$TargetPid")
    [Console]::Out.Flush()
  }
  $samples++
  $now = $clock.ElapsedMilliseconds
  if ($now - $last -gt $maxGap) { $maxGap = $now - $last }
  $last = $now
  Start-Sleep -Milliseconds $IntervalMs
}
[Console]::Out.WriteLine("DONE samples=$samples maxGapMs=$maxGap breaches=$breaches")
[Console]::Out.Flush()
