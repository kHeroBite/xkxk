# 전경 창을 N초 동안 0.25초 간격으로 기록하고 Pd.Poc.Desktop 이 전경이 된 순간을 센다(읽기 전용) — 사용: -Seconds <n> -Out <파일>
param([int]$Seconds = 20, [string]$Out)
Add-Type -Namespace W -Name G -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@
$end = (Get-Date).AddSeconds($Seconds); $hits = 0; $n = 0; $lines = @()
while ((Get-Date) -lt $end) {
  $h = [W.G]::GetForegroundWindow(); $p = 0; [void][W.G]::GetWindowThreadProcessId($h, [ref]$p)
  $name = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
  if ($name -eq 'Pd.Poc.Desktop') { $hits++ }
  $lines += "$((Get-Date).ToString('HH:mm:ss.fff')) fg=$h $name"; $n++
  Start-Sleep -Milliseconds 250
}
$lines | Set-Content -Path $Out
"samples=$n pd_foreground=$hits"
