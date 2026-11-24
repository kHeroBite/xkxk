# 지정 PID 의 보이는 최상위 창(메인·SDI·PdSdiShadow)을 EnumWindows Z 순서로 열거해 GW_OWNER·최소화·TopMost·DWM 프레임과 AC-05 판정용 요약을 JSON 으로 낸다 — 입력 0·창 조작 0, 읽기 전용
# 사용: powershell.exe -NoProfile -ExecutionPolicy Bypass -File <win_owner_z.ps1> -Pid <자기 PID> [-MainHwnd <hwnd>] [-ArrayOnly] [-IncludeHidden]
# z 는 EnumWindows 순번(0=맨 위, 클수록 아래)이다. 즉 "A 가 B 위"는 A.z < B.z 이다.
param(
    [Alias('Pid')][int]$TargetPid = 0,          # 대상 프로세스 ID(-Pid 로 지정)
    [string]$ProcName = "",                      # PID 대신 프로세스 이름(.exe 제외)으로 찾을 때
    [long]$MainHwnd = 0,                         # 메인 창 hwnd 직접 지정(미지정이면 요약이 면적 최대 비그림자 창을 메인으로 추정하고 main_source 에 표시)
    [string]$ShadowPattern = "PdSdiShadow",      # 그림자 창 식별(클래스명 또는 제목에 포함)
    [switch]$IncludeHidden,                      # 비가시 창도 포함(기본: 가시·최소화 창만)
    [switch]$ArrayOnly,                          # 창 배열만 출력(요약 생략)
    [switch]$SelfTest,                           # 자기 프로세스의 숨은 큰 창·보이는 작은 창으로 메인 추정을 잰다(F05, 화면 밖 비활성 창만 쓴다)
    [switch]$ParseOnly                           # 문법 확인용: PARSE_OK 출력 후 종료(AC-24)
)
if ($ParseOnly) { "PARSE_OK"; exit 0 }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class WOZ {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT val, int size);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
  static long Long(IntPtr h, int i) { return IntPtr.Size == 8 ? GetWindowLongPtr64(h, i).ToInt64() : GetWindowLong32(h, i); }
  // 각 행(Z 순서, 맨 위부터): z, hwnd, pid, class, title, visible, iconic, zoomed, exstyle(hex), owner, cloaked, rect(L,T,R,B), dwm(L,T,R,B)
  public static List<string[]> Snap() {
    var r = new List<string[]>(); int z = 0;
    EnumWindows((h, l) => {
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      var t = new StringBuilder(512); GetWindowText(h, t, 512);
      uint pid; GetWindowThreadProcessId(h, out pid);
      RECT wr; GetWindowRect(h, out wr);
      RECT fr; if (DwmGetWindowAttribute(h, 9, out fr, 16) != 0) fr = wr; // DWMWA_EXTENDED_FRAME_BOUNDS
      int cloaked; if (DwmGetWindowAttribute(h, 14, out cloaked, 4) != 0) cloaked = 0; // DWMWA_CLOAKED
      r.Add(new string[]{ z.ToString(), h.ToInt64().ToString(), pid.ToString(), c.ToString(), t.ToString(),
        IsWindowVisible(h) ? "1" : "0", IsIconic(h) ? "1" : "0", IsZoomed(h) ? "1" : "0", Long(h, -20).ToString("X"),
        GetWindow(h, 4).ToInt64().ToString(), cloaked.ToString(),
        wr.L + "," + wr.T + "," + wr.R + "," + wr.B, fr.L + "," + fr.T + "," + fr.R + "," + fr.B });
      z++; return true;
    }, IntPtr.Zero);
    return r;
  }
}
"@

$WS_EX_TOPMOST = 0x8L; $WS_EX_TOOLWINDOW = 0x80L

if ($SelfTest) {
    # 숨은 큰 창이 메인으로 잡히던 오인(F05)의 재현 조건을 자기 프로세스에 만든다. 숨은 창은 표시하지 않고, 작은 창은 화면 밖에 비활성으로 띄운다
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -Namespace WOZT -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);'
    $big = New-Object System.Windows.Forms.Form; $big.StartPosition = 'Manual'; $big.Location = New-Object System.Drawing.Point(-20000, -20000); $big.Size = New-Object System.Drawing.Size(1600, 1000)
    $small = New-Object System.Windows.Forms.Form; $small.StartPosition = 'Manual'; $small.ShowInTaskbar = $false; $small.Location = New-Object System.Drawing.Point(-20000, -19000); $small.Size = New-Object System.Drawing.Size(300, 200)
    $bigH = $big.Handle.ToInt64(); $smallH = $small.Handle.ToInt64()
    [void][WOZT.Native]::ShowWindow($small.Handle, 4)   # SW_SHOWNOACTIVATE
    [System.Windows.Forms.Application]::DoEvents()
    $self = $PSCommandPath
    $run = { param([string[]]$extra)
        $o = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $self -Pid $PID @extra
        return (($o -join "`n") | ConvertFrom-Json)
    }
    $a = & $run @()                                  # 기본: 숨은 창은 목록에 없어도 hidden_excluded 에 남아야 한다
    $b = & $run @('-IncludeHidden')                  # 옛 오인 경로: 숨은 창을 목록에 넣어도 메인이 되면 안 된다
    $c = & $run @('-IncludeHidden', '-MainHwnd', "$bigH")   # 숨은 hwnd 를 직접 지정하면 표시로 알린다
    $hasHidden = { param($r) @($r.summary.hidden_excluded | Where-Object { [long]$_.hwnd -eq $bigH }).Count -eq 1 }
    $checks = @(
        [pscustomobject]@{ name = 'hidden_window_not_main'; pass = ([long]$b.summary.main_hwnd -ne $bigH); detail = "include_hidden main=$($b.summary.main_hwnd) hidden=$bigH" }
        [pscustomobject]@{ name = 'main_hwnd_correct_with_hidden'; pass = (([long]$a.summary.main_hwnd -eq $smallH) -and ([long]$b.summary.main_hwnd -eq $smallH)); detail = "visible=$smallH default=$($a.summary.main_hwnd) include_hidden=$($b.summary.main_hwnd) source=$($b.summary.main_source)" }
        [pscustomobject]@{ name = 'hidden_excluded_listed_default'; pass = (& $hasHidden $a); detail = "count=$(@($a.summary.hidden_excluded).Count)" }
        [pscustomobject]@{ name = 'hidden_excluded_listed_include_hidden'; pass = (& $hasHidden $b); detail = "count=$(@($b.summary.hidden_excluded).Count)" }
        [pscustomobject]@{ name = 'hidden_not_counted_as_sdi'; pass = (@($b.summary.judged_sdi | Where-Object { [long]$_ -eq $bigH }).Count -eq 0 -and $b.summary.sdi_count -eq $a.summary.sdi_count); detail = "sdi_default=$($a.summary.sdi_count) sdi_include_hidden=$($b.summary.sdi_count)" }
        [pscustomobject]@{ name = 'explicit_hidden_main_flagged'; pass = (([long]$c.summary.main_hwnd -eq $bigH) -and ($c.summary.main_is_hidden -eq $true) -and ($c.summary.main_source -eq 'param')); detail = "main=$($c.summary.main_hwnd) main_is_hidden=$($c.summary.main_is_hidden)" }
    )
    $small.Close(); $big.Close()
    $failed = @($checks | Where-Object { -not $_.pass }).Count
    [ordered]@{ tool = 'win_owner_z.ps1 self-test'; pid = $PID; total = $checks.Count; failed = $failed; checks = $checks } | ConvertTo-Json -Depth 4
    exit $(if ($failed -eq 0) { 0 } else { 1 })
}

$pids = @()
if ($TargetPid -gt 0) { $pids = @($TargetPid) }
elseif ($ProcName -ne "") { $pids = @(Get-Process -Name $ProcName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
else { [Console]::Error.WriteLine("사용: -Pid <n> 또는 -ProcName <이름>"); exit 2 }

function To-Rect([string]$s) {
    $a = $s.Split(',') | ForEach-Object { [int]$_ }
    return [ordered]@{ left = $a[0]; top = $a[1]; right = $a[2]; bottom = $a[3]; w = ($a[2] - $a[0]); h = ($a[3] - $a[1]) }
}

$wins = New-Object System.Collections.ArrayList
$hiddenExcluded = New-Object System.Collections.ArrayList   # 메인·SDI 판단에서 뺀 숨은 창(비가시·비최소화, 또는 cloaked)
foreach ($w in [WOZ]::Snap()) {
    if ($pids -notcontains [int]$w[2]) { continue }
    $visible = ($w[5] -eq "1"); $iconic = ($w[6] -eq "1")
    if ((-not $visible -and -not $iconic) -or ([int]$w[10] -ne 0)) {
        [void]$hiddenExcluded.Add([pscustomobject][ordered]@{ hwnd = [long]$w[1]; class = $w[3]; title = $w[4]; visible = $visible; cloaked = [int]$w[10]; rect = (To-Rect $w[11]) })
    }
    if (-not $IncludeHidden -and -not $visible -and -not $iconic) { continue }
    $ex = [Convert]::ToInt64($w[8], 16)
    $isShadow = ($w[3] -like "*$ShadowPattern*") -or ($w[4] -like "*$ShadowPattern*")
    [void]$wins.Add([pscustomobject][ordered]@{
        z = [int]$w[0]; hwnd = [long]$w[1]; pid = [int]$w[2]; class = $w[3]; title = $w[4]
        owner = [long]$w[9]; visible = $visible; iconic = $iconic; zoomed = ($w[7] -eq "1")
        topmost = (($ex -band $WS_EX_TOPMOST) -ne 0); toolwindow = (($ex -band $WS_EX_TOOLWINDOW) -ne 0)
        shadow = $isShadow; cloaked = [int]$w[10]; exstyle = $w[8]
        rect = (To-Rect $w[11]); dwm_frame = (To-Rect $w[12])
    })
}

if ($ArrayOnly) {
    ConvertTo-Json -InputObject @($wins) -Depth 5
    exit 0
}

# 메인 창 결정: -MainHwnd 우선, 없으면 숨은 창을 뺀 비그림자 창 중 면적 최대(추정). 숨은 창은 면적이 커도 메인이 아니다(F05)
$isHidden = { param($x) (-not $x.visible -and -not $x.iconic) -or ($x.cloaked -ne 0) }
$mainSource = "param"; $main = $null
if ($MainHwnd -ne 0) { $main = $wins | Where-Object { $_.hwnd -eq $MainHwnd } | Select-Object -First 1 }
if ($null -eq $main) {
    $mainSource = "heuristic_largest_area"
    $main = $wins | Where-Object { -not $_.shadow -and -not (& $isHidden $_) } | Sort-Object { $_.rect.w * $_.rect.h } -Descending | Select-Object -First 1
}
$others = @($wins | Where-Object { -not $_.shadow -and -not (& $isHidden $_) -and ($null -eq $main -or $_.hwnd -ne $main.hwnd) })   # SDI 후보(메인 제외, 숨은 창 제외 비그림자 창)
$shadows = @($wins | Where-Object { $_.shadow })

# AC-05 판정: 보이는(비최소화) 비최상위 SDI 와 그림자는 모두 메인 위(z 가 더 작음)여야 한다
$judgeSdi = @($others | Where-Object { $_.visible -and -not $_.iconic -and -not $_.topmost })
$judgeSh = @($shadows | Where-Object { $_.visible -and -not $_.topmost })
$mainZ = if ($null -ne $main) { $main.z } else { -1 }
$sdiBelow = @($judgeSdi | Where-Object { $_.z -gt $mainZ } | ForEach-Object { $_.hwnd })
$shBelow = @($judgeSh | Where-Object { $_.z -gt $mainZ } | ForEach-Object { $_.hwnd })

$summary = [ordered]@{
    main_hwnd = if ($null -ne $main) { $main.hwnd } else { 0 }; main_source = $mainSource
    main_z = $mainZ; main_iconic = if ($null -ne $main) { $main.iconic } else { $null }; main_owner = if ($null -ne $main) { $main.owner } else { $null }
    main_is_hidden = if ($null -ne $main) { [bool](& $isHidden $main) } else { $null }; hidden_excluded = @($hiddenExcluded)
    sdi_count = $others.Count; shadow_count = $shadows.Count
    sdi_visible_count = @($others | Where-Object { $_.visible -and -not $_.iconic }).Count
    sdi_iconic_count = @($others | Where-Object { $_.iconic }).Count
    sdi_owner_nonzero = @($others | Where-Object { $_.owner -ne 0 } | ForEach-Object { $_.hwnd })
    sdi_owner_zero_all = (@($others | Where-Object { $_.owner -ne 0 }).Count -eq 0)
    sdi_topmost = @($others | Where-Object { $_.topmost } | ForEach-Object { $_.hwnd })
    judged_sdi = @($judgeSdi | ForEach-Object { $_.hwnd }); judged_shadow = @($judgeSh | ForEach-Object { $_.hwnd })
    sdi_below_main = $sdiBelow; shadow_below_main = $shBelow
    all_nontopmost_sdi_and_shadow_above_main = (($null -ne $main) -and ($sdiBelow.Count -eq 0) -and ($shBelow.Count -eq 0) -and (($judgeSdi.Count + $judgeSh.Count) -gt 0))
    note = "z 작을수록 위. 판정 대상=가시·비최소화·비TopMost 의 SDI(메인 제외 비그림자 창)와 PdSdiShadow. 판정 대상 0건이면 all_..._above_main=false(판정 불가). main_source=heuristic 이면 -MainHwnd 로 확정 권장"
}
[ordered]@{
    tool = "win_owner_z.ps1(읽기 전용 — 키·마우스 입력 0, 창 조작 0)"; pids = $pids; window_count = $wins.Count
    summary = $summary; windows = @($wins)
} | ConvertTo-Json -Depth 6
