# xkxk.frameworks.ui.desktop 최상위 창들의 클라이언트 영역만(보이지 않는 크기 조절 테두리·그림자 제외) 화면 캡처하는 스크립트 — otest-13 w13 용, odev4b/screen_grab.ps1 의 새 사본(원본 무수정). 실입력 없음
# 사용: powershell.exe -NoProfile -ExecutionPolicy Bypass -File screen_grab_client.ps1 -OutDir <폴더> -Tag <이름> [-ProcessId <pid>]
# 전경 창이 대상 프로세스가 아니면 아무것도 찍지 않고 'REFUSE_FG' 를 출력한다(다른 앱·터미널이 찍히는 것을 막는다)
param([string]$OutDir, [string]$Tag, [int]$ProcessId = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class W32C {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static List<IntPtr> ForPid(uint pid) {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
}
"@
if ($ProcessId -gt 0) { $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue } else { $proc = Get-Process -Name xkxk.frameworks.ui.desktop -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $proc) { "no process"; exit 1 }
$fgPid = 0; [void][W32C]::GetWindowThreadProcessId([W32C]::GetForegroundWindow(), [ref]$fgPid)
if ($fgPid -ne $proc.Id) { "REFUSE_FG fgPid=$fgPid target=$($proc.Id)"; exit 9 }
$i = 0
foreach ($h in [W32C]::ForPid([uint32]$proc.Id)) {
  $c = New-Object W32C+RECT; [void][W32C]::GetClientRect($h, [ref]$c)
  $p = New-Object W32C+POINT; [void][W32C]::ClientToScreen($h, [ref]$p)
  $w = $c.R - $c.L; $hh = $c.B - $c.T
  if ($w -lt 50 -or $hh -lt 50) { continue }
  $sb = New-Object System.Text.StringBuilder 256; [void][W32C]::GetWindowText($h, $sb, 256)
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($p.X, $p.Y, 0, 0, $bmp.Size)
  $file = Join-Path $OutDir ("cgrab_{0}_{1}.png" -f $Tag, $i)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  "hwnd=$h title_len=$($sb.Length) client=($($p.X),$($p.Y)) ${w}x${hh} file=$file"
  $g.Dispose(); $bmp.Dispose(); $i++
}
