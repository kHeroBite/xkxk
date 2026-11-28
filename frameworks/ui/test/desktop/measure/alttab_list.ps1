# Alt+Tab 후보 창을 문서화된 규칙(가시·비cloaked·소유자 없음 또는 APPWINDOW·TOOLWINDOW/NOACTIVATE 제외)으로 열거해 Pd SDI 창 포함·PdSdiShadow 제외 여부를 출력 — 입력 0, 읽기 전용. 규칙 평가(비직접 증거)이며 Alt+Tab UI 직접 관찰이 아니다
param(
    [string]$ProcName = $(try { $hiv = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\..\tools\host_identity.json') -Raw -Encoding UTF8 | ConvertFrom-Json).'desktop.process_name' } catch { $hiv = $null }; if ($hiv) { $hiv } else { throw 'host_identity.json 을 읽지 못했거나 desktop.process_name 키가 없다' }),     # Pd 프로세스 이름(.exe 제외)
    [string]$ShadowPattern = "PdSdiShadow",   # 그림자 창 식별(클래스명 또는 제목에 포함)
    [switch]$All,                             # 후보 전체를 출력(기본: 요약 + Pd 관련 창만)
    [switch]$ParseOnly
)
if ($ParseOnly) { "PARSE_OK"; exit 0 }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class AT {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
  static long Long(IntPtr h, int i) { return IntPtr.Size == 8 ? GetWindowLongPtr64(h, i).ToInt64() : GetWindowLong32(h, i); }
  // 각 행: hwnd, class, pid, title, visible, exstyle(hex), owner, cloaked, style(hex)
  public static List<string[]> Snap() {
    var r = new List<string[]>();
    EnumWindows((h, l) => {
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      var t = new StringBuilder(512); GetWindowText(h, t, 512);
      uint pid; GetWindowThreadProcessId(h, out pid);
      int cloaked = 0; int hr = DwmGetWindowAttribute(h, 14, out cloaked, 4); if (hr != 0) cloaked = 0; // DWMWA_CLOAKED
      r.Add(new string[]{ h.ToInt64().ToString(), c.ToString(), pid.ToString(), t.ToString(),
        IsWindowVisible(h) ? "1" : "0", Long(h, -20).ToString("X"), GetWindow(h, 4).ToInt64().ToString(), cloaked.ToString(), Long(h, -16).ToString("X") });
      return true;
    }, IntPtr.Zero);
    return r;
  }
}
"@

$WS_EX_TOOLWINDOW = 0x80L; $WS_EX_APPWINDOW = 0x40000L; $WS_EX_NOACTIVATE = 0x8000000L

function Eval-Rule($w) { # 후보 여부와 제외 사유(문서화된 Alt+Tab 규칙의 근사)
    $ex = [Convert]::ToInt64($w[5], 16)
    $app = (($ex -band $WS_EX_APPWINDOW) -ne 0)
    $reasons = @()
    if ($w[4] -ne "1") { $reasons += "비가시" }
    if ([int]$w[7] -ne 0) { $reasons += "DWM cloaked=" + $w[7] }
    if (($ex -band $WS_EX_TOOLWINDOW) -ne 0 -and -not $app) { $reasons += "WS_EX_TOOLWINDOW(APPWINDOW 아님)" }
    if (($ex -band $WS_EX_NOACTIVATE) -ne 0 -and -not $app) { $reasons += "WS_EX_NOACTIVATE(APPWINDOW 아님)" }
    if ([long]$w[6] -ne 0 -and -not $app) { $reasons += "소유자 있음(APPWINDOW 아님)" }
    if ($w[3] -eq "" -and -not $app) { $reasons += "제목 없음" }
    return [pscustomobject]@{ candidate = ($reasons.Count -eq 0); reasons = $reasons; appwindow = $app }
}

$pids = @(Get-Process -Name $ProcName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
$rows = [AT]::Snap()
$cands = @(); $pd = @(); $shadow = @()
foreach ($w in $rows) {
    $e = Eval-Rule $w
    $item = [ordered]@{ hwnd = [long]$w[0]; class = $w[1]; pid = [int]$w[2]; title = $w[3]; exstyle = $w[5]; owner = [long]$w[6]; cloaked = [int]$w[7]; candidate = $e.candidate; reasons = $e.reasons }
    $isShadow = ($w[1] -like "*$ShadowPattern*") -or ($w[3] -like "*$ShadowPattern*")
    $isPd = (($pids -contains [int]$w[2]) -and -not $isShadow)
    if ($e.candidate) { $cands += [pscustomobject]$item }
    if ($isShadow) { $shadow += [pscustomobject]$item }
    elseif ($isPd) { $pd += [pscustomobject]$item }
}

$pdIn = @($pd | Where-Object { $_.candidate })
$shIn = @($shadow | Where-Object { $_.candidate })
$out = [ordered]@{
    method = "rule-eval(비직접) — EnumWindows+DwmGetWindowAttribute+WS_EX 규칙 평가. Alt+Tab UI 를 직접 관찰한 것이 아니다. 키 입력·마우스 입력 0"
    rule = "가시 AND 비cloaked AND (소유자 없음 OR WS_EX_APPWINDOW) AND NOT(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE 이면서 APPWINDOW 아님) AND 제목 있음(또는 APPWINDOW)"
    proc_name = $ProcName; pd_pids = $pids; shadow_pattern = $ShadowPattern
    candidate_total = $cands.Count
    pd_windows_total = $pd.Count; pd_in_alttab = $pdIn.Count
    shadow_windows_total = $shadow.Count; shadow_in_alttab = $shIn.Count
    pd_windows = $pd; shadow_windows = $shadow
    verdict_hint = if ($pd.Count -eq 0) { "Pd 창 없음 — 판정 불가" } elseif ($shadow.Count -eq 0) { "PdSdiShadow 창 미발견(그림자 창이 떠 있지 않음) — 제외 여부 판정 불가" } elseif ($pdIn.Count -ge 1 -and $shIn.Count -eq 0) { "규칙상 Pd 창 포함·PdSdiShadow 제외" } else { "규칙상 불일치(Pd 포함 " + $pdIn.Count + ", Shadow 포함 " + $shIn.Count + ")" }
}
if ($All) { $out.candidates = $cands }
$out | ConvertTo-Json -Depth 6
