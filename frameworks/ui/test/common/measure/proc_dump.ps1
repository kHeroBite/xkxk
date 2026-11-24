# 지정 이름 프로세스의 PID·부모·시작 시각·명령줄을 한 줄씩 출력하는 점검 스크립트(빌드 종료 직후 잔존 dotnet.exe 정체 기록용)
param([string]$Name = "dotnet.exe")
$list = @(Get-CimInstance Win32_Process -Filter ("Name='" + $Name + "'"))
"count=" + $list.Count
foreach ($x in $list) {
  $p = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $x.ParentProcessId)
  "pid=" + $x.ProcessId + " ppid=" + $x.ParentProcessId + " parent=" + $p.Name + " start=" + $x.CreationDate.ToString("HH:mm:ss") + " cmd=" + $x.CommandLine
}
