# Windows 표준 창(메모장)을 화면 밖에서 PrintWindow 로 활성·비활성 각각 캡처하고 모서리·캡션 버튼 영역을 4배 확대해 저장한 뒤 창을 닫는 스크립트
param([string]$OutDir, [string]$ClassName = "Notepad", [int]$Wait = 15, [string]$Exclude = "", [switch]$List)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class W32 {
  public static System.Collections.Generic.List<long> All() { var l = new System.Collections.Generic.List<long>(); EnumWindows((h, x) => { l.Add(h.ToInt64()); return true; }, IntPtr.Zero); return l; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int sz);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
function IsProc($h) { $pid2 = [uint32]0; [void][W32]::GetWindowThreadProcessId($h, [ref]$pid2); $p = Get-Process -Id $pid2 -ErrorAction SilentlyContinue; return ($p -and $p.ProcessName -eq $ClassName) }
$ex = @($Exclude -split "," | Where-Object { $_ -ne "" })
function ProcHwnds() { foreach ($x in [W32]::All()) { $h = [IntPtr]$x; if (IsProc $h) { $h } } }
if ($List) {
  foreach ($h in ProcHwnds) { $t = New-Object System.Text.StringBuilder 256; [void][W32]::GetClassName($h, $t, 256); Write-Output ("HWND={0} class={1} vis={2}" -f $h.ToInt64(), $t, [W32]::IsWindowVisible($h)) }
  exit 0
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$script:found = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds($Wait)
while ($script:found -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
  foreach ($h in ProcHwnds) { if ([W32]::IsWindowVisible($h) -and ($ex -notcontains [string]$h.ToInt64())) { $script:found = $h; break } }
  if ($script:found -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500 }
}
if ($script:found -eq [IntPtr]::Zero) { Write-Output "NOT_FOUND class=$ClassName"; exit 2 }
$h = $script:found
$fg0 = [W32]::GetForegroundWindow()
# 화면 밖 + 비활성 이동(SWP_NOACTIVATE=0x10, NOZORDER=0x4)
[void][W32]::SetWindowPos($h, [IntPtr]::Zero, -5000, 100, 900, 560, 0x14)
Start-Sleep -Milliseconds 800
$r = New-Object W32+RECT; [void][W32]::GetWindowRect($h, [ref]$r)
$ext = New-Object W32+RECT; [void][W32]::DwmGetWindowAttribute($h, 9, [ref]$ext, 16)
$w = $r.R - $r.L; $ht = $r.B - $r.T
Write-Output ("RECT win=({0},{1},{2},{3}) frame=({4},{5},{6},{7}) size={8}x{9}" -f $r.L, $r.T, $r.R, $r.B, $ext.L, $ext.T, $ext.R, $ext.B, $w, $ht)
function Snap($name, $active) {
  [void][W32]::SendMessage($h, 0x86, [IntPtr]$active, [IntPtr]::Zero)   # WM_NCACTIVATE
  Start-Sleep -Milliseconds 500
  $bmp = New-Object System.Drawing.Bitmap $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  $ok = [W32]::PrintWindow($h, $dc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  # 4배 확대 크롭: 좌상 모서리·우상(캡션 버튼)·좌하 모서리·우하 모서리, 각 120x60
  $crops = @{ "topleft" = @(0, 0, 120, 60); "topright_caption" = @(($w - 200), 0, 200, 60); "bottomleft" = @(0, ($ht - 60), 120, 60); "bottomright" = @(($w - 120), ($ht - 60), 120, 60) }
  foreach ($k in $crops.Keys) {
    $c = $crops[$k]; $rect = New-Object System.Drawing.Rectangle $c[0], $c[1], $c[2], $c[3]
    $dst = New-Object System.Drawing.Bitmap ($c[2] * 4), ($c[3] * 4)
    $gg = [System.Drawing.Graphics]::FromImage($dst); $gg.InterpolationMode = 'NearestNeighbor'; $gg.PixelOffsetMode = 'Half'
    $gg.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, ($c[2] * 4), ($c[3] * 4)), $rect, 'Pixel'); $gg.Dispose()
    $dst.Save((Join-Path $OutDir ("{0}_{1}_x4.png" -f $name, $k)), [System.Drawing.Imaging.ImageFormat]::Png); $dst.Dispose()
  }
  $bmp.Dispose()
  Write-Output ("SNAP {0} active={1} printwindow={2}" -f $name, $active, $ok)
}
Snap "notepad_inactive" 0
Snap "notepad_active" 1
$fg1 = [W32]::GetForegroundWindow()
Write-Output ("FOREGROUND_CHANGED={0}" -f ($fg0 -ne $fg1))
[void][W32]::SendMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
Start-Sleep -Seconds 1
Write-Output "CLOSED"
