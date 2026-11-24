# 전경 프로세스가 기대 PID 일 때만 실제 키를 보내고 주입 시각을 남기는 키 입력 가드
#
# 사용 예
#   .\safe_key.ps1 -Key F1 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#   .\safe_key.ps1 -Key Alt+Space -ExpectedProcessId 41312 -ForbiddenProcessId 352
#
# 규칙
#   - 전경 창의 PID 가 -ExpectedProcessId 와 같을 때만 보낸다. 다르면 throw 하고 아무것도 보내지 않는다.
#   - 조합키는 누름·뗌을 한 번의 SendInput 배열로 보낸다. 중간에 다른 입력이 끼지 않고 수식키가 눌린 채 남지 않는다.
#   - 전경을 가져오지 않는다. 전경 획득은 호출자 몫이다.
#   - 주입 시각은 이 스크립트가 injections.jsonl 에 남긴다. 기록에 실패하면 보내지 않는다.
#   - logs/pcaway/user_returned.flag 가 있으면 보내지 않는다.
#   - 호출자는 exit code 로 판정한다(0=전송). 이 파일은 BOM 이 있어야 PowerShell 5.1 이 한글을 바르게 읽는다.
#
# 못 잡는 것
#   검사와 전송 사이 수 ms 안에 전경이 바뀌면 막지 못한다. 전경이 맞아도 같은 프로세스의 어느 컨트롤이 받는지는 보지 않는다.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Key,
    [Parameter(Mandatory = $true)] [int] $ExpectedProcessId,
    [int] $Repeat = 1,
    # 입력 금지 PID(그때의 사용자 Pd). 기본값이 없으며 빠지거나 1 미만이면 입력 전에 거부한다.
    [Parameter(Mandatory = $true)] [ValidateRange(1, [int]::MaxValue)] [int[]] $ForbiddenProcessId,
    [string] $InjectionLog = '',
    [string] $FlagPath = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if (-not ('PdSafeKeyNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PdSafeKeyNative {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT_KB { public uint type; public KEYBDINPUT ki; public int pad1; public int pad2; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT_KB[] p, int cb);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;

    public static uint ForegroundPid() {
        uint pid = 0;
        IntPtr h = GetForegroundWindow();
        if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid);
        return pid;
    }

    static INPUT_KB Make(ushort vk, bool extended, bool up) {
        INPUT_KB i = new INPUT_KB();
        i.type = 1;
        i.ki.wVk = vk;
        i.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i.ki.dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        return i;
    }

    // 전송 직전 전경을 한 번 더 본다. 다르면 0 을 돌려주고 보내지 않는다.
    public static uint SendChord(uint expectedPid, ushort[] vks, bool[] extended) {
        if (ForegroundPid() != expectedPid) return 0;
        INPUT_KB[] seq = new INPUT_KB[vks.Length * 2];
        for (int i = 0; i < vks.Length; i++) {
            seq[i] = Make(vks[i], extended[i], false);
            seq[seq.Length - 1 - i] = Make(vks[i], extended[i], true);
        }
        return SendInput((uint)seq.Length, seq, Marshal.SizeOf(typeof(INPUT_KB)));
    }
}
"@
}

function Fail-Guard { param([string] $Reason) throw "safe_key 차단: $Reason" }

$logDir = Join-Path $PSScriptRoot '..\..\..\logs\pcaway'
if (-not $InjectionLog) { $InjectionLog = Join-Path $logDir 'injections.jsonl' }
if (-not $FlagPath) { $FlagPath = Join-Path $logDir 'user_returned.flag' }

function Write-Injection {
    param([string] $Kind, $X, $Y)
    $line = [ordered]@{ t_ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); kind = $Kind; x = $X; y = $Y; source = 'safe_key'; key = $Key } | ConvertTo-Json -Compress
    $dir = Split-Path -Parent $InjectionLog
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::AppendAllText($InjectionLog, $line + "`n", [System.Text.UTF8Encoding]::new($false))
}

# 키 이름표. Win 키와 시스템 전환 조합은 시험 앱 밖으로 입력이 나가므로 넣지 않는다.
$table = @{ Ctrl = 0x11; Shift = 0x10; Alt = 0x12; Esc = 0x1B; Enter = 0x0D; Tab = 0x09; Space = 0x20; Backspace = 0x08 }
$extendedTable = @{ Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28; Home = 0x24; End = 0x23; PageUp = 0x21; PageDown = 0x22; Insert = 0x2D; Delete = 0x2E }
for ($n = 1; $n -le 24; $n++) { $table["F$n"] = 0x6F + $n }
foreach ($c in [char[]]'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789') { $table["$c"] = [int]$c }

$names = @($Key -split '\+' | ForEach-Object { $_.Trim() })
$vks = New-Object System.Collections.Generic.List[uint16]
$exts = New-Object System.Collections.Generic.List[bool]
foreach ($name in $names) {
    if ($table.ContainsKey($name)) { $vks.Add([uint16]$table[$name]); $exts.Add($false) }
    elseif ($extendedTable.ContainsKey($name)) { $vks.Add([uint16]$extendedTable[$name]); $exts.Add($true) }
    else { Fail-Guard "지원하지 않는 키 이름: '$name'" }
}
$hasAlt = $names -contains 'Alt'
if (($hasAlt -and ($names -contains 'Tab' -or $names -contains 'Esc')) -or (($names -contains 'Ctrl') -and ($names -contains 'Esc')) -or (($names -contains 'Ctrl') -and $hasAlt -and ($names -contains 'Delete'))) {
    Fail-Guard "시험 앱 밖으로 나가는 시스템 조합이다: $Key"
}
if ($Repeat -lt 1) { Fail-Guard "Repeat 는 1 이상이어야 한다: $Repeat" }

if ($ExpectedProcessId -le 0) { Fail-Guard 'ExpectedProcessId 가 필요하다' }
if ($ForbiddenProcessId -contains $ExpectedProcessId) { Fail-Guard "입력 금지 PID 다: $ExpectedProcessId" }
if (Test-Path -LiteralPath $FlagPath) { Fail-Guard "사용자 복귀 플래그가 있다: $FlagPath" }

try { [void][PdSafeKeyNative]::SetProcessDpiAwareness(2) } catch { }

$before = [PdSafeKeyNative]::ForegroundPid()
if ($before -ne [uint32]$ExpectedProcessId) { Fail-Guard "전경 PID 불일치. 기대=$ExpectedProcessId 실제=$before" }

$vkArray = $vks.ToArray()
$extArray = $exts.ToArray()
Write-Injection 'key_pre' $null $null
for ($i = 1; $i -le $Repeat; $i++) {
    $sent = [PdSafeKeyNative]::SendChord([uint32]$ExpectedProcessId, $vkArray, $extArray)
    if ($sent -ne [uint32]($vkArray.Length * 2)) {
        Write-Injection 'key_failed' $null $null
        Fail-Guard "전송 실패 또는 전송 직전 전경 변경. 회차=$i 전송=$sent 현재 전경=$([PdSafeKeyNative]::ForegroundPid())"
    }
    if ($i -lt $Repeat) { Start-Sleep -Milliseconds 60 }
}
Write-Injection 'key' $null $null
Write-Output ('{{"sent":true,"key":"{0}","repeat":{1},"foreground_pid":{2}}}' -f $Key, $Repeat, $before)
exit 0
