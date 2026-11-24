# 전경 프로세스와 좌표 소유 창이 기대 PID 일 때만 수식키+휠을 보내고 주입 시각·입력 언어 변화를 남기는 휠 입력 가드
#
# 사용 예
#   .\safe_wheel.ps1 -ScreenX -1500 -ScreenY 500 -ExpectedProcessId 41312 -Modifiers Alt+Shift -Clicks 1
#   .\safe_wheel.ps1 -SelfCheck -Modifiers Alt+Shift -Clicks -1     # 입력 없이 전송 배열만 출력한다
#
# 규칙
#   - 전경 창의 PID 가 -ExpectedProcessId 와 같고 그 좌표의 창도 같은 PID 일 때만 보낸다. 다르면 throw 하고 아무것도 보내지 않는다.
#   - 수식키 누름·휠·수식키 뗌을 한 번의 SendInput 배열로 보낸다. 중간에 다른 입력이 끼지 않고 수식키가 눌린 채 남지 않는다.
#   - 수식키는 None·Ctrl·Alt·Shift·Alt+Shift 만 받는다. 키보드의 왼쪽 키(VK_LMENU 등)로 보낸다. 입력 언어 전환 단축키가 왼쪽 키 기준이라는 서술이 있어 실제 조작과 맞췄다.
#   - -ForbiddenProcessId 를 주지 않으면 retest_real_input.resolve_user_pid 와 같은 규칙으로 사용자 Pd 를 찾는다. 0개·2개 이상이면 보내지 않는다.
#   - 화면 경계 100px 안의 좌표는 거부한다.
#   - 전경을 가져오지 않는다. 전경 획득은 호출자 몫이다.
#   - 커서를 그 좌표로 옮기고 되돌리지 않는다. 끝 위치를 주입 기록에 남겨 복귀 감지 기준으로 쓴다.
#   - 주입 시각은 이 스크립트가 injections.jsonl 에 남긴다. 기록에 실패하면 보내지 않는다.
#   - 전송 전후 입력 언어(전경 스레드 HKL)를 읽어 결과에 남긴다. 바뀌었으면 시험 앱 창에 WM_INPUTLANGCHANGEREQUEST 로 되돌리고 다시 읽는다.
#   - logs/pcaway/user_returned.flag 가 있으면 보내지 않는다.
#   - 호출자는 exit code 로 판정한다(0=전송). 이 파일은 BOM 이 있어야 PowerShell 5.1 이 한글을 바르게 읽는다.
#
# 못 잡는 것
#   검사와 전송 사이 수 ms 안의 창 변경은 막지 못한다. 합성 입력이라 실제 마우스 드라이버의 고해상도 휠 경로는 재현하지 않는다.
#   입력 언어를 창마다 따로 쓰는 설정이 아니면 언어 전환은 시스템 전체에 걸리고, 되돌림 확인은 전경 스레드 값으로만 한다.

[CmdletBinding()]
param(
    [int] $ScreenX = 0,
    [int] $ScreenY = 0,
    [int] $ExpectedProcessId = 0,
    [string] $Modifiers = 'None',
    [int] $Clicks = 1,
    [int[]] $ForbiddenProcessId = @(),
    [string] $InjectionLog = '',
    [string] $FlagPath = '',
    [switch] $SelfCheck
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if (-not ('PdSafeWheelNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PdSafeWheelNative {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    // 64비트 INPUT: type 4바이트 + 맞춤 4바이트 + 공용체 32바이트
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetCursorPos(int x, int y);
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, MOUSEEVENTF_WHEEL = 0x0800, GA_ROOT = 2, WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const int WHEEL_DELTA = 120;

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

    static INPUT Key(ushort vk, bool up) {
        INPUT i = new INPUT();
        i.type = INPUT_KEYBOARD;
        i.ki.wVk = vk;
        i.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        return i;
    }

    // 수식키 누름 → 휠 → 수식키 뗌(역순)
    public static INPUT[] Build(ushort[] mods, int clicks) {
        INPUT[] seq = new INPUT[mods.Length * 2 + 1];
        for (int i = 0; i < mods.Length; i++) {
            seq[i] = Key(mods[i], false);
            seq[seq.Length - 1 - i] = Key(mods[i], true);
        }
        INPUT w = new INPUT();
        w.type = INPUT_MOUSE;
        w.mi.dwFlags = MOUSEEVENTF_WHEEL;
        w.mi.mouseData = unchecked((uint)(clicks * WHEEL_DELTA));
        seq[mods.Length] = w;
        return seq;
    }

    // 전송 직전 전경과 좌표 소유자를 한 번 더 본다. 다르면 0 을 돌려주고 보내지 않는다.
    public static uint Send(uint expectedPid, int x, int y, ushort[] mods, int clicks) {
        if (ForegroundPid() != expectedPid || PidAt(x, y) != expectedPid) return 0;
        INPUT[] seq = Build(mods, clicks);
        return SendInput((uint)seq.Length, seq, Marshal.SizeOf(typeof(INPUT)));
    }

    // 부분 전송 뒤에도 수식키가 눌린 채 남지 않게 뗌만 다시 보낸다
    public static void Release(ushort[] mods) {
        if (mods.Length == 0) return;
        INPUT[] seq = new INPUT[mods.Length];
        for (int i = 0; i < mods.Length; i++) seq[i] = Key(mods[mods.Length - 1 - i], true);
        SendInput((uint)seq.Length, seq, Marshal.SizeOf(typeof(INPUT)));
    }

    public static bool AnyDown(ushort[] mods) {
        foreach (ushort vk in mods) { if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true; }
        return false;
    }

    public static long ForegroundLayout() {
        uint pid;
        IntPtr h = GetForegroundWindow();
        uint thread = h == IntPtr.Zero ? 0 : GetWindowThreadProcessId(h, out pid);
        return GetKeyboardLayout(thread).ToInt64();
    }

    public static bool RequestLayout(long hkl) {
        IntPtr h = GetForegroundWindow();
        return h != IntPtr.Zero && PostMessage(h, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, new IntPtr(hkl));
    }

    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    public static int InstalledLayouts() { return GetKeyboardLayoutList(0, null); } // 읽기 전용, 설치된 입력 언어·자판 수
    public static int InputSize() { return Marshal.SizeOf(typeof(INPUT)); }
}
"@
}

# 자가검사가 사유를 가려낼 수 있게 대괄호 안에 영문 코드를 넣는다
function Fail-Guard { param([string] $Code, [string] $Reason) throw "safe_wheel 차단[$Code]: $Reason" }

$logDir = Join-Path $PSScriptRoot '..\..\..\logs\pcaway'
if (-not $InjectionLog) { $InjectionLog = Join-Path $logDir 'injections.jsonl' }
if (-not $FlagPath) { $FlagPath = Join-Path $logDir 'user_returned.flag' }

function Write-Injection {
    param([string] $Kind, $X, $Y)
    $line = [ordered]@{ t_ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); kind = $Kind; x = $X; y = $Y; source = 'safe_wheel'; modifiers = $Modifiers; clicks = $Clicks } | ConvertTo-Json -Compress
    $dir = Split-Path -Parent $InjectionLog
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::AppendAllText($InjectionLog, $line + "`n", [System.Text.UTF8Encoding]::new($false))
}

$modTable = @{ 'None' = @(); 'Ctrl' = @(0xA2); 'Alt' = @(0xA4); 'Shift' = @(0xA0); 'Alt+Shift' = @(0xA4, 0xA0) }
if (-not $modTable.ContainsKey($Modifiers)) { Fail-Guard 'modifiers' "지원하지 않는 수식키: '$Modifiers'" }
[uint16[]] $mods = @($modTable[$Modifiers] | ForEach-Object { [uint16]$_ })
if ($null -eq $mods) { [uint16[]] $mods = @() }
if ($Clicks -ne 1 -and $Clicks -ne -1) { Fail-Guard 'clicks' "Clicks 는 1 또는 -1 이어야 한다: $Clicks" }
if ([PdSafeWheelNative]::InputSize() -ne 40) { Fail-Guard 'input_size' ("INPUT 크기가 40 이 아니다: {0}" -f [PdSafeWheelNative]::InputSize()) }

if ($SelfCheck) {
    $rows = foreach ($i in [PdSafeWheelNative]::Build($mods, $Clicks)) {
        if ($i.type -eq 1) { [ordered]@{ t = 'key'; vk = [int]$i.ki.wVk; up = (($i.ki.dwFlags -band 2) -ne 0) } }
        else { [ordered]@{ t = 'wheel'; data = [long]$i.mi.mouseData; flags = [int]$i.mi.dwFlags } }
    }
    Write-Output ([ordered]@{ input_size = [PdSafeWheelNative]::InputSize(); layouts_installed = [PdSafeWheelNative]::InstalledLayouts(); seq = @($rows) } | ConvertTo-Json -Compress -Depth 4)
    exit 0
}

if ($ExpectedProcessId -le 0) { Fail-Guard 'expected' 'ExpectedProcessId 가 필요하다' }
if ($ForbiddenProcessId.Count -eq 0) {
    # retest_real_input.pick_user_pid 와 같은 규칙: 사용자 실행 경로이고 --data-dir 가 없는 프로세스가 정확히 1개
    $found = @(Get-CimInstance Win32_Process -Filter "Name='Pd.Poc.Desktop.exe'" | Where-Object { ([string]$_.ExecutablePath).ToLowerInvariant().Contains('\bin\run\otest_final\') -and -not ([string]$_.CommandLine).Contains('--data-dir') } | ForEach-Object { [int]$_.ProcessId })
    if ($found.Count -ne 1) { Fail-Guard 'user_pid' ("사용자 Pd 를 확정하지 못했다. 찾은 PID=[{0}]" -f ($found -join ',')) }
    $ForbiddenProcessId = $found
}
if ($ForbiddenProcessId -contains $ExpectedProcessId) { Fail-Guard 'forbidden' "입력 금지 PID 다: $ExpectedProcessId" }
if (Test-Path -LiteralPath $FlagPath) { Fail-Guard 'flag' "사용자 복귀 플래그가 있다: $FlagPath" }

try { [void][PdSafeWheelNative]::SetProcessDpiAwareness(2) } catch { }
Add-Type -AssemblyName System.Windows.Forms

$foreground = [PdSafeWheelNative]::ForegroundPid()
if ($foreground -ne [uint32]$ExpectedProcessId) { Fail-Guard 'foreground' "전경 PID 불일치. 기대=$ExpectedProcessId 실제=$foreground" }

$inside = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { if ($s.Bounds.Contains($ScreenX, $ScreenY)) { $inside = $s.Bounds; break } }
if ($null -eq $inside) { Fail-Guard 'point' "($ScreenX,$ScreenY) 는 어느 모니터에도 없다" }
if ($ScreenX -lt $inside.Left + 100 -or $ScreenX -ge $inside.Right - 100 -or $ScreenY -lt $inside.Top + 100 -or $ScreenY -ge $inside.Bottom - 100) {
    Fail-Guard 'edge' "($ScreenX,$ScreenY) 는 화면 경계 100px 안이다"
}
$owner = [PdSafeWheelNative]::PidAt($ScreenX, $ScreenY)
if ($owner -ne [uint32]$ExpectedProcessId) { Fail-Guard 'owner' "($ScreenX,$ScreenY) 의 창 PID 불일치. 기대=$ExpectedProcessId 실제=$owner" }

$layoutBefore = [PdSafeWheelNative]::ForegroundLayout()
Write-Injection 'wheel_pre' $null $null
if (-not [PdSafeWheelNative]::SetCursorPos($ScreenX, $ScreenY)) {
    Write-Injection 'wheel_failed' $null $null
    Fail-Guard 'cursor' 'SetCursorPos 실패'
}
Start-Sleep -Milliseconds 60

$expected = [uint32]($mods.Length * 2 + 1)
$sent = [PdSafeWheelNative]::Send([uint32]$ExpectedProcessId, $ScreenX, $ScreenY, $mods, $Clicks)
if ($sent -ne $expected) {
    if ($sent -gt 0) { [PdSafeWheelNative]::Release($mods) }
    Write-Injection 'wheel_failed' $ScreenX $ScreenY
    Fail-Guard 'send' "전송 실패 또는 전송 직전 창 변경. 전송=$sent 기대=$expected 현재 전경=$([PdSafeWheelNative]::ForegroundPid())"
}
Write-Injection 'wheel' $ScreenX $ScreenY

# 언어 전환은 수식키를 뗀 뒤 비동기로 일어나므로 잠시 뒤에 읽는다
Start-Sleep -Milliseconds 400
$layoutAfter = [PdSafeWheelNative]::ForegroundLayout()
$changed = $layoutAfter -ne $layoutBefore
$layoutFinal = $layoutAfter
$restored = $null
if ($changed) {
    $restored = $false
    if ([PdSafeWheelNative]::ForegroundPid() -eq [uint32]$ExpectedProcessId) {
        [void][PdSafeWheelNative]::RequestLayout($layoutBefore)
        Write-Injection 'wheel_lang_restore' $ScreenX $ScreenY
        Start-Sleep -Milliseconds 400
        $layoutFinal = [PdSafeWheelNative]::ForegroundLayout()
        $restored = $layoutFinal -eq $layoutBefore
    }
}

$released = -not [PdSafeWheelNative]::AnyDown($mods)
Write-Output ([ordered]@{
    sent = $true; modifiers = $Modifiers; clicks = $Clicks; x = $ScreenX; y = $ScreenY; foreground_pid = $foreground
    layout_before = ('0x{0:X}' -f $layoutBefore); layout_after = ('0x{0:X}' -f $layoutAfter); layout_final = ('0x{0:X}' -f $layoutFinal)
    layout_changed = $changed; layout_restored = $restored; modifiers_released = $released; layouts_installed = [PdSafeWheelNative]::InstalledLayouts()
} | ConvertTo-Json -Compress)
exit 0
