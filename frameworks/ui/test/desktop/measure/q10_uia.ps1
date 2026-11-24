# Q10 제품판 그리드 첫 열기 시간을 UIAutomation(L1, 실입력 없음)으로 재는 스크립트 — 파일 메뉴 펼침 → '그리드 열기' Invoke → grid.dgMain·grid.lblCount 가 나타날 때까지 폴링
param(
    [string]$ProcName = "xkxk.frameworks.ui.desktop",
    [int]$TimeoutMs = 15000,
    [int]$PollMs = 5,
    [switch]$ParseOnly
)
if ($ParseOnly) { "PARSE_OK"; exit 0 }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8  # count_text(한글)를 WSL 쪽이 UTF-8 로 받게 한다(otest-1 11:1x — cp949 로 섞여 요약이 죽음)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function ById($root, $id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    return $root.FindFirst($TS::Descendants, $c)
}
$p = Get-Process -Name $ProcName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { '{"ok":false,"error":"no-process"}'; exit 2 }
$pc = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
$win = $A::RootElement.FindFirst($TS::Children, $pc)
if (-not $win) { '{"ok":false,"error":"no-window"}'; exit 3 }
$file = ById $win "shell.mnuFile"
if (-not $file) { '{"ok":false,"error":"no-mnuFile"}'; exit 4 }
try { $file.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() } catch { '{"ok":false,"error":"expand-failed"}'; exit 5 }
# 펼친 하위 메뉴는 별도 팝업 창일 수 있어 같은 프로세스의 최상위 창 전체에서 찾는다
$item = $null
$sw0 = [Diagnostics.Stopwatch]::StartNew()
while (-not $item -and $sw0.ElapsedMilliseconds -lt 3000) {
    foreach ($w in $A::RootElement.FindAll($TS::Children, $pc)) { $item = ById $w "shell.mnuOpenGrid"; if ($item) { break } }
    if (-not $item) { Start-Sleep -Milliseconds 20 }
}
if (-not $item) { '{"ok":false,"error":"no-mnuOpenGrid"}'; exit 6 }
$inv = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$sw = [Diagnostics.Stopwatch]::StartNew()
$t0 = Get-Date
$inv.Invoke()
$invokeMs = $sw.Elapsed.TotalMilliseconds
$gridMs = $null; $countMs = $null; $countText = ""
while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    if ($null -eq $gridMs) {
        $g = ById $win "grid.dgMain"
        if ($g -and -not $g.Current.BoundingRectangle.IsEmpty) { $gridMs = $sw.Elapsed.TotalMilliseconds }
    }
    if ($null -ne $gridMs) {
        $c = ById $win "grid.lblCount"
        if ($c -and $c.Current.Name) { $countMs = $sw.Elapsed.TotalMilliseconds; $countText = $c.Current.Name; break }
    }
    Start-Sleep -Milliseconds $PollMs
}
# 폴링 1회 비용(트리 검색) — 측정값에 섞이는 오차의 크기를 함께 남긴다
# 같은 요소를 다시 찾으면 UIA 캐시로 0ms 가 나올 수 있어(otest-1 11:1x 관찰) 아직 찾지 않은 요소로 잰다
$sw2 = [Diagnostics.Stopwatch]::StartNew(); $null = ById $win "grid.txtFilter"; $probeMs = $sw2.Elapsed.TotalMilliseconds
$o = [ordered]@{ ok = ($null -ne $countMs); t0 = $t0.ToString("HH:mm:ss.fff"); invoke_ms = [math]::Round($invokeMs,1);
    grid_visible_ms = if ($gridMs) { [math]::Round($gridMs,1) } else { $null };
    count_ms = if ($countMs) { [math]::Round($countMs,1) } else { $null }; count_text = $countText; poll_cost_ms = [math]::Round($probeMs,1) }
$o | ConvertTo-Json -Compress
