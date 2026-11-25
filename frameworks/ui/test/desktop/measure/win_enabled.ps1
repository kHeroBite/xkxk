# 지정 PID 의 최상위 창마다 OS 활성(IsWindowEnabled)·보임·소유자·제목을 JSON 으로 내는 읽기 전용 탐침(B3 설정 앱 모달) — 입력·창 조작 없음
param(
  [int]$TargetPid = 0,
  [switch]$IncludeHidden,
  [switch]$ParseOnly
)
if ($ParseOnly) { 'PARSE_OK'; exit 0 }
if ($TargetPid -le 0) { [Console]::Error.WriteLine('사용: win_enabled.ps1 -TargetPid <자기 PID> [-IncludeHidden]'); exit 2 }
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnabled {
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static List<IntPtr> Of(uint pid, bool hidden) {
    var list = new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr p) {
      uint w; GetWindowThreadProcessId(h, out w);
      if (w == pid && (hidden || IsWindowVisible(h))) list.Add(h);
      return true;
    }, IntPtr.Zero);
    return list;
  }
  public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
  public static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
}
"@
$rows = @()
foreach ($h in [WinEnabled]::Of([uint32]$TargetPid, [bool]$IncludeHidden)) {
  $rows += [pscustomobject]@{
    hwnd    = [int64]$h
    title   = [WinEnabled]::Text($h)
    class   = [WinEnabled]::Cls($h)
    enabled = [WinEnabled]::IsWindowEnabled($h)
    visible = [WinEnabled]::IsWindowVisible($h)
    owner   = [int64]([WinEnabled]::GetWindow($h, 4))  # 4 = GW_OWNER
  }
}
# 제목이 한글이라 콘솔 코드 페이지를 거치지 않게 UTF-8 바이트를 base64 로 낸다(호출자가 풀어 JSON 으로 읽는다)
$json = ConvertTo-Json -InputObject @($rows) -Compress -Depth 3
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
