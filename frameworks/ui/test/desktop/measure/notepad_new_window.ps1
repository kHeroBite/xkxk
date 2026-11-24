# 시험용 새 메모장 창을 사용자 창과 분리해 확보·종료하는 래퍼 — 읽기 전용 판별(detect) → 확정 시에만 1회 실행(run) → 새 HWND 에만 WM_CLOSE(close). 입력 API(SendInput·keybd_event·mouse_event·SetCursorPos·키 PostMessage) 미사용
param(
    [ValidateSet("detect","run","close")][string]$Mode = "detect",
    [string]$StateFile = "",            # run 이 쓰고 close 가 읽는 실행 전 HWND 스냅샷 JSON 경로
    [long]$OwnHwnd = 0,                 # close 대상(run 이 출력한 own_hwnd)
    [int]$NewWindowValue = -1,          # settings.dat 'OpenFile' 값 중 '새 창으로 열림' 을 뜻하는 값(실측으로 확인된 값만 지정, -1=미지정=판별 불가)
    [string]$SettingsPath = "",         # 기본: %LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\settings.dat
    [string]$ScratchDir = "",           # settings.dat 사본을 둘 폴더(기본 %TEMP%)
    [int]$WaitMs = 6000,
    [switch]$ParseOnly
)
if ($ParseOnly) { "PARSE_OK"; exit 0 }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class NW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<string[]> Snap() {
    var r = new List<string[]>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      var t = new StringBuilder(512); GetWindowText(h, t, 512);
      uint pid; GetWindowThreadProcessId(h, out pid);
      r.Add(new string[]{ h.ToInt64().ToString(), c.ToString(), pid.ToString(), t.ToString() });
      return true;
    }, IntPtr.Zero);
    return r;
  }
  public static bool Close(long h) { return PostMessage(new IntPtr(h), 0x0010, IntPtr.Zero, IntPtr.Zero); } // WM_CLOSE 만(키 이벤트 아님)
}
"@

function Get-Notepad { # 최상위 가시 창 중 Notepad 클래스만 {hwnd,pid,title} 로
    $o = @()
    foreach ($e in [NW]::Snap()) { if ($e[1] -eq "Notepad") { $o += [pscustomobject]@{ hwnd = [long]$e[0]; pid = [int]$e[2]; title = $e[3] } } }
    return ,$o
}
function Out-Json($obj) { $obj | ConvertTo-Json -Depth 6 -Compress }

# ---- detect: settings.dat 사본에서 'OpenFile' DWORD(vk 레코드, 인라인 데이터)를 읽기 전용으로 판독 ----
function Get-OpenFileValue {
    $src = $SettingsPath
    if (-not $src) { $src = Join-Path $env:LOCALAPPDATA "Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\settings.dat" }
    $scr = $ScratchDir; if (-not $scr) { $scr = $env:TEMP }
    $res = [ordered]@{ source = $src; found = $false; value = $null; note = "" }
    if (-not (Test-Path -LiteralPath $src)) { $res.note = "settings.dat 없음"; return $res }
    $dst = Join-Path $scr ("nw_settings_copy_{0}.dat" -f $PID)
    try { Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop }   # 원본은 열지 않고 복사만(잠금 시 실패 → 판별 불가)
    catch { $res.note = "사본 생성 실패: " + $_.Exception.Message; return $res }
    try {
        $b = [System.IO.File]::ReadAllBytes($dst)
        $name = [System.Text.Encoding]::ASCII.GetBytes("OpenFile")
        $hits = @()
        for ($i = 20; $i -le $b.Length - $name.Length; $i++) {
            $m = $true
            for ($j = 0; $j -lt $name.Length; $j++) { if ($b[$i + $j] -ne $name[$j]) { $m = $false; break } }
            if (-not $m) { continue }
            $s = $i - 20   # vk 헤더: sig(2) namelen(2) datalen(4) dataoff(4) type(4) flags(2) spare(2) name
            if ($b[$s] -eq 0x76 -and $b[$s+1] -eq 0x6B) {
                $nl = [BitConverter]::ToUInt16($b, $s + 2)
                $dl = [BitConverter]::ToUInt32($b, $s + 4)
                $ty = [BitConverter]::ToUInt32($b, $s + 12)
                if ($nl -eq $name.Length -and $ty -eq 4 -and (($dl -band 0x80000000) -ne 0)) { $hits += [BitConverter]::ToUInt32($b, $s + 8) }
            }
        }
        if ($hits.Count -eq 1) { $res.found = $true; $res.value = [int]$hits[0] }
        elseif ($hits.Count -eq 0) { $res.note = "OpenFile 값 레코드 미발견" }
        else { $res.note = "OpenFile 레코드 " + $hits.Count + "건(모호)"; }
    } finally { Remove-Item -LiteralPath $dst -Force -ErrorAction SilentlyContinue }
    return $res
}

function Get-Verdict {
    $r = Get-OpenFileValue
    $v = "UNDETERMINED"; $why = $r.note
    if ($r.found) {
        if ($NewWindowValue -lt 0) { $why = "OpenFile=" + $r.value + " 판독됐으나 '새 창' 값 매핑 미지정(-NewWindowValue) → 판별 불가" }
        elseif ($r.value -eq $NewWindowValue) { $v = "NEW_WINDOW"; $why = "OpenFile=" + $r.value + " == 지정한 새 창 값" }
        else { $v = "TAB"; $why = "OpenFile=" + $r.value + " != 새 창 값 " + $NewWindowValue + " → 탭 결합" }
    }
    return [ordered]@{ verdict = $v; reason = $why; settings = $r }
}

if ($Mode -eq "detect") {
    $vd = Get-Verdict
    $vd.notepad_windows = Get-Notepad
    $vd.action = if ($vd.verdict -eq "NEW_WINDOW") { "run 가능" } else { "판별 불가/탭 결합 — 실행하지 않음, 대체 창으로 PASS 근거를 만들지 않는다" }
    Out-Json $vd; exit 0
}

if ($Mode -eq "run") {
    $vd = Get-Verdict
    if ($vd.verdict -ne "NEW_WINDOW") {
        Out-Json ([ordered]@{ mode = "run"; executed = $false; verdict = $vd.verdict; reason = $vd.reason; message = "판별 불가/탭 결합 — 메모장을 실행하지 않았다" }); exit 2
    }
    $before = Get-Notepad
    $beforeSet = @{}; foreach ($w in $before) { $beforeSet[[string]$w.hwnd] = $w.title }
    Start-Process notepad.exe
    $own = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $WaitMs) {
        Start-Sleep -Milliseconds 100
        $now = Get-Notepad
        $new = @($now | Where-Object { -not $beforeSet.ContainsKey([string]$_.hwnd) })
        if ($new.Count -ge 1) { $own = $new; break }
    }
    $now = Get-Notepad
    $changed = @($now | Where-Object { $beforeSet.ContainsKey([string]$_.hwnd) -and $beforeSet[[string]$_.hwnd] -ne $_.title })
    if ($StateFile) { Out-Json ([ordered]@{ before = $before }) | Set-Content -LiteralPath $StateFile -Encoding ASCII }
    if (-not $own -or $own.Count -ne 1 -or $changed.Count -gt 0) {
        # 새 HWND 0건/다수 또는 기존 창 제목 변동(탭 추가 의심) → 중단·기록. 복구·종료 시도 안 함
        Out-Json ([ordered]@{ mode = "run"; executed = $true; ok = $false; new_hwnd_count = @($own).Count; user_title_changed = $changed; message = "새 HWND 확정 실패 또는 사용자 창 제목 변동 — 중단, 복구하지 않음(보고 대상)" }); exit 3
    }
    Out-Json ([ordered]@{ mode = "run"; executed = $true; ok = $true; own_hwnd = $own[0].hwnd; own_pid = $own[0].pid; own_title = $own[0].title; before_count = $before.Count }); exit 0
}

if ($Mode -eq "close") {
    if ($OwnHwnd -le 0) { Out-Json ([ordered]@{ mode = "close"; ok = $false; message = "-OwnHwnd 필요" }); exit 4 }
    if ($StateFile -and (Test-Path -LiteralPath $StateFile)) {
        $st = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
        foreach ($w in @($st.before)) { if ([long]$w.hwnd -eq $OwnHwnd) { Out-Json ([ordered]@{ mode = "close"; ok = $false; message = "실행 전부터 있던 창(사용자 창) — 닫지 않는다" }); exit 5 } }
    } else {
        Out-Json ([ordered]@{ mode = "close"; ok = $false; message = "-StateFile(run 이 남긴 실행 전 스냅샷) 없음 — 사용자 창 여부를 확인할 수 없어 닫지 않는다" }); exit 5
    }
    $t = @(Get-Notepad | Where-Object { $_.hwnd -eq $OwnHwnd })
    if ($t.Count -ne 1) { Out-Json ([ordered]@{ mode = "close"; ok = $false; message = "대상 HWND 가 Notepad 클래스 가시 창으로 존재하지 않음" }); exit 6 }
    $sent = [NW]::Close($OwnHwnd)
    Start-Sleep -Milliseconds 800
    $still = @(Get-Notepad | Where-Object { $_.hwnd -eq $OwnHwnd }).Count
    Out-Json ([ordered]@{ mode = "close"; ok = ($sent -and $still -eq 0); wm_close_sent = $sent; still_open = ($still -gt 0); note = "저장 확인 대화상자가 뜨면 still_open=true 일 수 있다 — 사용자 창이 아니므로 별도 처리 후 보고" }); exit 0
}
