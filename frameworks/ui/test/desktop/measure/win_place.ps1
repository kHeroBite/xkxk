# 자기 PID 창의 사각형·최대화·소속 모니터를 읽고(읽기 전용), 비활성으로 옮기거나 투명 처리하는 측정 보조. 입력 API·전경 획득 없음. 다른 PID 창은 거부한다
# 사용: win_place.ps1 -OwnPid <pid> -Ops "list;move:<hwnd>:x:y:w:h;cloak:<hwnd>;bottom:<hwnd>;monrect:x:y:w:h;monitors;control:x:y:w:h"  → 작업마다 JSON 한 줄
param([int]$OwnPid = 0, [string]$Ops = "list")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class WP {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MI { public int cb; public RECT M; public RECT W; public uint F; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string D; }
  [StructLayout(LayoutKind.Sequential)] public struct PL { public uint Len,Flags,Show; public POINT Min,Max; public RECT Normal; }
  public delegate bool EnumW(IntPtr h, IntPtr l);
  public delegate bool EnumM(IntPtr h, IntPtr dc, ref RECT r, IntPtr d);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumW p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumM cb, IntPtr d);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr h, ref MI i);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr h, int t, out uint x, out uint y);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref PL p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetLong(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint k, byte a, uint f);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  public static List<IntPtr> Wins(uint pid) { var l=new List<IntPtr>(); EnumWindows((h,x)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid&&IsWindowVisible(h)) l.Add(h); return true; },IntPtr.Zero); return l; }
  public static List<IntPtr> Mons() { var l=new List<IntPtr>(); EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr h,IntPtr dc,ref RECT r,IntPtr d)=>{ l.Add(h); return true; },IntPtr.Zero); return l; }
}
"@
[void][WP]::SetProcessDpiAwarenessContext([IntPtr](-4))

function RectObj($r) { @{ l = $r.L; t = $r.T; r = $r.R; b = $r.B } }
function MonObj([IntPtr]$h) {
  $i = New-Object WP+MI; $i.cb = [Runtime.InteropServices.Marshal]::SizeOf([type][WP+MI]); [void][WP]::GetMonitorInfo($h, [ref]$i)
  $x = [uint32]0; $y = [uint32]0; [void][WP]::GetDpiForMonitor($h, 0, [ref]$x, [ref]$y)
  @{ device = $i.D; primary = (($i.F -band 1) -eq 1); bounds = RectObj $i.M; work = RectObj $i.W; dpi = $x }
}
function Guard([IntPtr]$h) {
  $p = [uint32]0; [void][WP]::GetWindowThreadProcessId($h, [ref]$p)
  if ($OwnPid -le 0 -or $p -ne [uint32]$OwnPid) { throw "거부: hwnd $h 는 pid $p (자기 pid $OwnPid 아님)" }
}
function Fg() {
  $h = [WP]::GetForegroundWindow(); $p = [uint32]0; [void][WP]::GetWindowThreadProcessId($h, [ref]$p)
  @{ hwnd = [int64]$h; pid = $p; isOwn = ($p -eq [uint32]$OwnPid) }
}
function WinObj([IntPtr]$h) {
  $wr = New-Object WP+RECT; [void][WP]::GetWindowRect($h, [ref]$wr)
  $fr = New-Object WP+RECT; $hasFrame = [WP]::DwmGetWindowAttribute($h, 9, [ref]$fr, 16) -eq 0
  $cr = New-Object WP+RECT; [void][WP]::GetClientRect($h, [ref]$cr)
  $pt = New-Object WP+POINT; [void][WP]::ClientToScreen($h, [ref]$pt)
  $pl = New-Object WP+PL; $pl.Len = [Runtime.InteropServices.Marshal]::SizeOf([type][WP+PL]); [void][WP]::GetWindowPlacement($h, [ref]$pl)
  $cn = New-Object Text.StringBuilder 128; [void][WP]::GetClassName($h, $cn, 128)
  $tx = New-Object Text.StringBuilder 256; [void][WP]::GetWindowText($h, $tx, 256)
  @{ hwnd = [int64]$h; cls = $cn.ToString(); title = $tx.ToString(); rect = RectObj $wr; frame = $(if ($hasFrame) { RectObj $fr } else { $null })
     client = @{ l = $pt.X; t = $pt.Y; r = $pt.X + $cr.R; b = $pt.Y + $cr.B }; zoomed = [WP]::IsZoomed($h); iconic = [WP]::IsIconic($h)
     owner = [int64][WP]::GetWindow($h, 4); monitor = (MonObj ([WP]::MonitorFromWindow($h, 2))).device; dpi = [WP]::GetDpiForWindow($h); normal = RectObj $pl.Normal; show = $pl.Show }
}

foreach ($op in ($Ops -split ';')) {
  $a = $op -split ':'
  switch ($a[0]) {
    'monitors' { @{ op = 'monitors'; monitors = @([WP]::Mons() | ForEach-Object { MonObj $_ }) } | ConvertTo-Json -Depth 6 -Compress }
    'list' { @{ op = 'list'; fg = (Fg); windows = @([WP]::Wins([uint32]$OwnPid) | ForEach-Object { WinObj $_ }) } | ConvertTo-Json -Depth 6 -Compress }
    'monrect' {
      $r = New-Object WP+RECT; $r.L = [int]$a[1]; $r.T = [int]$a[2]; $r.R = [int]$a[1] + [int]$a[3]; $r.B = [int]$a[2] + [int]$a[4]
      @{ op = 'monrect'; rect = RectObj $r; monitor = (MonObj ([WP]::MonitorFromRect([ref]$r, 2))).device } | ConvertTo-Json -Depth 4 -Compress
    }
    'move' {
      $h = [IntPtr][int64]$a[1]; Guard $h
      # SWP_NOACTIVATE|SWP_NOZORDER
      $ok = [WP]::SetWindowPos($h, [IntPtr]::Zero, [int]$a[2], [int]$a[3], [int]$a[4], [int]$a[5], 0x0014)
      @{ op = 'move'; ok = $ok; win = (WinObj $h) } | ConvertTo-Json -Depth 5 -Compress
    }
    'bottom' {
      $h = [IntPtr][int64]$a[1]; Guard $h
      # HWND_BOTTOM, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE
      @{ op = 'bottom'; ok = [WP]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013) } | ConvertTo-Json -Compress
    }
    'cloak' {
      # 측정 창이 사용자 화면을 가리지 않게: 투명·클릭 통과·활성화 안 됨(WS_EX_LAYERED|TRANSPARENT|NOACTIVATE) + 알파 0
      $h = [IntPtr][int64]$a[1]; Guard $h
      $ex = [WP]::GetLong($h, -20).ToInt64() -bor 0x00080000 -bor 0x00000020 -bor 0x08000000
      [void][WP]::SetLong($h, -20, [IntPtr]$ex)
      @{ op = 'cloak'; ok = [WP]::SetLayeredWindowAttributes($h, 0, 0, 2) } | ConvertTo-Json -Compress
    }
    'control' {
      # 대조군: 보이지 않는 표준 Win32 창(자기 PID)을 같은 사각형에 놓고 OS 최대화 → 결과 모니터. Windows 기본 동작 비교용
      $f = New-Object Windows.Forms.Form
      $f.Opacity = 0; $f.ShowInTaskbar = $false; $f.StartPosition = 'Manual'
      $f.SetBounds([int]$a[1], [int]$a[2], [int]$a[3], [int]$a[4])
      $f.Show(); [Windows.Forms.Application]::DoEvents()
      $f.SetBounds([int]$a[1], [int]$a[2], [int]$a[3], [int]$a[4]); [Windows.Forms.Application]::DoEvents()
      $before = New-Object WP+RECT; [void][WP]::GetWindowRect($f.Handle, [ref]$before)
      $f.WindowState = 'Maximized'; [Windows.Forms.Application]::DoEvents()
      $wr = New-Object WP+RECT; [void][WP]::GetWindowRect($f.Handle, [ref]$wr)
      @{ op = 'control'; before = RectObj $before; rect = RectObj $wr; zoomed = [WP]::IsZoomed($f.Handle); monitor = (MonObj ([WP]::MonitorFromWindow($f.Handle, 2))).device } | ConvertTo-Json -Depth 4 -Compress
      $f.Close(); $f.Dispose()
    }
    default { throw "알 수 없는 작업: $op" }
  }
}
