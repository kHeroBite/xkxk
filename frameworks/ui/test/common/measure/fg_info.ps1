# 전경 창(hwnd·pid·제목)과 커서 위치를 읽어 출력하고, -Restore 지정 시 해당 hwnd 를 전경으로 되돌리는 스크립트
param([long]$Restore = 0)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public class FG {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@
if ($Restore -ne 0) {
  $h = [IntPtr]$Restore; $p = 0
  $cur = [FG]::GetForegroundWindow(); $t1 = [FG]::GetWindowThreadProcessId($cur, [ref]$p); $t2 = [FG]::GetCurrentThreadId()
  [void][FG]::AttachThreadInput($t2, $t1, $true); $ok = [FG]::SetForegroundWindow($h); [void][FG]::AttachThreadInput($t2, $t1, $false)
  "restore ok=$ok"
}
$h = [FG]::GetForegroundWindow(); $p = 0; [void][FG]::GetWindowThreadProcessId($h, [ref]$p)
$sb = New-Object System.Text.StringBuilder 256; [void][FG]::GetWindowText($h, $sb, 256)
$c = [System.Windows.Forms.Cursor]::Position
"fg hwnd=$([int64]$h) pid=$p title='$($sb.ToString())' cursor=($($c.X),$($c.Y))"
