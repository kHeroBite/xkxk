# 데스크톱 앱 프로세스의 모든 최상위 창을 PrintWindow 로 비활성 캡처하고 위치·크기를 남기는 스크립트(실입력 없음)
param([string]$OutDir)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class W32 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static List<IntPtr> ForPid(uint pid) {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
}
"@
$procName = $null; try { $procName = (Get-Content (Join-Path $PSScriptRoot '..\..\..\tools\host_identity.json') -Raw -ErrorAction Stop | ConvertFrom-Json).'desktop.process_name' } catch {}
if (-not $procName) { [Console]::Error.WriteLine('ERROR: cannot read desktop.process_name from tools/host_identity.json'); exit 2 }
$p = Get-Process $procName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "NOPROC"; exit 2 }
$fg = [W32]::GetForegroundWindow()
$i = 0
foreach ($h in [W32]::ForPid([uint32]$p.Id)) {
  $r = New-Object W32+RECT; [void][W32]::GetWindowRect($h, [ref]$r)
  $sb = New-Object System.Text.StringBuilder 256; [void][W32]::GetWindowText($h, $sb, 256)
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $line = "hwnd=$h title='$($sb.ToString())' rect=($($r.L),$($r.T))-($($r.R),$($r.B)) size=${w}x${hh} foreground=$($h -eq $fg)"
  if ($w -gt 0 -and $hh -gt 0) {
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [W32]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $f = Join-Path $OutDir ("sdi_win_{0}.png" -f $i)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $line += " printwindow=$ok file=$f"
  }
  $line; $i++
}
