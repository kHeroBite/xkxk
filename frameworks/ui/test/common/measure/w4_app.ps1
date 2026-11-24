# W4 앱 비교 실측기 — 대상 창을 전경으로 올리고 SendInput 실클릭·끌기 뒤 화면 영역 캡처 변화 시각을 기록한다(클릭→마지막 변화=안정, 끌기 중 변화 간격=프레임)
param(
  [string]$Mode,            # shot | open | drag | move
  [long]$Hwnd = 0,
  [int]$X = 0, [int]$Y = 0,           # 클릭·끌기 시작(화면 좌표)
  [int]$CloseX = 0, [int]$CloseY = 0, # 열기 뒤 닫기 클릭(0 이면 안 함)
  [int]$RX = 0, [int]$RY = 0, [int]$RW = 0, [int]$RH = 0, # 캡처 영역(화면 좌표)
  [int]$Reps = 1, [int]$Horizon = 2400, [int]$Dx = 0, [int]$Steps = 60, [int]$Interval = 16,
  [int]$WX = 0, [int]$WY = 0, [int]$WW = 0, [int]$WH = 0,
  [string]$Out = ""
)
$ErrorActionPreference = 'Stop'
# 끌기 입력 주기: Thread.Sleep 은 Windows 타이머 해상도(15.6ms)로 늘어나 8ms 가 15~20ms 가 됐다(12:02 실측) → C# Drag 는 절대 시각 일정 + SpinWait 로 맞춘다.
# SendInput INPUT 구조체는 x64 에서 40바이트다. 여분 필드로 48바이트가 되면 SendInput 이 0 을 돌려 입력이 하나도 안 들어간다(11:44 실측: 커서 불변 → 가드 자체 오판)
# C# 본문에는 한국어 주석을 두지 않는다(PowerShell 5.1 이 BOM 없는 UTF-8 을 ANSI 로 읽어 주석 줄이 깨지며 다음 줄을 삼킨다, 12:04 실측)
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.CompilerOptions = '/unsafe'
[void]$cp.ReferencedAssemblies.Add('System.Drawing.dll')
[void]$cp.ReferencedAssemblies.Add('System.dll')
Add-Type -CompilerParameters $cp -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Drawing; using System.Drawing.Imaging; using System.Diagnostics; using System.Collections.Generic; using System.Threading;
public static class W4 {
  [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy, data, flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] struct INP { public int type; public MI mi; }  // x64 INPUT = 40 bytes (see header note)
  public static int InputSize() { return Marshal.SizeOf(typeof(INP)); }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, INP[] i, int sz);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
  public static void Fg(IntPtr h) {
    uint p; uint t = GetWindowThreadProcessId(GetForegroundWindow(), out p); uint me = GetCurrentThreadId();
    AttachThreadInput(me, t, true); SetForegroundWindow(h); AttachThreadInput(me, t, false);
  }
  static void Mouse(int x, int y, int flags) {
    int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    var i = new INP(); i.type = 0; i.mi.dx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1)); i.mi.dy = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
    i.mi.flags = flags | 0x0001 | 0x8000 | 0x4000;
    if (SendInput(1, new INP[] { i }, Marshal.SizeOf(typeof(INP))) != 1) throw new Exception("SendInput 실패 err=" + Marshal.GetLastWin32Error());
  }
  public static void Move(int x, int y) { Mouse(x, y, 0); }
  public static void Down(int x, int y) { Mouse(x, y, 0x0002); }
  public static void Up(int x, int y) { Mouse(x, y, 0x0004); }
  static long Hash(Bitmap b, Graphics g, Rectangle r) {
    g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
    var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
    long h = 1469598103934665603; int stride = d.Stride; 
    unsafe { byte* p = (byte*)d.Scan0; for (int yy = 0; yy < b.Height; yy += 3) { int* row = (int*)(p + yy * stride); for (int xx = 0; xx < b.Width; xx += 3) { h ^= row[xx]; h *= 1099511628211; } } }
    b.UnlockBits(d); return h;
  }
  // 누름 시각 기준 화면 변화 시각 목록(ms)과 표본 주기
  public static string Open(int x, int y, Rectangle r, int horizon) {
    using (var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb)) using (var g = Graphics.FromImage(b)) {
      Move(x, y); Thread.Sleep(400); long h0 = Hash(b, g, r);
      var sw = Stopwatch.StartNew(); Down(x, y); Thread.Sleep(30); Up(x, y);
      var ch = new List<double>(); int n = 0; long prev = h0;
      while (sw.ElapsedMilliseconds < horizon) { long h = Hash(b, g, r); n++; if (h != prev) { ch.Add(Math.Round(sw.Elapsed.TotalMilliseconds, 1)); prev = h; } }
      return "{\"changes\":[" + string.Join(",", ch) + "],\"samples\":" + n + ",\"sample_ms\":" + Math.Round((double)horizon / Math.Max(1, n), 2) + "}";
    }
  }
  public static string Drag(int x, int y, int dx, int steps, int interval, Rectangle r) {
    using (var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb)) using (var g = Graphics.FromImage(b)) {
      Move(x, y); Thread.Sleep(300); long prev = Hash(b, g, r);
      var ch = new List<double>(); int n = 0; bool done = false; var sw = Stopwatch.StartNew(); double tEnd = 0;
      var th = new Thread(() => { Down(x, y); double t0 = sw.Elapsed.TotalMilliseconds; for (int i = 1; i <= steps; i++) { double due = t0 + interval * i; while (sw.Elapsed.TotalMilliseconds < due) Thread.SpinWait(20); Move(x + dx * i / steps, y); } Up(x + dx, y); tEnd = sw.Elapsed.TotalMilliseconds; done = true; });
      th.Priority = ThreadPriority.Highest;
      th.Start();
      while (!done || sw.Elapsed.TotalMilliseconds < tEnd + 50) { long h = Hash(b, g, r); n++; if (h != prev) { ch.Add(Math.Round(sw.Elapsed.TotalMilliseconds, 1)); prev = h; } }
      th.Join();
      return "{\"changes\":[" + string.Join(",", ch) + "],\"samples\":" + n + ",\"sample_ms\":" + Math.Round(tEnd / Math.Max(1, n), 2) + ",\"drag_ms\":" + Math.Round(tEnd, 1) + "}";
    }
  }
  public static void Shot(Rectangle r, string path) {
    using (var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb)) using (var g = Graphics.FromImage(b)) { g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size); b.Save(path, ImageFormat.Png); }
  }
}
'@
try { [void][W4]::SetProcessDpiAwareness(2) } catch { }
$r = New-Object System.Drawing.Rectangle $RX, $RY, $RW, $RH
$h = [IntPtr]$Hwnd
$res = @()
switch ($Mode) {
  'move' { [void][W4]::ShowWindow($h, 9); [void][W4]::MoveWindow($h, $WX, $WY, $WW, $WH, $true); [W4]::Fg($h); $res += '{"ok":true}' }
  'shot' { [W4]::Fg($h); Start-Sleep -Milliseconds 300; [W4]::Shot($r, $Out); $res += '{"ok":true}' }
  'open' {
    for ($i = 0; $i -lt $Reps; $i++) {
      [W4]::Fg($h); Start-Sleep -Milliseconds 200
      $res += [W4]::Open($X, $Y, $r, $Horizon)
      # 왼쪽 모니터 좌표는 음수다(11:57 -gt 0 조건으로 닫기 누락 → 자체 오판 정지)
      if ($CloseX -ne 0 -or $CloseY -ne 0) { Start-Sleep -Milliseconds 200; [W4]::Move($CloseX, $CloseY); Start-Sleep -Milliseconds 150; [W4]::Down($CloseX, $CloseY); Start-Sleep -Milliseconds 30; [W4]::Up($CloseX, $CloseY); Start-Sleep -Milliseconds 900 }
    }
  }
  'drag' {
    $cx = $X
    for ($i = 0; $i -lt $Reps; $i++) {
      [W4]::Fg($h); Start-Sleep -Milliseconds 200
      $d = if ($i % 2 -eq 0) { $Dx } else { -$Dx }
      $res += [W4]::Drag($cx, $Y, $d, $Steps, $Interval, $r); $cx += $d; Start-Sleep -Milliseconds 600
    }
  }
}
Write-Output ('{"results":[' + ($res -join ',') + ']}')
