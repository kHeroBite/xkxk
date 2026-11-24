# 전경 창을 0.25초 간격으로 감시해 바뀔 때만 기록하고, Pd.Poc.Desktop 이 전경이 되면 HIT 줄을 남긴다(읽기 전용) — 사용: -Seconds <n> -Out <파일>; -StopFile <파일> 이 생기면 조기 종료
param([int]$Seconds = 600, [string]$Out, [string]$StopFile = "")
Add-Type -Namespace W -Name G2 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@
$end = (Get-Date).AddSeconds($Seconds); $last = [IntPtr]::Zero; $hits = 0; $n = 0
"start $((Get-Date).ToString('HH:mm:ss.fff'))" | Add-Content -Path $Out
while ((Get-Date) -lt $end) {
  if ($StopFile -ne "" -and (Test-Path $StopFile)) { break }
  $h = [W.G2]::GetForegroundWindow(); $n++
  if ($h -ne $last) {
    $p = 0; [void][W.G2]::GetWindowThreadProcessId($h, [ref]$p)
    $name = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
    $tag = if ($name -eq 'Pd.Poc.Desktop') { $hits++; 'HIT' } else { 'fg' }
    "$tag $((Get-Date).ToString('HH:mm:ss.fff')) hwnd=$h pid=$p $name" | Add-Content -Path $Out
    $last = $h
  }
  Start-Sleep -Milliseconds 250
}
"end $((Get-Date).ToString('HH:mm:ss.fff')) samples=$n pd_foreground_transitions=$hits" | Add-Content -Path $Out
