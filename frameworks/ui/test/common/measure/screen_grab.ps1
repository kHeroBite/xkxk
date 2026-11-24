# xkxk.frameworks.ui.desktop 최상위 창들을 화면 캡처(CopyFromScreen, 실제 화면 합성 결과)로 저장하고 평균 밝기를 남기는 스크립트 — 캡처 방지(⑬) 검증용, 실입력 없음
param([string]$OutDir, [string]$Tag)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class W32G {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr h, out uint a);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static List<IntPtr> ForPid(uint pid) {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
}
"@
$proc = Get-Process -Name xkxk.frameworks.ui.desktop -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { "no process"; exit 1 }
$i = 0
foreach ($h in [W32G]::ForPid([uint32]$proc.Id)) {
  $r = New-Object W32G+RECT; [void][W32G]::GetWindowRect($h, [ref]$r)
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  if ($w -lt 50 -or $hh -lt 50) { continue }
  $sb = New-Object System.Text.StringBuilder 256; [void][W32G]::GetWindowText($h, $sb, 256)
  $aff = 0; [void][W32G]::GetWindowDisplayAffinity($h, [ref]$aff)
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
  $sum = 0; $n = 0
  for ($y = 0; $y -lt $hh; $y += 8) { for ($x = 0; $x -lt $w; $x += 8) { $c = $bmp.GetPixel($x, $y); $sum += ($c.R + $c.G + $c.B) / 3; $n++ } }
  $file = Join-Path $OutDir ("grab_{0}_{1}.png" -f $Tag, $i)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  "hwnd=$h title_len=$($sb.Length) rect=($($r.L),$($r.T))-($($r.R),$($r.B)) affinity=0x{0:X} mean_luma={1:N1} file={2}" -f $aff, ($sum / [math]::Max(1,$n)), $file
  $g.Dispose(); $bmp.Dispose(); $i++
}
