# 전경 프로세스와 목표 좌표 소유 창이 기대 PID 일 때만 SendInput 상대 이동으로 커서를 옮기고 주입 시각을 남기는 마우스 이동 가드(클릭 없음)
#
# 사용 예
#   .\safe_move.ps1 -ScreenX -1500 -ScreenY 500 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#   .\safe_move.ps1 -ScreenX -1500 -ScreenY 500 -Steps 30 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#
# 규칙
#   - 전경 창의 PID 가 -ExpectedProcessId 와 같고 목표점의 창도 같은 PID 일 때만 이동한다. 다르면 throw 한다.
#   - 프레임마다 전경과 사용자 복귀 플래그를 다시 본다. 바뀌면 이동을 멈추고 throw 한다.
#   - 화면 경계 100px 안의 좌표는 거부한다(safe_touch 와 같다).
#   - 전경을 가져오지 않는다. 전경 획득은 호출자 몫이다.
#   - MOUSEEVENTF_MOVE 상대 이동만 단계별로 보낸다(SetCursorPos 가 아님). 포인터 가속 때문에 오차가 나므로 마지막에 보정 이동을 반복한다.
#   - 클릭·휠은 보내지 않는다.
#   - 주입 시각은 이 스크립트가 injections.jsonl 에 남긴다. 기록에 실패하면 주입하지 않는다.
#   - expected_cursor 규약은 safe_touch 와 같다. 기록의 x,y 는 이동 뒤 기대 커서, alt_x,alt_y 는 이동 전 커서이며 복귀 감지 기준이 된다.
#   - logs/pcaway/user_returned.flag 가 있으면 주입하지 않는다.
#   - 호출자는 exit code 로 판정한다(0=이동). 이 파일은 BOM 이 있어야 PowerShell 5.1 이 한글을 바르게 읽는다.
#
# 못 잡는 것
#   검사와 주입 사이 수 ms 안의 창 변경은 막지 못한다. 마지막 보정 뒤 1px 안 오차는 허용한다.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [int] $ScreenX,
    [Parameter(Mandatory = $true)] [int] $ScreenY,
    [Parameter(Mandatory = $true)] [int] $ExpectedProcessId,
    [int] $Steps = 20,
    # 입력 금지 PID(그때의 사용자 Pd). 기본값이 없으며 빠지거나 1 미만이면 입력 전에 거부한다.
    [Parameter(Mandatory = $true)] [ValidateRange(1, [int]::MaxValue)] [int[]] $ForbiddenProcessId,
    [string] $InjectionLog = '',
    [string] $FlagPath = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if (-not ('PdSafeMoveNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PdSafeMoveNative {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);

    const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x0001, GA_ROOT = 2;

    public static uint ForegroundPid() {
        uint pid = 0;
        IntPtr h = GetForegroundWindow();
        if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid);
        return pid;
    }

    public static uint PidAt(int x, int y) {
        POINT p = new POINT(); p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        if (h == IntPtr.Zero) return 0;
        IntPtr root = GetAncestor(h, GA_ROOT);
        uint pid = 0;
        GetWindowThreadProcessId(root == IntPtr.Zero ? h : root, out pid);
        return pid;
    }

    // MOUSEEVENTF_MOVE 상대 이동 1회. 클릭 플래그는 절대 넣지 않는다
    public static bool MoveRel(int dx, int dy) {
        INPUT[] a = new INPUT[1];
        a[0].type = INPUT_MOUSE;
        a[0].mi.dx = dx;
        a[0].mi.dy = dy;
        a[0].mi.dwFlags = MOUSEEVENTF_MOVE;
        return SendInput(1, a, Marshal.SizeOf(typeof(INPUT))) == 1;
    }
}
"@
}

function Fail-Guard { param([string] $Reason) throw "safe_move 차단: $Reason" }

$logDir = Join-Path $PSScriptRoot '..\..\..\logs\pcaway'
if (-not $InjectionLog) { $InjectionLog = Join-Path $logDir 'injections.jsonl' }
if (-not $FlagPath) { $FlagPath = Join-Path $logDir 'user_returned.flag' }

function Write-Injection {
    param([string] $Kind, $X, $Y, $AltX = $null, $AltY = $null)
    $line = [ordered]@{ t_ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); kind = $Kind; x = $X; y = $Y; alt_x = $AltX; alt_y = $AltY; source = 'safe_move' } | ConvertTo-Json -Compress
    $dir = Split-Path -Parent $InjectionLog
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::AppendAllText($InjectionLog, $line + "`n", [System.Text.UTF8Encoding]::new($false))
}

try { [void][PdSafeMoveNative]::SetProcessDpiAwareness(2) } catch { }
Add-Type -AssemblyName System.Windows.Forms

function Assert-Point {
    param([int] $X, [int] $Y, [string] $Label)
    $inside = $null
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { if ($s.Bounds.Contains($X, $Y)) { $inside = $s.Bounds; break } }
    if ($null -eq $inside) { Fail-Guard "$Label ($X,$Y) 는 어느 모니터에도 없다" }
    if ($X -lt $inside.Left + 100 -or $X -ge $inside.Right - 100 -or $Y -lt $inside.Top + 100 -or $Y -ge $inside.Bottom - 100) {
        Fail-Guard "$Label ($X,$Y) 는 화면 경계 100px 안이다"
    }
    $owner = [PdSafeMoveNative]::PidAt($X, $Y)
    if ($owner -ne [uint32]$ExpectedProcessId) { Fail-Guard "$Label ($X,$Y) 의 창 PID 불일치. 기대=$ExpectedProcessId 실제=$owner" }
}

if ($ExpectedProcessId -le 0) { Fail-Guard 'ExpectedProcessId 가 필요하다' }
if ($ForbiddenProcessId -contains $ExpectedProcessId) { Fail-Guard "입력 금지 PID 다: $ExpectedProcessId" }
if (Test-Path -LiteralPath $FlagPath) { Fail-Guard "사용자 복귀 플래그가 있다: $FlagPath" }
if ($Steps -lt 1) { Fail-Guard "Steps 는 1 이상이어야 한다: $Steps" }

$foreground = [PdSafeMoveNative]::ForegroundPid()
if ($foreground -ne [uint32]$ExpectedProcessId) { Fail-Guard "전경 PID 불일치. 기대=$ExpectedProcessId 실제=$foreground" }
Assert-Point $ScreenX $ScreenY '목표점'

function Send-Rel {
    param([int] $Dx, [int] $Dy)
    if ([PdSafeMoveNative]::ForegroundPid() -ne [uint32]$ExpectedProcessId) {
        Write-Injection 'move_failed' $null $null
        Fail-Guard "이동 중 전경이 바뀌었다. 현재 전경=$([PdSafeMoveNative]::ForegroundPid())"
    }
    if (Test-Path -LiteralPath $FlagPath) {
        Write-Injection 'move_failed' $null $null
        Fail-Guard "이동 중 사용자 복귀 플래그가 생겼다: $FlagPath"
    }
    if (-not [PdSafeMoveNative]::MoveRel($Dx, $Dy)) {
        $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Write-Injection 'move_failed' $null $null
        Fail-Guard "SendInput 실패. 오류=$code"
    }
}

$cursorBefore = New-Object PdSafeMoveNative+POINT
if (-not [PdSafeMoveNative]::GetCursorPos([ref] $cursorBefore)) { Fail-Guard 'GetCursorPos 실패' }
# 기록 실패 시 throw 하므로 주입 전에 먼저 남긴다. x,y 는 이동 뒤 기대 커서다
Write-Injection 'move_pre' $ScreenX $ScreenY $cursorBefore.X $cursorBefore.Y

# 단계 이동. 가속 때문에 실제 위치가 어긋나므로 매 단계 현재 위치 기준 남은 거리를 단계 수로 나눠 보낸다
for ($s = 1; $s -le $Steps; $s++) {
    $cur = New-Object PdSafeMoveNative+POINT
    [void][PdSafeMoveNative]::GetCursorPos([ref] $cur)
    $left = $Steps - $s + 1
    $dx = [int][Math]::Round(($ScreenX - $cur.X) / $left)
    $dy = [int][Math]::Round(($ScreenY - $cur.Y) / $left)
    if ($dx -ne 0 -or $dy -ne 0) { Send-Rel $dx $dy }
    Start-Sleep -Milliseconds 16
}

# 마지막 오차 보정. 1px 이하가 될 때까지 최대 10회
$cursor = New-Object PdSafeMoveNative+POINT
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Milliseconds 16
    [void][PdSafeMoveNative]::GetCursorPos([ref] $cursor)
    $ex = $ScreenX - $cursor.X
    $ey = $ScreenY - $cursor.Y
    if ([Math]::Abs($ex) -le 1 -and [Math]::Abs($ey) -le 1) { break }
    Send-Rel $ex $ey
}
[void][PdSafeMoveNative]::GetCursorPos([ref] $cursor)
$err = [Math]::Max([Math]::Abs($ScreenX - $cursor.X), [Math]::Abs($ScreenY - $cursor.Y))

Write-Injection 'move' $cursor.X $cursor.Y $cursorBefore.X $cursorBefore.Y
if ($err -gt 1) { Fail-Guard "보정 뒤에도 오차 ${err}px. 커서=($($cursor.X),$($cursor.Y)) 목표=($ScreenX,$ScreenY)" }
Write-Output ('{{"sent":true,"from":[{0},{1}],"to":[{2},{3}],"cursor":[{4},{5}],"error_px":{6},"foreground_pid":{7}}}' -f $cursorBefore.X, $cursorBefore.Y, $ScreenX, $ScreenY, $cursor.X, $cursor.Y, $err, $foreground)
exit 0
