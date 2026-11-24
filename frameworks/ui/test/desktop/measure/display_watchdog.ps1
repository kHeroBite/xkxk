# 표시 토폴로지 시험의 독립 감시 프로세스 — 마감·부모 소멸·사용자 복귀 시 저장한 구성으로 되돌리고, 실패하면 SDC_APPLY|SDC_USE_DATABASE_CURRENT 로 돌아간다
# display_topology.ps1 이 Start-Process 로 분리 기동한다. WSL·bash_exec 호출이 끊겨도 Windows 쪽에서 계속 돈다. 직접 실행할 일은 없다.
# 결과는 <RunDir>/watchdog_armed.json(무장 확인)·watchdog_result.json(발동 사유·원복 결과). -DryRun 이면 원복 대신 저장 구성을 SDC_VALIDATE 로 검증만 한다.
# 해제: 세션이 원복을 끝내고 <RunDir>/session_done 을 남기면 끝난다. 이때도 구성이 다르면 되돌린다.
param(
    [int]$ParentPid = 0,
    [string]$SnapshotFile = '',
    [string]$RunDir = '',
    [int]$DeadlineSec = 120,
    [string]$UserReturnedFlag = '',
    [int]$RetryWindowSec = 600,      # 잠금 화면 등으로 SetDisplayConfig 가 거부될 때 다시 시도하는 시간
    [switch]$DryRun,
    [switch]$ParseOnly
)
if ($ParseOnly) { 'PARSE_OK'; exit 0 }
# display_topology.ps1 을 점 소싱하면 같은 이름의 매개변수를 덮어쓰므로 먼저 옮겨 둔다
$wd = @{ parent = $ParentPid; snap = $SnapshotFile; dir = $RunDir; deadline = $DeadlineSec; flag = $UserReturnedFlag; retry = $RetryWindowSec; dry = [bool]$DryRun }
$errLog = Join-Path $wd.dir 'watchdog_error.log'
try {
    . (Join-Path $PSScriptRoot 'display_topology.ps1') -Action lib
    $wdSnap = Load-Snapshot $wd.snap
    $parent = $null
    try { $parent = [System.Diagnostics.Process]::GetProcessById($wd.parent) } catch { }   # 핸들을 잡아 PID 재사용에 속지 않는다
    $deadlineAt = (Get-Date).AddSeconds($wd.deadline)
    Write-Json (Join-Path $wd.dir 'watchdog_armed.json') ([pscustomobject]@{ watchdog_pid = $PID; parent_pid = $wd.parent; deadline_at = $deadlineAt.ToString('o'); dry_run = $wd.dry; at = (Get-Stamp) })

    $done = Join-Path $wd.dir 'session_done'
    $reason = $null
    while ($null -eq $reason) {
        if (($wd.flag -ne '') -and (Test-Path -LiteralPath $wd.flag)) { $reason = 'user_returned' }
        elseif (Test-Path -LiteralPath $done) { $reason = 'disarmed' }
        elseif (($null -eq $parent) -or $parent.HasExited) { $reason = 'parent_gone' }
        elseif ((Get-Date) -ge $deadlineAt) { $reason = 'deadline' }
        else { Start-Sleep -Milliseconds 300 }
    }

    $triggeredAt = Get-Stamp
    $retryUntil = (Get-Date).AddSeconds($wd.retry)
    $tries = 0
    while ($true) {
        $tries++
        $rs = Restore-Topology $wdSnap $wd.dry
        Write-Json (Join-Path $wd.dir 'watchdog_result.json') ([pscustomobject]@{ reason = $reason; triggered_at = $triggeredAt; finished_at = (Get-Stamp); tries = $tries; dry_run = $wd.dry; restore = $rs; topology_equal = [bool]$rs.equal })
        if ($rs.equal -or $wd.dry -or (Get-Date) -ge $retryUntil) { break }
        Start-Sleep -Seconds 10
    }
    exit $(if ($rs.equal) { 0 } else { 5 })
}
catch {
    # 감시가 죽어도 저장 없는 구성이라 재부팅이 복구한다. 그래도 한 번은 저장 구성으로 돌린다
    [IO.File]::AppendAllText($errLog, ((Get-Date).ToString('o') + " $_`r`n"))
    if (-not $wd.dry -and ('PdCcd' -as [type])) { [void][PdCcd]::Set($null, 0, $null, 0, 0x8F) }   # 0x8F = SDC_APPLY|SDC_USE_DATABASE_CURRENT
    exit 6
}
