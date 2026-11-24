# L2 창 메시지 드래그 — 대상 창 핸들에 PostMessage 로 WM_LBUTTONDOWN·WM_MOUSEMOVE(MK_LBUTTON)·WM_LBUTTONUP 을 단계적으로 보내는 스크립트(커서·전경·SendInput 사용 안 함)
# 사용: powershell.exe -NoProfile -ExecutionPolicy Bypass -File post_mouse.ps1 -Hwnd <창 핸들> -X1 <시작 클라이언트 x> -Y1 <y> -X2 <끝 x> -Y2 <끝 y> [-Steps 20] [-DelayMs 25] [-Child]
# 좌표는 대상 창 클라이언트 기준(원격 tree.get 의 bounds 와 같은 DIP 이면 DPI 배율 1 에서 동일). -Child 를 주면 시작점의 가장 깊은 자식 창으로 보낸다.
param(
    [Parameter(Mandatory = $true)][long]$Hwnd,
    [Parameter(Mandatory = $true)][int]$X1, [Parameter(Mandatory = $true)][int]$Y1,
    [Parameter(Mandatory = $true)][int]$X2, [Parameter(Mandatory = $true)][int]$Y2,
    [int]$Steps = 20, [int]$DelayMs = 25, [switch]$Child, [switch]$NoUp, [switch]$UpOnly, [int]$HoldMoves = 0
)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class PM {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint f);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static IntPtr L(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
}
"@
$h = [IntPtr]$Hwnd
if (-not [PM]::IsWindow($h)) { "NOWINDOW"; exit 2 }
if ($Child) {
    $p = New-Object PM+POINT; $p.X = $X1; $p.Y = $Y1
    $c = [PM]::ChildWindowFromPointEx($h, $p, 0)
    if ($c -ne [IntPtr]::Zero) { $h = $c }
}
$WM_MOUSEMOVE = 0x0200; $WM_LBUTTONDOWN = 0x0201; $WM_LBUTTONUP = 0x0202; $MK_LBUTTON = 0x0001
if ($UpOnly) { # 끌기 도중 상태를 읽은 뒤 끝점에서 놓기만 한다(-NoUp 짝)
    $ok = [PM]::PostMessage($h, $WM_LBUTTONUP, [IntPtr]0, [PM]::L($X2, $Y2))
    "posted-up hwnd=$([int64]$h) ok=$ok at=($X2,$Y2)"
    exit 0
}
$ok = [PM]::PostMessage($h, $WM_MOUSEMOVE, [IntPtr]0, [PM]::L($X1, $Y1))
Start-Sleep -Milliseconds $DelayMs
$ok = $ok -and [PM]::PostMessage($h, $WM_LBUTTONDOWN, [IntPtr]$MK_LBUTTON, [PM]::L($X1, $Y1))
Start-Sleep -Milliseconds $DelayMs
for ($i = 1; $i -le $Steps; $i++) {
    $x = [int]($X1 + ($X2 - $X1) * $i / $Steps); $y = [int]($Y1 + ($Y2 - $Y1) * $i / $Steps)
    $ok = $ok -and [PM]::PostMessage($h, $WM_MOUSEMOVE, [IntPtr]$MK_LBUTTON, [PM]::L($x, $y))
    Start-Sleep -Milliseconds $DelayMs
}
for ($i = 0; $i -lt $HoldMoves; $i++) { # 끝점에서 더 움직여 Dock 안내가 배치된 뒤 다시 판정하게 한다
    Start-Sleep -Milliseconds 80
    $ok = $ok -and [PM]::PostMessage($h, $WM_MOUSEMOVE, [IntPtr]$MK_LBUTTON, [PM]::L($X2, $Y2))
}
if (-not $NoUp) { $ok = $ok -and [PM]::PostMessage($h, $WM_LBUTTONUP, [IntPtr]0, [PM]::L($X2, $Y2)) }
"posted hwnd=$([int64]$h) ok=$ok from=($X1,$Y1) to=($X2,$Y2) steps=$Steps"
