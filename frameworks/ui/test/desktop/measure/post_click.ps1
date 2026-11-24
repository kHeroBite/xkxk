# 지정 PID 주 창의 클라이언트 좌표에 마우스 메시지(이동·누름·뗌)를 PostMessage 로 보내는 스크립트 — 활성화·커서 이동·실입력 없음(otest_winforms_bg L2)
param(
  [Parameter(Mandatory = $true)][int]$TargetPid,
  [Parameter(Mandatory = $true)][int]$X,
  [Parameter(Mandatory = $true)][int]$Y
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PostClick {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
$p = Get-Process -Id $TargetPid -ErrorAction Stop
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "주 창 없음: pid=$TargetPid" }
$lp = [IntPtr](($Y -shl 16) -bor ($X -band 0xFFFF))
$a = [PostClick]::PostMessage($h, 0x0200, [IntPtr]::Zero, $lp)   # WM_MOUSEMOVE
Start-Sleep -Milliseconds 60
$b = [PostClick]::PostMessage($h, 0x0201, [IntPtr]1, $lp)        # WM_LBUTTONDOWN(MK_LBUTTON)
Start-Sleep -Milliseconds 60
$c = [PostClick]::PostMessage($h, 0x0202, [IntPtr]::Zero, $lp)   # WM_LBUTTONUP
"posted hwnd=$h x=$X y=$Y move=$a down=$b up=$c"
