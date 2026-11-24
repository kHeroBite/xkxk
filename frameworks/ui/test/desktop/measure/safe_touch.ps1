# 전경 프로세스와 좌표 소유 창이 기대 PID 일 때만 터치를 주입하고 주입 시각을 남기는 터치 입력 가드
#
# 사용 예
#   .\safe_touch.ps1 -ScreenX -1500 -ScreenY 500 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#   .\safe_touch.ps1 -ScreenX -1500 -ScreenY 500 -ToX -1200 -ToY 500 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#   .\safe_touch.ps1 -ScreenX -1500 -ScreenY 500 -HoldMs 800 -ExpectedProcessId 41312 -ForbiddenProcessId 352
#
# 규칙
#   - 전경 창의 PID 가 -ExpectedProcessId 와 같고 시작점·끝점의 창도 같은 PID 일 때만 주입한다. 다르면 throw 한다.
#   - 프레임마다 전경을 다시 본다. 중간에 바뀌면 접촉을 취소하고 throw 한다.
#   - 화면 경계 100px 안의 좌표는 거부한다. 가장자리 터치는 OS 제스처로 빠질 수 있다.
#   - 전경을 가져오지 않는다. 전경 획득은 호출자 몫이다.
#   - 주입 시각은 이 스크립트가 injections.jsonl 에 남긴다. 기록에 실패하면 주입하지 않는다.
#   - 커서는 앱이 터치를 마우스로 승격할 때만 늦게 옮겨 간다(실측). 끝점과 주입 전 커서를 함께 복귀 감지 기준으로 남긴다.
#   - logs/pcaway/user_returned.flag 가 있으면 주입하지 않는다.
#   - 호출자는 exit code 로 판정한다(0=주입). 이 파일은 BOM 이 있어야 PowerShell 5.1 이 한글을 바르게 읽는다.
#
# 못 잡는 것
#   검사와 주입 사이 수 ms 안의 창 변경은 막지 못한다. 실제 터치 하드웨어 경로와 드라이버 차이는 재현하지 않는다.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [int] $ScreenX,
    [Parameter(Mandatory = $true)] [int] $ScreenY,
    [Parameter(Mandatory = $true)] [int] $ExpectedProcessId,
    [int] $ToX = [int]::MinValue,
    [int] $ToY = [int]::MinValue,
    [int] $HoldMs = 0,
    [int] $Steps = 20,
    # 입력 금지 PID(그때의 사용자 Pd). 기본값이 없으며 빠지거나 1 미만이면 입력 전에 거부한다.
    [Parameter(Mandatory = $true)] [ValidateRange(1, [int]::MaxValue)] [int[]] $ForbiddenProcessId,
    [string] $InjectionLog = '',
    [string] $FlagPath = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if (-not ('PdSafeTouchNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PdSafeTouchNative {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO {
        public uint pointerType; public uint pointerId; public uint frameId; public uint pointerFlags;
        public IntPtr sourceDevice; public IntPtr hwndTarget;
        public POINT ptPixelLocation; public POINT ptHimetricLocation; public POINT ptPixelLocationRaw; public POINT ptHimetricLocationRaw;
        public uint dwTime; public uint historyCount; public int InputData; public uint dwKeyStates;
        public ulong PerformanceCount; public int ButtonChangeType;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO {
        public POINTER_INFO pointerInfo; public uint touchFlags; public uint touchMask;
        public RECT rcContact; public RECT rcContactRaw; public uint orientation; public uint pressure;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool InitializeTouchInjection(uint maxCount, uint mode);
    [DllImport("user32.dll", SetLastError = true)] static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] info);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);

    public const uint FLAG_INRANGE = 0x2, FLAG_INCONTACT = 0x4, FLAG_CANCELED = 0x8000;
    public const uint FLAG_DOWN = 0x10000, FLAG_UPDATE = 0x20000, FLAG_UP = 0x40000;
    const uint PT_TOUCH = 2, TOUCH_MASK_ALL = 0x7, GA_ROOT = 2;

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

    public static bool Inject(int x, int y, uint flags) {
        POINTER_TOUCH_INFO[] t = new POINTER_TOUCH_INFO[1];
        t[0].pointerInfo.pointerType = PT_TOUCH;
        t[0].pointerInfo.pointerFlags = flags;
        t[0].pointerInfo.ptPixelLocation.X = x;
        t[0].pointerInfo.ptPixelLocation.Y = y;
        t[0].touchMask = TOUCH_MASK_ALL;
        t[0].orientation = 90;
        t[0].pressure = 32000;
        t[0].rcContact.Left = x - 2; t[0].rcContact.Right = x + 2;
        t[0].rcContact.Top = y - 2; t[0].rcContact.Bottom = y + 2;
        return InjectTouchInput(1, t);
    }
}
"@
}

function Fail-Guard { param([string] $Reason) throw "safe_touch 차단: $Reason" }

$logDir = Join-Path $PSScriptRoot '..\..\..\logs\pcaway'
if (-not $InjectionLog) { $InjectionLog = Join-Path $logDir 'injections.jsonl' }
if (-not $FlagPath) { $FlagPath = Join-Path $logDir 'user_returned.flag' }

function Write-Injection {
    param([string] $Kind, $X, $Y, $AltX = $null, $AltY = $null)
    $line = [ordered]@{ t_ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); kind = $Kind; x = $X; y = $Y; alt_x = $AltX; alt_y = $AltY; source = 'safe_touch' } | ConvertTo-Json -Compress
    $dir = Split-Path -Parent $InjectionLog
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::AppendAllText($InjectionLog, $line + "`n", [System.Text.UTF8Encoding]::new($false))
}

try { [void][PdSafeTouchNative]::SetProcessDpiAwareness(2) } catch { }
Add-Type -AssemblyName System.Windows.Forms

function Assert-Point {
    param([int] $X, [int] $Y, [string] $Label)
    $inside = $null
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { if ($s.Bounds.Contains($X, $Y)) { $inside = $s.Bounds; break } }
    if ($null -eq $inside) { Fail-Guard "$Label ($X,$Y) 는 어느 모니터에도 없다" }
    if ($X -lt $inside.Left + 100 -or $X -ge $inside.Right - 100 -or $Y -lt $inside.Top + 100 -or $Y -ge $inside.Bottom - 100) {
        Fail-Guard "$Label ($X,$Y) 는 화면 경계 100px 안이다"
    }
    $owner = [PdSafeTouchNative]::PidAt($X, $Y)
    if ($owner -ne [uint32]$ExpectedProcessId) { Fail-Guard "$Label ($X,$Y) 의 창 PID 불일치. 기대=$ExpectedProcessId 실제=$owner" }
}

if ($ExpectedProcessId -le 0) { Fail-Guard 'ExpectedProcessId 가 필요하다' }
if ($ForbiddenProcessId -contains $ExpectedProcessId) { Fail-Guard "입력 금지 PID 다: $ExpectedProcessId" }
if (Test-Path -LiteralPath $FlagPath) { Fail-Guard "사용자 복귀 플래그가 있다: $FlagPath" }

$isDrag = ($ToX -ne [int]::MinValue) -and ($ToY -ne [int]::MinValue)
if (($ToX -ne [int]::MinValue) -ne ($ToY -ne [int]::MinValue)) { Fail-Guard 'ToX 와 ToY 는 함께 지정해야 한다' }
if ($Steps -lt 1) { Fail-Guard "Steps 는 1 이상이어야 한다: $Steps" }

$foreground = [PdSafeTouchNative]::ForegroundPid()
if ($foreground -ne [uint32]$ExpectedProcessId) { Fail-Guard "전경 PID 불일치. 기대=$ExpectedProcessId 실제=$foreground" }
Assert-Point $ScreenX $ScreenY '시작점'
if ($isDrag) { Assert-Point $ToX $ToY '끝점' }

# TOUCH_FEEDBACK_NONE = 3. 화면에 터치 표시를 그리지 않는다
if (-not [PdSafeTouchNative]::InitializeTouchInjection(1, 3)) { Fail-Guard ("InitializeTouchInjection 실패. 오류={0}" -f [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }

$contactFlags = [PdSafeTouchNative]::FLAG_INRANGE -bor [PdSafeTouchNative]::FLAG_INCONTACT
$lastX = $ScreenX
$lastY = $ScreenY
$script:inContact = $false

function Send-Frame {
    param([int] $X, [int] $Y, [uint32] $Flags)
    if ([PdSafeTouchNative]::ForegroundPid() -ne [uint32]$ExpectedProcessId) {
        if ($script:inContact) { [void][PdSafeTouchNative]::Inject($lastX, $lastY, [PdSafeTouchNative]::FLAG_UP -bor [PdSafeTouchNative]::FLAG_CANCELED) }
        Write-Injection 'touch_failed' $null $null
        Fail-Guard "주입 중 전경이 바뀌었다. 현재 전경=$([PdSafeTouchNative]::ForegroundPid())"
    }
    if (-not [PdSafeTouchNative]::Inject($X, $Y, $Flags)) {
        $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($script:inContact) { [void][PdSafeTouchNative]::Inject($lastX, $lastY, [PdSafeTouchNative]::FLAG_UP -bor [PdSafeTouchNative]::FLAG_CANCELED) }
        Write-Injection 'touch_failed' $null $null
        Fail-Guard "InjectTouchInput 실패. 오류=$code 좌표=($X,$Y)"
    }
}

$cursorBefore = New-Object PdSafeTouchNative+POINT
if (-not [PdSafeTouchNative]::GetCursorPos([ref] $cursorBefore)) { Fail-Guard 'GetCursorPos 실패' }
Write-Injection 'touch_pre' $null $null
Send-Frame $ScreenX $ScreenY ([PdSafeTouchNative]::FLAG_DOWN -bor $contactFlags)
$script:inContact = $true

$waited = 0
while ($waited -lt $HoldMs) {
    Start-Sleep -Milliseconds 50
    $waited += 50
    Send-Frame $lastX $lastY ([PdSafeTouchNative]::FLAG_UPDATE -bor $contactFlags)
}
if ($isDrag) {
    for ($s = 1; $s -le $Steps; $s++) {
        $ix = [int]($ScreenX + ($ToX - $ScreenX) * $s / $Steps)
        $iy = [int]($ScreenY + ($ToY - $ScreenY) * $s / $Steps)
        Send-Frame $ix $iy ([PdSafeTouchNative]::FLAG_UPDATE -bor $contactFlags)
        $lastX = $ix
        $lastY = $iy
        Start-Sleep -Milliseconds 16
    }
}
Send-Frame $lastX $lastY ([PdSafeTouchNative]::FLAG_UP)
$script:inContact = $false

Start-Sleep -Milliseconds 150
$cursor = New-Object PdSafeTouchNative+POINT
[void][PdSafeTouchNative]::GetCursorPos([ref] $cursor)
Write-Injection 'touch' $lastX $lastY $cursorBefore.X $cursorBefore.Y
Write-Output ('{{"sent":true,"from":[{0},{1}],"to":[{2},{3}],"hold_ms":{4},"cursor":[{5},{6}],"foreground_pid":{7}}}' -f $ScreenX, $ScreenY, $lastX, $lastY, $HoldMs, $cursor.X, $cursor.Y, $foreground)
exit 0
