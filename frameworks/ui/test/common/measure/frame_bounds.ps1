# 지정 프로세스의 보이는 최상위 창마다 GetWindowRect·DWMWA_EXTENDED_FRAME_BOUNDS(보이는 경계)·클라이언트 영역(화면 좌표)을 출력하는 스크립트(읽기 전용, 실입력 없음)
param([string]$ProcName = "Pd.Poc.Desktop", [int]$ProcId = 0)
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class FB {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static List<IntPtr> ForPid(uint pid) {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
}
"@
$procs = if ($ProcId -gt 0) { Get-Process -Id $ProcId -ErrorAction SilentlyContinue } else { Get-Process -Name $ProcName -ErrorAction SilentlyContinue }
foreach ($proc in $procs) {
  foreach ($h in [FB]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256; [void][FB]::GetWindowText($h, $sb, 256)
    $wr = New-Object FB+RECT; [void][FB]::GetWindowRect($h, [ref]$wr)
    $fr = New-Object FB+RECT; $hr = [FB]::DwmGetWindowAttribute($h, 9, [ref]$fr, 16)
    $cr = New-Object FB+RECT; [void][FB]::GetClientRect($h, [ref]$cr)
    $pt = New-Object FB+POINT; [void][FB]::ClientToScreen($h, [ref]$pt)
    "hwnd=$h title='$($sb.ToString())' window=($($wr.L),$($wr.T))-($($wr.R),$($wr.B)) frame=($($fr.L),$($fr.T))-($($fr.R),$($fr.B)) frameSize=$($fr.R-$fr.L)x$($fr.B-$fr.T) hr=$hr client=($($pt.X),$($pt.Y)) $($cr.R)x$($cr.B)"
  }
}
