# 작업 표시줄 알림 영역(UIA)에서 Pd 시험 인스턴스의 트레이 아이콘을 찾아 읽고(list) 필요하면 1회 Invoke(invoke)하는 측정 스크립트 — 좌표·입력 API 없음
# 마우스·키보드 입력 주입 함수는 일절 쓰지 않는다. UIA 패턴(Invoke·ExpandCollapse)만 쓴다. 안전 규칙 3(화면 경계 100px)은 좌표 입력 규칙이라 이 경로엔 적용 대상이 아니다.
# 사용: powershell -File tray_uia.ps1 -Mode list   -NameLike "Pd" [-ExpandOverflow]
#       powershell -File tray_uia.ps1 -Mode invoke -NameLike "Pd" -OwnPid <시험 인스턴스 PID> [-ExpandOverflow]
# 출력: JSON 한 덩어리(stdout). 앱 내장 tray.state / tray.invoke(RemoteTrayCommands.cs) 결과와 시각·창 상태를 대조할 수 있게 ownWindows(전/후)를 함께 남긴다.
#       powershell -File tray_uia.ps1 -Mode menu   -OwnPid <PID> [-InvokeItem "표시"] [-CallbackHwnd <hwnd>]
# menu 모드 근거(Avalonia master 소스를 WebFetch 로 확인): Pd 트레이는 Avalonia TrayIcon 이다.
#   TrayIconImpl.UpdateIcon: NOTIFYICONDATA.hWnd = Win32Platform.Instance.Handle, uID = _uniqueId(첫 아이콘=1, ++s_nextUniqueId), uCallbackMessage = WM_TRAYMOUSE = WM_USER+1024(0x800).
#   Win32Platform.CreateMessageWindow: 클래스명 "AvaloniaMessageWindow " + Guid, CreateWindowEx 부모=IntPtr.Zero(HWND_MESSAGE 아님 → 보이지 않는 최상위 창 → EnumWindows 로 열거됨).
#   그 창의 WndProc 이 TrayIconImpl.ProcWnd(hWnd,msg,wParam,lParam) 를 부르고, ProcWnd 는 wParam(=uID)으로 인스턴스를 찾아 lParam==WM_RBUTTONUP(0x205)이면 OnRightClicked → Avalonia 팝업(TrayPopupRoot, 네이티브 HMENU 아님),
#   WM_LBUTTONUP(0x202)이면 Clicked. NOTIFYICON_VERSION_4 미사용 → lParam 은 마우스 메시지 번호 그대로.
#   자동 선택: 자기 PID 소유 창 중 클래스명이 "AvaloniaMessageWindow " 로 시작하는 창이 정확히 1개일 때만. -CallbackHwnd 로 직접 지정 가능(자기 PID 소유 검증). 그 외엔 topWindows/messageWindows 목록만 출력하고 거부.
#   PostMessage(콜백창, 0x800, wParam=-IconId(기본 1), lParam=0x205) → 열린 팝업 창(자기 PID)의 MenuItem 을 UIA 로 읽고 → -InvokeItem 지정 시 그 항목 1개만 Invoke → WM_CANCELMODE(0x1F)/WM_CLOSE 로 닫는다.
# 한계: 네이티브 우클릭 컨텍스트 메뉴는 입력 없이 열 수 없다 → contextMenu 항목에 "NOT_MEASURABLE_WITHOUT_INPUT" 로 출력한다(거짓 PASS 금지).
param(
    [ValidateSet("list", "invoke", "menu")][string]$Mode = "list",
    [string]$NameLike = "Pd",
    [int]$OwnPid = 0,
    [string]$InvokeItem = "",
    [int64]$CallbackHwnd = 0,
    [int]$IconId = 1,
    [switch]$ExpandOverflow,
    [int]$SettleMs = 800,
    [switch]$ParseOnly
)
if ($ParseOnly) { "PARSE_OK"; exit 0 }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class TrayWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
  [DllImport("user32.dll", SetLastError=true)] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  public static uint PidOf(long h) { uint p; GetWindowThreadProcessId(new IntPtr(h), out p); return p; }
  public static string ClassOf(long h) { var c = new StringBuilder(128); GetClassName(new IntPtr(h), c, 128); return c.ToString(); }
  public static bool Exists(long h) { return IsWindow(new IntPtr(h)); }
  public static bool VisibleWin(long h) { return IsWindowVisible(new IntPtr(h)); }
  // 메시지 전용 창(HWND_MESSAGE=-3 의 자식) 중 pid 소유 창 — EnumWindows 에는 나오지 않는다
  public static List<long> MessageWindows(uint pid) {
    var r = new List<long>();
    IntPtr h = IntPtr.Zero;
    while ((h = FindWindowEx(new IntPtr(-3), h, null, null)) != IntPtr.Zero) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) r.Add(h.ToInt64()); }
    return r;
  }
  // 최상위 창(숨김 포함) 중 pid 소유 창
  public static List<long> TopWindows(uint pid) {
    var r = new List<long>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) r.Add(h.ToInt64()); return true; }, IntPtr.Zero);
    return r;
  }
  public static string TitleOf(long h) { var s = new StringBuilder(256); GetWindowText(new IntPtr(h), s, 256); return s.ToString(); }
  public static bool Post(long h, uint m, long w, long l) { return PostMessage(new IntPtr(h), m, new IntPtr(w), new IntPtr(l)); }
  public static List<string> Snapshot(uint pid) {
    var r = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      var t = new StringBuilder(256); GetWindowText(h, t, 256);
      var c = new StringBuilder(128); GetClassName(h, c, 128);
      r.Add(h.ToInt64() + "|" + c + "|" + (IsWindowVisible(h) ? 1 : 0) + "|" + (IsIconic(h) ? 1 : 0) + "|" + t);
      return true;
    }, IntPtr.Zero);
    return r;
  }
}
"@
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

function Get-OwnWindows([int]$procId) {
    if ($procId -le 0) { return @() }
    $out = @()
    foreach ($line in [TrayWin]::Snapshot([uint32]$procId)) {
        $f = $line.Split("|", 5)
        $out += [ordered]@{ hwnd = [int64]$f[0]; cls = $f[1]; visible = ($f[2] -eq "1"); iconic = ($f[3] -eq "1"); title = $f[4] }
    }
    return $out
}

# 알림 영역 후보 루트 — Win11(Shell_TrayWnd 안 XAML, 오버플로 TopLevelWindowForOverflowXamlIsland) / Win10(NotifyIconOverflowWindow)
function Get-TrayRoots() {
    $roots = @()
    foreach ($cn in @("Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland")) {
        $c = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, $cn)
        foreach ($e in $A::RootElement.FindAll($TS::Children, $c)) { $roots += $e }
    }
    return $roots
}

function Find-Buttons() {
    $found = @()
    $btn = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Button)
    foreach ($root in (Get-TrayRoots)) {
        $rootCls = $root.Current.ClassName
        foreach ($e in $root.FindAll($TS::Descendants, $btn)) {
            $found += [pscustomobject]@{ el = $e; root = $rootCls }
        }
    }
    return $found
}

function Describe($b) {
    $e = $b.el
    $r = $e.Current.BoundingRectangle
    $pats = @()
    foreach ($p in $e.GetSupportedPatterns()) { $pats += $p.ProgrammaticName }
    return [ordered]@{
        name = $e.Current.Name; className = $e.Current.ClassName; automationId = $e.Current.AutomationId
        root = $b.root; offscreen = $e.Current.IsOffscreen
        rect = @([math]::Round($r.X), [math]::Round($r.Y), [math]::Round($r.Width), [math]::Round($r.Height))
        patterns = $pats
    }
}

function Close-Popup([int64]$h) {
    # WM_CANCELMODE(0x1F) 로 모달 메뉴 모드를 풀고, 아직 보이면 WM_CLOSE(0x10) — 키 입력(Esc) 없이 닫는다
    [void][TrayWin]::Post($h, 0x1F, 0, 0)
    Start-Sleep -Milliseconds 250
    if ([TrayWin]::Exists($h) -and [TrayWin]::VisibleWin($h)) { [void][TrayWin]::Post($h, 0x10, 0, 0); Start-Sleep -Milliseconds 250 }
    return -not ([TrayWin]::Exists($h) -and [TrayWin]::VisibleWin($h))
}

$result = [ordered]@{ ok = $true; mode = $Mode; nameLike = $NameLike; ownPid = $OwnPid; inputApiUsed = $false }
$expandedChevron = $null
$chevronState = "not-touched"
try {
  if ($Mode -eq "menu") {
    if ($OwnPid -le 0) { throw "menu 는 -OwnPid(자기 인스턴스 PID) 필수" }
    $msgWins = @([TrayWin]::MessageWindows([uint32]$OwnPid))
    $topWins = @([TrayWin]::TopWindows([uint32]$OwnPid))
    $result.messageWindows = @($msgWins | ForEach-Object { [ordered]@{ hwnd = $_; cls = [TrayWin]::ClassOf($_); title = [TrayWin]::TitleOf($_); visible = [TrayWin]::VisibleWin($_) } })
    $result.topWindows = @($topWins | ForEach-Object { [ordered]@{ hwnd = $_; cls = [TrayWin]::ClassOf($_); title = [TrayWin]::TitleOf($_); visible = [TrayWin]::VisibleWin($_) } })
    $cands = @(($topWins + $msgWins) | Where-Object { [TrayWin]::ClassOf($_) -like "AvaloniaMessageWindow *" })
    $result.callbackCandidates = @($cands)
    if ($CallbackHwnd -gt 0) {
        if ([TrayWin]::PidOf($CallbackHwnd) -ne [uint32]$OwnPid) { throw "거부 - -CallbackHwnd 가 -OwnPid 소유 창이 아니다" }
        $cb = $CallbackHwnd
    } elseif ($cands.Count -eq 1) { $cb = [int64]$cands[0] }
    else { throw "거부 - 클래스 'AvaloniaMessageWindow *' 후보가 $($cands.Count)개라 콜백 창을 특정할 수 없다(topWindows/messageWindows 참고해 -CallbackHwnd 지정)" }
    $result.callbackHwnd = $cb; $result.callbackClass = [TrayWin]::ClassOf($cb)
    $result.ownWindowsBefore = @(Get-OwnWindows $OwnPid)
    $result.posted = [TrayWin]::Post($cb, 0x800, $IconId, 0x205)   # WM_USER+1024, wParam=트레이 uID, lParam=WM_RBUTTONUP
    Start-Sleep -Milliseconds $SettleMs
    $pc = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $OwnPid)
    $mi = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::MenuItem)
    $items = @(); $popupHwnd = 0
    foreach ($w in $A::RootElement.FindAll($TS::Children, $pc)) {
        $found = $w.FindAll($TS::Descendants, $mi)
        if ($found.Count -gt 0) {
            $popupHwnd = [int64]$w.Current.NativeWindowHandle
            foreach ($e in $found) { $items += [ordered]@{ name = $e.Current.Name; enabled = $e.Current.IsEnabled; patterns = @($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }) } }
            if ($InvokeItem) {
                $tgt = @($found | Where-Object { $_.Current.Name -eq $InvokeItem })
                if ($tgt.Count -eq 1) {
                    try { $tgt[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $result.invoke = [ordered]@{ attempted = $true; item = $InvokeItem } }
                    catch { $result.invoke = [ordered]@{ attempted = $false; item = $InvokeItem; reason = "InvokePattern 미노출/실패" } }
                } else { $result.invoke = [ordered]@{ attempted = $false; item = $InvokeItem; reason = "이름 일치 항목 $($tgt.Count)개" } }
            }
            break
        }
    }
    $result.menuOpened = ($items.Count -gt 0)
    $result.menuItems = $items
    $result.popupHwnd = $popupHwnd
    if ($InvokeItem) { Start-Sleep -Milliseconds $SettleMs }
    if ($popupHwnd -gt 0 -and [TrayWin]::PidOf($popupHwnd) -eq [uint32]$OwnPid) { $result.popupClosed = (Close-Popup $popupHwnd) }
    $result.ownWindowsAfter = @(Get-OwnWindows $OwnPid)
  } else {
    $all = Find-Buttons
    $match = @($all | Where-Object { $_.el.Current.Name -like "*$NameLike*" })
    # 기본 영역에 없고 오버플로 펼침 허용 시에만 쉐브론("숨겨진 아이콘"/"Show Hidden Icons"/"Notification Chevron")을 펼쳤다가 즉시 접는다
    if ($match.Count -eq 0 -and $ExpandOverflow) {
        $result.scannedBeforeExpand = $all.Count
        $result.chevronCandidates = @($all | Where-Object { $_.el.Current.Name -match "숨겨진|Hidden|Chevron|Overflow|오버플로|알림" } | ForEach-Object { $_.el.Current.Name })
        $chev = $all | Where-Object { $_.el.Current.Name -match "숨겨진|Hidden|Chevron|Overflow|오버플로" } | Select-Object -First 1
        if ($chev) {
            $expandedChevron = $chev.el
            try {
                $ec = $expandedChevron.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                $ec.Expand(); $chevronState = "expanded"
            } catch {
                try { $expandedChevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $chevronState = "invoked-open" } catch { $chevronState = "open-failed" }
            }
            Start-Sleep -Milliseconds 500
            $all = Find-Buttons
            $match = @($all | Where-Object { $_.el.Current.Name -like "*$NameLike*" })
        } else { $chevronState = "no-chevron" }
    }
    $result.buttonsScanned = $all.Count
    if ($match.Count -eq 0) { $result.scannedNames = @($all | ForEach-Object { [ordered]@{ name = $_.el.Current.Name; cls = $_.el.Current.ClassName; root = $_.root; offscreen = $_.el.Current.IsOffscreen } }) }
    $result.matches = @($match | ForEach-Object { Describe $_ })
    $result.matchCount = $match.Count
    $result.contextMenu = "NOT_MEASURABLE_WITHOUT_INPUT"
    $result.ownWindowsBefore = @(Get-OwnWindows $OwnPid)

    if ($Mode -eq "invoke") {
        if ($OwnPid -le 0) { throw "invoke 는 -OwnPid(자기 인스턴스 PID) 필수" }
        if ($match.Count -ne 1) { throw "invoke 거부 — 이름 일치 버튼이 정확히 1개여야 한다(현재 $($match.Count)). 사용자 알림 영역 아이콘 불간섭을 위해 -NameLike 를 좁혀라" }
        $inv = $null
        try { $inv = $match[0].el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) } catch { }
        if (-not $inv) {
            $result.invoke = [ordered]@{ attempted = $false; reason = "InvokePattern 미노출" }
        } else {
            $inv.Invoke()
            Start-Sleep -Milliseconds $SettleMs
            $result.invoke = [ordered]@{ attempted = $true; target = $match[0].el.Current.Name }
        }
        $result.ownWindowsAfter = @(Get-OwnWindows $OwnPid)
    }
  }
} catch {
    $result.ok = $false
    $result.error = $_.Exception.Message
} finally {
    # 펼친 쉐브론은 반드시 접는다(사용자 알림 영역 원상 복귀)
    if ($expandedChevron) {
        try { $expandedChevron.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse(); $chevronState = "collapsed" }
        catch {
            try { if ($chevronState -eq "invoked-open") { $expandedChevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $chevronState = "invoked-close" } } catch { $chevronState = "close-failed" }
        }
    }
    $result.chevron = $chevronState
}
$result | ConvertTo-Json -Depth 6 -Compress
if ($result.ok) { exit 0 } else { exit 2 }
