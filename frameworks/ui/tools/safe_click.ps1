# 물리 클릭 전 좌표계·창소유권·DPI 3축을 검증하는 가드
# 출처: Pd 저장소 Pd:.claude/scripts/safe_click.ps1(기준 커밋 867b432, 파일 최종 커밋 611e1f9)를 2026-11-24 그대로 복사(UTODO-0001 계열, K14). 본문 무변경
#
# ============================================================================
# 이 가드가 ★못 잡는 것★ (사이클40 A2 교훈 — 게이트의 사각지대를 명시한다)
# ============================================================================
# 1. 검증과 클릭 사이의 race
#    검증 3축을 전부 통과한 뒤 SendInput 을 쏘기까지 수 ms 의 간극이 있다.
#    그 사이 대상 창이 이동/닫힘/z-order 변경되면 엉뚱한 창을 클릭한다.
#    ⇒ 이 가드는 그 간극을 좁힐 뿐 없애지 못한다. 완전 차단 불가.
#
# 2. 투명 창 / 자식 컨트롤
#    WindowFromPoint 는 WS_EX_TRANSPARENT 창을 건너뛰고, 자식 컨트롤 대신
#    최상위 부모 핸들을 반환할 수 있다. 따라서 "PID 가 맞다" 는
#    "그 컨트롤이 맞다" 를 의미하지 않는다. 컨트롤 단위 확정이 필요하면
#    UIAutomation(AutomationElement.FromPoint)을 별도로 써야 한다.
#
# 3. ExpectedProcessId 를 넘기지 않은 경우
#    ② 창 소유권 검증이 통째로 스킵된다. 이때 남는 방어선은 ①(모니터 멤버십)
#    과 ③(DPI 사후 검증)뿐이며, 둘 다 "좌표가 유효한가" 만 본다.
#    ⇒ ★올바른 좌표로 엉뚱한 창을 클릭하는 사고는 막지 못한다.★
#    가능하면 항상 -ExpectedProcessId 를 넘겨라.
#
# 4. 멀티 모니터 재구성 중
#    VirtualScreen / Screen.AllScreens 는 스크립트 진입 시점의 스냅샷이다.
#    검증 도중 모니터가 추가/제거/해상도 변경되면 판정 근거가 낡는다.
#
# 5. 좌표는 맞지만 대상이 논리적으로 틀린 경우
#    "클릭 가능한 좌표인가" 만 판정한다. 그 좌표에 있는 버튼이 사용자가
#    의도한 버튼인지는 판정 범위 밖이다.
#
# 6. 클릭 자체의 부작용
#    이 스크립트는 클릭 전 검증만 한다. 클릭 후 앱이 어떤 상태가 됐는지는
#    호출자가 별도로 확인해야 한다.
#
# 7. ★이 파일이 BOM 없이 저장된 경우★ — 가드가 통째로 사라진다
#    Windows PowerShell 5.1 은 BOM 없는 한글 파일을 ANSI 로 오해석해 ParserError 를
#    내고 ★스크립트가 아예 실행되지 않는다★. 그러면 호출자가 rc 를 확인하지 않는 한
#    "검증을 거쳤다" 고 착각한 채 클릭만 나갈 수 있다.
#    ⇒ ★가드의 부재가 가드의 통과처럼 보이는★ 가장 위험한 형태다.
#    ⇒ 방어: 호출자는 반드시 ★exit code★ 로 판정하라(0=통과). 상세는 아래 인코딩 절.
#
# 8. ★Tolerance 기본 4px 가 놓치는 것★ (완화의 대가 — 명시한다)
#    ±4px 이내의 좌표 이탈은 통과한다. 따라서 ★클릭 대상이 4px 이하로 작거나★,
#    ★버튼 경계에서 4px 이내에 다른 컨트롤이 붙어 있으면★ 인접 컨트롤을 누를 수 있다.
#    실측상 DPI 잔차는 ±2px 였고 ③이 잡으려는 실패(수십~수백px)와는 자릿수가 다르므로
#    4px 를 택했다. 픽셀 단위 정밀도가 필요하면 ★-Tolerance 0★ 을 명시하라.
#
# 9. ★④를 지정해도 "탭"까지는 보장되지 않는다★
#    ④는 ★최상위 창 제목의 부분 일치★ 다. 탭을 바꿔도 제목이 그대로인 앱에서는
#    ★아무것도 걸러내지 못한다★. 실측(사이클41): activate 가 성공 응답을 냈는데
#    화면은 대시보드 탭이었고 ②(PID)는 통과했다.
#    ⇒ 폼/탭 단위 확정이 필요하면 ★UIAutomation 또는 앱 자체 상태 조회★ 를 병행하라.
#    ⇒ 이 스크립트는 ★특정 앱의 REST 에 의존하지 않기 위해★ 그 축을 일부러 넣지 않았다.
#
# 10. ★사람이 마우스를 만지고 있으면 ③이 실패한다★ (오탐이 아니라 정상 동작)
#     SetCursorPos 로 옮긴 직후 ★실제 마우스 입력★ 이 들어오면 GetCursorPos 가
#     다른 좌표를 읽어 ③이 throw 한다.
#     실측(사이클41): 목표(3040,500) → 실제(2770,426), 오차 270px — 사용자가
#     같은 시각에 마우스를 움직이고 있었다. 20회 중 3회 발생.
#     ⇒ ★이건 가드가 틀린 게 아니라 "지금 클릭하면 안 되는 상태" 를 잡은 것이다.★
#       사람이 쓰는 중인 화면에 자동 클릭을 쏘면 실제로 엉뚱한 곳을 누른다.
#     ⇒ 호출자 대응: ★실패 시 재시도★ 하되, ★연속 실패하면 사람이 쓰는 중★ 으로
#       판단하고 중단하라. Tolerance 를 올려서 뚫는 것은 ★잘못된 대응★ 이다.
#
# ============================================================================
# ★실측 근거★ (2026-08-26 이 PC 에서 측정 — 하드코딩하지 말 것, 재측정 대상)
# ============================================================================
# 모니터 구성:
#   DISPLAY2 (Primary) X=0     Y=0   W=2560 H=1440
#   DISPLAY3           X=-2560 Y=5   W=2560 H=1440
#   DISPLAY1           X=2560  Y=244 W=1920 H=1200
#   VirtualScreen      X=-2560 Y=0   W=7040 H=1445  (Right=4480)
#
# ★핵심★ — VirtualScreen 은 ★모니터들의 바운딩 박스★ 이지 유효 영역이 아니다.
#   VirtualScreen 안이지만 어떤 모니터에도 속하지 않는 ★구멍(hole)★ 이 존재한다.
#   실측: (3040,100) 은 VirtualScreen 안(X<4480)이지만 DISPLAY1 의 Y 범위
#         (244~1443) 밖이라 ★어느 모니터에도 없다★ → 커서가 (3040,244)로 클램프됐다.
#   ⇒ ★VirtualScreen 범위 검사만으로는 불충분하다.★
#     그래서 ①은 VirtualScreen 이 아니라 ★Screen.AllScreens 멤버십★ 으로 판정한다.
#
# 클램프 실측 (SetCursorPos 후 GetCursorPos):
#   TARGET 5600,500 -> ACTUAL 2559,500  [CLAMPED]
#   TARGET 3040,500 -> ACTUAL 3040,500  [OK]      (DISPLAY1 안)
#   TARGET 4479,500 -> ACTUAL 4479,500  [OK]      (DISPLAY1 우측 끝)
#   TARGET 3040,100 -> ACTUAL 3040,244  [CLAMPED] (구멍 → Y 클램프)
#   TARGET 3040,243 -> ACTUAL 3040,244  [CLAMPED]
#   TARGET 3040,244 -> ACTUAL 3040,244  [OK]      (경계 정확히)
#
# ============================================================================
# ★인코딩 요구사항★ — 이 파일은 ★UTF-8 with BOM★ 이어야 한다 (실측 근거 있음)
# ============================================================================
# Windows PowerShell 5.1 은 BOM 없는 파일을 ★ANSI 코드페이지★ 로 읽는다.
# 그 결과 한글 주석 바이트가 오해석되어 ★뒤따르는 코드 줄이 통째로 사라진다★.
#   A/B 실측 (2026-08-26):
#     BOM 없음 : '# 한글 주석' + '$x = 5' + 'Write-Output "ok $x"'  →  "ok "   ($x 소실)
#     BOM 있음 : 동일 내용                                          →  "ok 5"  (정상)
#   실제로 이 스크립트도 BOM 없이 저장했을 때 ParserError 로 아예 실행되지 않았다.
#
# ⚠ L-148 과 모순이 아니다.
#   L-148 이 금지하는 것은 `Out-File`/`Set-Content -Encoding UTF8` 이며,
#   그 이유는 BOM 이 ★의도치 않게 부작용으로★ 붙기 때문이다.
#   여기서는 BOM 이 ★실측으로 확인된 필수 요구사항★ 이므로 명시적으로 붙인다.
#   ⇒ ★이 파일을 수정할 때 BOM 을 제거하지 마라. 제거하면 스크립트가 죽는다.★
#   ⇒ 검증: head -c 3 safe_click.ps1 | xxd   →   efbb bf 가 나와야 한다.
#

# ⚠ 가설 "5600 → X≈4479 로 클램프(=Mars 닫기 버튼 근처)" 는 ★반증됐다★.
#   실제 클램프는 ★X=2559★ (Primary DISPLAY2 의 우측 끝) 였다. 4479 가 아니다.
#   y=500 (DISPLAY1 이 실제로 덮는 Y) 에서도 5600 → 2559 로 동일했다.
#   ⇒ Windows 는 VirtualScreen 우변으로 클램프하지 않는다.
#   ⇒ ★"닫기(X) 버튼 근처 9px" 추론은 성립하지 않는다.★ 2559 는 Mars 창
#     (X 2552~4488) 의 ★좌측 끝 부근★ 이며 닫기 버튼과 무관하다.
#   ⇒ 클램프 지점은 예측 불가하다. 예측하지 말고 ③ 사후 검증으로 ★측정★하라.
#
# ============================================================================
# 사용 예
# ============================================================================
#   # 이미지 좌표(캡처 기준)를 스크린 좌표로 변환해 클릭
#   .\safe_click.ps1 -ImageX 5600 -ImageY 500 -ExpectedProcessId 41312
#
#   # 이미 스크린 좌표를 알고 있을 때
#   .\safe_click.ps1 -ScreenX 3040 -ScreenY 500 -ExpectedProcessId 41312
#
#   # 검증만 하고 클릭은 하지 않는다 (dry-run)
#   .\safe_click.ps1 -ScreenX 3040 -ScreenY 500 -NoClick
# ============================================================================

[CmdletBinding()]
param(
    # 이미지(캡처) 좌표. 지정 시 VirtualScreen.X 를 더해 스크린 좌표로 변환한다.
    [int] $ImageX = [int]::MinValue,
    [int] $ImageY = [int]::MinValue,

    # 스크린(가상 데스크톱) 좌표. ImageX/ImageY 와 배타적으로 사용한다.
    [int] $ScreenX = [int]::MinValue,
    [int] $ScreenY = [int]::MinValue,

    # 기대 프로세스 ID. 지정하면 ② 창 소유권 검증을 수행한다.
    # ⚠ 미지정 시 ②가 스킵된다 — 사각지대 3번 참조.
    [int] $ExpectedProcessId = 0,

    # 검증만 수행하고 클릭하지 않는다.
    [switch] $NoClick,

    # ★더블클릭★ — 한 프로세스 안에서 2연타를 수행한다(3축 가드는 동일하게 통과해야 한다).
    #   safe_click 을 두 번 호출하는 방식은 프로세스 기동 지연으로 더블클릭이 성립하지 않는다.
    [switch] $DoubleClick,

    # ★휠 스크롤★ — 클릭 대신 휠을 보낸다. 3축 가드는 동일하게 통과해야 한다.
    #   WheelDelta: 양수=위/앞, 음수=아래/뒤 (120 단위). WheelTimes: 반복 횟수.
    #   ModifierKey: 'Shift'/'Ctrl'/'Alt' 지정 시 휠·드래그 동안 해당 키를 누른 상태로 유지한다
    #   (간트 Shift+세로휠 좌우스크롤 검증 — 사이클60 R1,
    #    간트 Alt+휠 행높이 축소 검증 — 사이클88 R9).
    #   ★[2026-09-06 사이클88] 'Alt' 추가★ — 종전 Shift/Ctrl 만 지원해 Alt+휠 경로를
    #     safe_click 으로 검증할 수 없었고, 그 때문에 가드를 우회하는 별도 스크립트를
    #     쓰려는 압력이 생겼다(otest-2 실측). 가드 안에서 처리하는 편이 안전하다.
    [int] $WheelDelta = 0,
    [int] $WheelTimes = 1,
    [ValidateSet('','Shift','Ctrl','Alt')]
    [string] $ModifierKey = '',

    # ★드래그★ — 시작점(위 좌표)에서 버튼을 누른 채 (DragToX, DragToY) 로 이동 후 뗀다.
    #   ★[2026-09-06 사이클88 신설]★ 간트 Ctrl+드래그 가상프로젝트 생성처럼 ★드래그가
    #     유일한 진입로★ 인 UI 가 실재한다. 종전에는 그런 경로를 검증할 수단이 아예 없어
    #     SetCursorPos/mouse_event 를 직접 부르는 우회가 유도됐다(F10-3 이 차단하므로
    #     결국 검증 자체가 포기됐다). ⇒ 가드 안으로 들여온다.
    #   ⚠ 3축 가드는 ★시작점 기준으로 종전과 동일하게 전부 수행★ 하며 약화하지 않는다.
    #     종료점도 같은 가드(① 모니터 멤버십 · ② 창 소유권)를 통과해야 한다 —
    #     드래그가 다른 창으로 빠져나가면 그 창에 드롭되기 때문이다.
    #   ⚠ down→이동→up 은 ★한 프로세스 안에서★ 수행한다(더블클릭과 같은 이유).
    [int] $DragToX = [int]::MinValue,
    [int] $DragToY = [int]::MinValue,

    # ③ DPI 사후 검증에서 허용할 오차(픽셀).
    # ★기본 4px★ — 실측상 SetProcessDPIAware 이후에도 ±2px 잔차가 실재한다
    #   (사이클41: 목표(2653,492) → 실제(2655,490)). ③의 목적은 DPI 스케일링으로
    #   ★수십~수백px★ 튀는 것을 잡는 것이므로 ±2px 는 그 실패 모드가 아니다.
    #   0 으로 두면 정상 클릭이 상시 차단된다. 엄격 검증이 필요하면 -Tolerance 0.
    [int] $Tolerance = 4,

    # 클릭 버튼. 기본 Left — 기존 호출부 무영향.
    #   헤더 컨텍스트 메뉴 등 ★우클릭 전용★ UI 가 실재한다
    #   (util.DataGridView.cs:917  if (e.Button == MouseButtons.Right)).
    [ValidateSet('Left','Right','Middle')]
    [string] $Button = 'Left',

    # ★가드 ④(선택)★ — 클릭 대상 최상위 창의 제목에 이 문자열이 포함되는지 확인한다.
    #   ②(PID)는 "창이 맞는가" 만 본다. ★같은 프로세스 안에서 다른 탭/폼이 앞에 있어도 통과한다.★
    #   실측(사이클41): /api/window/activate 가 성공 응답을 냈는데 화면은 대시보드 탭이었고,
    #   그 상태의 우클릭이 ★대시보드의 메뉴★ 를 띄웠다. ②는 PID 가 같아 통과했다.
    #   ⇒ 앱 종속 REST 에 의존하지 않으면서 그 오인을 줄이는 절충이 이 파라미터다.
    #   ⚠ 부분 일치(대소문자 무시)다. 탭 전환이 제목에 반영되지 않는 앱에서는 무력하다.
    [string] $ExpectedWindowTitle = "",

    # ★주입 기록(D-6/D-7, 2026-11-25)★ — PC 비움 감시기(user_away_guard·pc_away_monitor)가 시험 자신의 입력을
    #   사용자 입력으로 오인하지 않게 safe_wheel.ps1 과 같은 형식(t_ms·kind·x·y·source)으로 injections.jsonl 에 남긴다.
    #   비우면 영역 logs/pcaway/injections.jsonl. 기록에 실패하면 입력을 보내지 않는다(safe_wheel 과 같음).
    [string] $InjectionLog = ''
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Win32 P/Invoke
# ---------------------------------------------------------------------------
if (-not ('SafeClickNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public class SafeClickNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

    // DPI 인식: SHCore(Win8.1+) 우선, 실패 시 user32 폴백
    [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int value);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    public const uint MOUSEEVENTF_MOVE     = 0x0001;   // ★[사이클88] 드래그 중 이동 통지용
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP   = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    // ★휠★ — Shift+휠 등 수정키 조합 스크롤 검증용(사이클60 R1).
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const byte VK_SHIFT = 0x10;
    public const byte VK_CONTROL = 0x11;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint GA_ROOT = 2;

    // ★[2026-09-06 사이클88] SendInput + 스캔코드 수식키★
    //   keybd_event(VK) 로 내린 Ctrl/Alt 가 WinForms 의 ModifierKeys(=GetKeyState 기반)에
    //   반영되지 않는 사례가 실측됐다(간트 Ctrl+드래그 미발동). 스캔코드 기반 SendInput 은
    //   하드웨어 입력에 더 가깝게 처리되어 키 상태가 확실히 갱신된다.
    //   Ctrl 스캔코드 = 0x1D, Alt(=MENU) = 0x38.
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const ushort SC_CONTROL = 0x1D;
    public const ushort SC_MENU    = 0x38;
    public const ushort SC_SHIFT   = 0x2A;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT_KB { public uint type; public KEYBDINPUT ki; public int pad1; public int pad2; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT_KB[] p, int cb);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

    /// <summary>스캔코드 기반으로 수식키를 누르거나 뗀다. 성공 시 SendInput 반환값(1) 을 돌려준다.</summary>
    public static uint SendModifierScan(ushort scan, bool up) {
        INPUT_KB[] inp = new INPUT_KB[1];
        inp[0].type = 1; // INPUT_KEYBOARD
        inp[0].ki.wVk = 0;
        inp[0].ki.wScan = scan;
        inp[0].ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
        inp[0].ki.time = 0;
        inp[0].ki.dwExtraInfo = IntPtr.Zero;
        return SendInput(1, inp, Marshal.SizeOf(typeof(INPUT_KB)));
    }
}
"@
}

function Write-Step {
    param([string] $Mark, [string] $Text)
    Write-Host ("[safe_click] {0} {1}" -f $Mark, $Text)
}

function Fail-Guard {
    param([string] $Axis, [string] $Reason)
    Write-Step 'X' ("{0} 실패 — {1}" -f $Axis, $Reason)
    throw ("safe_click 가드 차단 [{0}]: {1}" -f $Axis, $Reason)
}

# 주입 기록 — user_away_guard.read_records 가 읽는 한 줄 JSON. t_ms 는 Windows 시계 UTC ms(GetLastInputInfo 와 같은 기준).
#   감시기 규칙: '<kind>_pre' 는 같은 이름의 완료('<kind>' 또는 '<kind>_failed')까지 주입 구간, 완료 기록은 t_ms~+1.5초,
#   x·y 가 있는 마지막 기록이 '주입 뒤 커서가 있어야 할 자리'다.
if (-not $InjectionLog) { $InjectionLog = Join-Path (Join-Path $PSScriptRoot '..\logs\pcaway') 'injections.jsonl' }
function Write-Injection {
    param([string] $Kind, $X, $Y)
    $line = [ordered]@{ t_ms = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); kind = $Kind; x = $X; y = $Y; source = 'safe_click'; button = $Button } | ConvertTo-Json -Compress
    $dir = Split-Path -Parent $InjectionLog
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::AppendAllText($InjectionLog, $line + "`n", [System.Text.UTF8Encoding]::new($false))
}
$script:injKind = $null   # '<kind>_pre' 를 남긴 뒤에만 채운다
$script:injDone = $false
# 주입 구간 안에서 가드 차단·예외로 끝나면 '<kind>_failed' 를 남기고 그대로 실패시킨다(break = 원래 예외 전파)
trap {
    if ($script:injKind -and -not $script:injDone) {
        $script:injDone = $true
        try { Write-Injection ($script:injKind + '_failed') $null $null } catch { }
    }
    break
}

# ---------------------------------------------------------------------------
# 0. DPI 인식 선언 — ★반드시 다른 작업보다 먼저★
#    누락 시 ③ DPI 사후 검증이 상시 실패한다(좌표가 스케일링됨).
# ---------------------------------------------------------------------------
$dpiMode = 'none'
try {
    # 2 = PROCESS_PER_MONITOR_DPI_AWARE
    $rc = [SafeClickNative]::SetProcessDpiAwareness(2)
    if ($rc -eq 0) { $dpiMode = 'per-monitor' }
    elseif ($rc -eq -2147024891) { $dpiMode = 'per-monitor(이미 설정됨)' }  # E_ACCESSDENIED
    else { $dpiMode = ("SHCore rc=0x{0:X}" -f $rc) }
} catch {
    try {
        [void][SafeClickNative]::SetProcessDPIAware()
        $dpiMode = 'system(폴백)'
    } catch {
        $dpiMode = 'declare-failed'
    }
}
Write-Step '0' ("DPI 인식 = {0}" -f $dpiMode)

Add-Type -AssemblyName System.Windows.Forms

$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
Write-Step '0' ("VirtualScreen X={0} Y={1} W={2} H={3}" -f $vs.X, $vs.Y, $vs.Width, $vs.Height)

# ---------------------------------------------------------------------------
# 이미지 → 스크린 좌표 변환 헬퍼
# ★-2560 을 하드코딩하지 않는다. VirtualScreen.X 에서 읽는다.★
#   모니터 구성이 바뀌면 자동으로 따라간다 (L-787 — 고정값은 유효기간을 갖는다).
# ---------------------------------------------------------------------------
function ConvertTo-ScreenX { param([int] $imageX) return $imageX + $vs.X }
function ConvertTo-ScreenY { param([int] $imageY) return $imageY + $vs.Y }

# ---------------------------------------------------------------------------
# 입력 좌표 결정
# ---------------------------------------------------------------------------
$haveImage  = ($ImageX  -ne [int]::MinValue) -and ($ImageY  -ne [int]::MinValue)
$haveScreen = ($ScreenX -ne [int]::MinValue) -and ($ScreenY -ne [int]::MinValue)

if ($haveImage -and $haveScreen) {
    Fail-Guard '입력' 'ImageX/Y 와 ScreenX/Y 를 동시에 지정했다. 하나만 써라.'
}
if (-not $haveImage -and -not $haveScreen) {
    Fail-Guard '입력' 'ImageX/ImageY 또는 ScreenX/ScreenY 중 하나는 필수다.'
}

if ($haveImage) {
    $tx = ConvertTo-ScreenX $ImageX
    $ty = ConvertTo-ScreenY $ImageY
    Write-Step '0' ("이미지({0},{1}) → 스크린({2},{3})  [offset {4},{5}]" -f $ImageX, $ImageY, $tx, $ty, $vs.X, $vs.Y)
} else {
    $tx = $ScreenX
    $ty = $ScreenY
    Write-Step '0' ("스크린 좌표 직접 지정 ({0},{1})  [버튼={2}]" -f $tx, $ty, $Button)
}

# ---------------------------------------------------------------------------
# ① 좌표계 검증 — ★가장 싸고 가장 중요★
#
#    ⚠ VirtualScreen 범위 검사로는 부족하다. 실측상 VirtualScreen 안에도
#      어떤 모니터에도 속하지 않는 ★구멍★ 이 있다(상단 실측 근거 참조).
#      따라서 ★Screen.AllScreens 멤버십★ 으로 판정한다.
#    ⇒ 좌표 변환을 빠뜨린 사고를 클릭 전에 잡을 수 있는 유일한 지점이다.
# ---------------------------------------------------------------------------
$inVirtual = ($tx -ge $vs.X) -and ($tx -lt ($vs.X + $vs.Width)) -and
             ($ty -ge $vs.Y) -and ($ty -lt ($vs.Y + $vs.Height))

$hostScreen = $null
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    $b = $s.Bounds
    if ($tx -ge $b.X -and $tx -lt ($b.X + $b.Width) -and
        $ty -ge $b.Y -and $ty -lt ($b.Y + $b.Height)) {
        $hostScreen = $s
        break
    }
}

if ($null -eq $hostScreen) {
    $detail = if ($inVirtual) {
        ("VirtualScreen 안이지만 ★어느 모니터에도 속하지 않는 구멍★ 이다. " +
         "모니터 배치: " + (([System.Windows.Forms.Screen]::AllScreens |
            ForEach-Object { "{0}={1}" -f $_.DeviceName, $_.Bounds }) -join ' / '))
    } else {
        (("VirtualScreen({0},{1})~({2},{3}) 범위를 벗어났다. " +
          "★좌표 변환(이미지→스크린)을 빠뜨렸을 가능성이 높다 — ConvertTo-ScreenX 를 경유하라.★") `
            -f $vs.X, $vs.Y, ($vs.X + $vs.Width - 1), ($vs.Y + $vs.Height - 1))
    }
    Fail-Guard '① 좌표계' ("({0},{1}) 은 클릭 가능한 좌표가 아니다. {2}" -f $tx, $ty, $detail)
}

Write-Step 'O' ("① 좌표계 통과 — ({0},{1}) 은 {2} {3} 안에 있다" -f $tx, $ty, $hostScreen.DeviceName, $hostScreen.Bounds)

# ---------------------------------------------------------------------------
# ② 창 소유권 검증
#    v3 는 WindowFromPoint 를 이미 갖고 있었으나 ★출력만 하고 중단하지 않았다★.
#    검사만 하고 exit 하지 않는 가드는 가드가 아니다 → throw 로 승격한다.
# ---------------------------------------------------------------------------
$pt = New-Object SafeClickNative+POINT
$pt.X = $tx
$pt.Y = $ty
$hwnd = [SafeClickNative]::WindowFromPoint($pt)

if ($hwnd -eq [IntPtr]::Zero) {
    Fail-Guard '② 창소유권' ("({0},{1}) 에 창이 없다 (WindowFromPoint = NULL)." -f $tx, $ty)
}

$root = [SafeClickNative]::GetAncestor($hwnd, [SafeClickNative]::GA_ROOT)
if ($root -eq [IntPtr]::Zero) { $root = $hwnd }

$actualPid = 0
[void][SafeClickNative]::GetWindowThreadProcessId($root, [ref] $actualPid)

$buf = New-Object System.Text.StringBuilder 512
$len = [SafeClickNative]::GetWindowTextW($root, $buf, 512)
$title = if ($len -gt 0) { $buf.ToString() } else { '(제목없음)' }

$procName = '(알수없음)'
try { $procName = (Get-Process -Id $actualPid -ErrorAction Stop).ProcessName } catch { }

if ($ExpectedProcessId -gt 0) {
    if ($actualPid -ne $ExpectedProcessId) {
        Fail-Guard '② 창소유권' (
            "({0},{1}) 의 창이 기대 프로세스가 아니다. 기대 PID={2} / 실제 PID={3} ({4}) 제목='{5}'" `
                -f $tx, $ty, $ExpectedProcessId, $actualPid, $procName, $title)
    }
    Write-Step 'O' ("② 창소유권 통과 — PID={0} ({1}) 제목='{2}'" -f $actualPid, $procName, $title)
} else {
    # ★사각지대 3번★ — 스킵을 조용히 넘기지 않고 명시적으로 경고한다.
    Write-Step '!' (("② 창소유권 ★스킵★ — ExpectedProcessId 미지정. " +
                    "관측된 창: PID={0} ({1}) 제목='{2}'. " +
                    "★올바른 좌표로 엉뚱한 창을 클릭하는 사고는 막지 못한다 — -ExpectedProcessId 를 넘겨라.★") `
                    -f $actualPid, $procName, $title)
}

# ---------------------------------------------------------------------------
# ④ 창 제목 검증 (선택) — ExpectedWindowTitle 지정 시에만 수행
#    ②(PID)가 못 잡는 "같은 프로세스의 다른 탭/폼" 오인을 줄인다.
# ---------------------------------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace($ExpectedWindowTitle)) {
    if ($title -notlike "*$ExpectedWindowTitle*") {
        Fail-Guard '④ 창제목' ((
            "창 제목이 기대와 다르다. 기대 포함={0} / 실제={1} (PID={2}). " +
            "★같은 앱의 다른 탭/폼이 앞에 있을 수 있다 — 대상 화면을 먼저 활성화하라.★") `
                -f $ExpectedWindowTitle, $title, $actualPid)
    }
    Write-Step 'O' ("④ 창제목 통과 — {0} 포함 확인" -f $ExpectedWindowTitle)
} else {
    Write-Step '!' ("④ 창제목 ★스킵★ — ExpectedWindowTitle 미지정. " +
                    "★②는 PID만 본다 — 같은 앱의 다른 탭/폼을 클릭해도 통과한다.★")
}

# ---------------------------------------------------------------------------
# ③ DPI 사후 검증
#    SetCursorPos 후 GetCursorPos 로 ★실제 도달 좌표★를 측정한다.
#    - DPI 스케일링 사건(2653,493 → 2493,571)을 잡는 축이다.
#    - ★클램프도 여기서 잡힌다★ — ①을 통과해도 OS 가 좌표를 옮길 수 있다.
#      클램프 지점은 예측 불가하므로(상단 실측 참조) 예측하지 말고 측정한다.
# ---------------------------------------------------------------------------
# 주입 시작 기록 — ③ 의 SetCursorPos 부터가 실제 커서 이동(입력)이다. 기록에 실패하면 예외로 보내지 않는다.
$isDrag = ($DragToX -ne [int]::MinValue -and $DragToY -ne [int]::MinValue)
$injKindNow = if ($NoClick) { 'cursor_move' } elseif ($WheelDelta -ne 0) { 'wheel' } elseif ($isDrag) { 'drag' } elseif ($DoubleClick) { 'dblclick' } else { 'click' }
Write-Injection ($injKindNow + '_pre') $null $null
$script:injKind = $injKindNow
if (-not [SafeClickNative]::SetCursorPos($tx, $ty)) {
    Fail-Guard '③ DPI' ("SetCursorPos({0},{1}) 호출 자체가 실패했다." -f $tx, $ty)
}
Start-Sleep -Milliseconds 60

$actual = New-Object SafeClickNative+POINT
if (-not [SafeClickNative]::GetCursorPos([ref] $actual)) {
    Fail-Guard '③ DPI' 'GetCursorPos 호출이 실패해 실제 커서 위치를 확인할 수 없다.'
}

$dx = [Math]::Abs($actual.X - $tx)
$dy = [Math]::Abs($actual.Y - $ty)

if ($dx -gt $Tolerance -or $dy -gt $Tolerance) {
    Fail-Guard '③ DPI' ((
        "커서가 목표에 도달하지 못했다. 목표=({0},{1}) 실제=({2},{3}) 오차=({4},{5}) 허용={6}. " +
        "★DPI 스케일링 또는 OS 클램프★ 를 의심하라 — 좌표계 변환·DPI 인식 선언을 재확인할 것.") `
            -f $tx, $ty, $actual.X, $actual.Y, $dx, $dy, $Tolerance)
}

Write-Step 'O' ("③ DPI 통과 — 목표=({0},{1}) 실제=({2},{3})" -f $tx, $ty, $actual.X, $actual.Y)

# ---------------------------------------------------------------------------
# 클릭 실행
# ---------------------------------------------------------------------------
if ($NoClick) {
    Write-Step '=' ("3축 검증 통과 — NoClick 지정으로 클릭은 수행하지 않았다. (버튼={0})" -f $Button)
} elseif ($WheelDelta -ne 0) {
    # ★휠 경로★ — 3축 가드를 통과한 좌표 위에서만 휠을 보낸다.
    $mod = 0
    # ★[2026-09-06 사이클88] 휠 경로도 스캔코드 SendInput 으로 통일한다(드래그와 같은 사유).
    $wscan = 0
    if ($ModifierKey -eq 'Shift') { $wscan = [SafeClickNative]::SC_SHIFT }
    elseif ($ModifierKey -eq 'Ctrl') { $wscan = [SafeClickNative]::SC_CONTROL }
    elseif ($ModifierKey -eq 'Alt') { $wscan = [SafeClickNative]::SC_MENU }
    if ($wscan -ne 0) {
        [SafeClickNative]::SendModifierScan($wscan, $false) | Out-Null
        Start-Sleep -Milliseconds 50
        $wvk = if ($ModifierKey -eq 'Shift') { 0x10 } elseif ($ModifierKey -eq 'Ctrl') { 0x11 } else { 0x12 }
        $wst = [SafeClickNative]::GetAsyncKeyState($wvk)
        Write-Step 'O' ("수식키 {0} 눌림 확인 — {1}" -f $ModifierKey, $(if (($wst -band 0x8000) -ne 0) { '(눌림)' } else { '★(안 눌림)★' }))
    }
    # ★수식키 잔류 진단★ — 1004 간트 휠 분기는 `ModifierKeys == Keys.Alt` 처럼 ★엄격 동등★ 이라
    #   다른 수식키가 하나라도 남아 있으면 조합이 통째로 무시된다(odev-w24 실측 보고).
    #   ⇒ 실제로 무엇이 눌려 있는지 찍어 두어야 "왜 안 먹었나" 를 사후에 가릴 수 있다.
    $held = @()
    if (([SafeClickNative]::GetAsyncKeyState(0x10) -band 0x8000) -ne 0) { $held += 'Shift' }
    if (([SafeClickNative]::GetAsyncKeyState(0x11) -band 0x8000) -ne 0) { $held += 'Ctrl' }
    if (([SafeClickNative]::GetAsyncKeyState(0x12) -band 0x8000) -ne 0) { $held += 'Alt' }
    Write-Step '!' ("휠 직전 수식키 실제 상태 = [{0}] (기대: {1})" -f $(if ($held.Count) { $held -join '+' } else { '없음' }), $(if($ModifierKey){$ModifierKey}else{'없음'}))
    # 음수 delta(아래/뒤 스크롤)는 2의 보수로 uint32 에 담아야 한다 — [uint32](-120) 은 캐스팅 예외다.
    $rawDelta = if ($WheelDelta -lt 0) { [uint32](4294967296 + $WheelDelta) } else { [uint32]$WheelDelta }
    for ($w = 1; $w -le $WheelTimes; $w++) {
        [SafeClickNative]::mouse_event([SafeClickNative]::MOUSEEVENTF_WHEEL, 0, 0, $rawDelta, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250
    }
    if ($wscan -ne 0) { [SafeClickNative]::SendModifierScan($wscan, $true) | Out-Null }
    Write-Step '=' ("3축 검증 통과 — ({0},{1}) ★휠★ delta={2} x{3} modifier={4} 전송 완료." -f $tx, $ty, $WheelDelta, $WheelTimes, $(if($ModifierKey){$ModifierKey}else{'none'}))
} elseif ($DragToX -ne [int]::MinValue -and $DragToY -ne [int]::MinValue) {
    # ★드래그 경로★ [2026-09-06 사이클88 신설]
    #   시작점은 위에서 3축 가드를 이미 통과했다. 종료점도 ①②를 동일하게 검사한 뒤에만 진행한다.
    #   ⇒ 드래그가 다른 창으로 빠져나가 엉뚱한 곳에 드롭되는 사고를 막는다.
    $dx = $DragToX; $dy = $DragToY
    $dOnScreen = $false
    foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
        if ($scr.Bounds.Contains($dx, $dy)) { $dOnScreen = $true; break }
    }
    if (-not $dOnScreen) { Fail-Guard '① 좌표계(종료점)' ("({0},{1}) 이 어느 모니터에도 속하지 않는다." -f $dx, $dy) }
    Write-Step 'O' ("① 좌표계(종료점) 통과 — ({0},{1})" -f $dx, $dy)

    if ($ExpectedProcessId -gt 0) {
        $dpt = New-Object SafeClickNative+POINT
        $dpt.X = $dx; $dpt.Y = $dy
        $dh = [SafeClickNative]::WindowFromPoint($dpt)
        $dOwner = 0
        [SafeClickNative]::GetWindowThreadProcessId($dh, [ref]$dOwner) | Out-Null
        if ($dOwner -ne $ExpectedProcessId) {
            Fail-Guard '② 창소유권(종료점)' ("({0},{1}) 의 창이 기대 프로세스가 아니다. 기대 PID={2} / 실제 PID={3}" -f $dx, $dy, $ExpectedProcessId, $dOwner)
        }
        Write-Step 'O' ("② 창소유권(종료점) 통과 — PID={0}" -f $dOwner)
    } else {
        Write-Step '!' "② 창소유권(종료점) 스킵 — ExpectedProcessId 미지정."
    }

    switch ($Button) {
        'Right'  { $dDown = [SafeClickNative]::MOUSEEVENTF_RIGHTDOWN;  $dUp = [SafeClickNative]::MOUSEEVENTF_RIGHTUP }
        'Middle' { $dDown = [SafeClickNative]::MOUSEEVENTF_MIDDLEDOWN; $dUp = [SafeClickNative]::MOUSEEVENTF_MIDDLEUP }
        default  { $dDown = [SafeClickNative]::MOUSEEVENTF_LEFTDOWN;   $dUp = [SafeClickNative]::MOUSEEVENTF_LEFTUP }
    }
    # ★[2026-09-06 사이클88] 수식키는 ★스캔코드 SendInput★ 으로 내린다.
    #   keybd_event(VK) 방식은 WinForms 의 ModifierKeys 에 반영되지 않는 사례가 실측됐다
    #   (간트 Ctrl+드래그가 끝내 발동하지 않았고 앱 로그에 진입 흔적이 없었다).
    #   순서 규약: 키 down → 50ms 대기 → 마우스 down … 마우스 up → 키 up.
    $dscan = 0
    if ($ModifierKey -eq 'Shift') { $dscan = [SafeClickNative]::SC_SHIFT }
    elseif ($ModifierKey -eq 'Ctrl') { $dscan = [SafeClickNative]::SC_CONTROL }
    elseif ($ModifierKey -eq 'Alt') { $dscan = [SafeClickNative]::SC_MENU }
    if ($dscan -ne 0) {
        [SafeClickNative]::SendModifierScan($dscan, $false) | Out-Null
        Start-Sleep -Milliseconds 50
        $vk = if ($ModifierKey -eq 'Shift') { 0x10 } elseif ($ModifierKey -eq 'Ctrl') { 0x11 } else { 0x12 }
        $st = [SafeClickNative]::GetAsyncKeyState($vk)
        Write-Step 'O' ("수식키 {0} 눌림 확인 — GetAsyncKeyState=0x{1:X4} {2}" -f $ModifierKey, ($st -band 0xFFFF), $(if (($st -band 0x8000) -ne 0) { '(눌림)' } else { '★(안 눌림 — 조합이 무시될 수 있다)★' }))
    }

    # down → 중간 이동(앱이 드래그로 인식하도록 여러 스텝) → up. 한 프로세스 안에서 수행한다.
    # ★시작점으로 커서를 다시 확정★ — ③ DPI 검증 이후 수식키 입력 등으로 커서가 움직였을 수 있다.
    [SafeClickNative]::SetCursorPos($tx, $ty) | Out-Null
    Start-Sleep -Milliseconds 80
    [SafeClickNative]::mouse_event($dDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    # ★스텝 수/대기는 넉넉하게★ — WinForms 는 MouseMove 를 실제로 여러 번 받아야 드래그로 인식한다.
    #   (사이클88 실측: 12스텝·45ms 로는 간트 Ctrl+드래그 선택영역이 만들어지지 않았다)
    $steps = 25
    for ($s = 1; $s -le $steps; $s++) {
        $ix = [int]($tx + ($dx - $tx) * $s / $steps)
        $iy = [int]($ty + ($dy - $ty) * $s / $steps)
        [SafeClickNative]::SetCursorPos($ix, $iy) | Out-Null
        [SafeClickNative]::mouse_event([SafeClickNative]::MOUSEEVENTF_MOVE, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 150
    [SafeClickNative]::mouse_event($dUp, 0, 0, 0, [UIntPtr]::Zero)
    # 마우스 up 이 끝난 뒤에 키를 뗀다(순서 규약).
    if ($dscan -ne 0) { Start-Sleep -Milliseconds 60; [SafeClickNative]::SendModifierScan($dscan, $true) | Out-Null }
    # ★[2026-09-06 사이클88] up 이후 정착 대기★ — MouseUp 핸들러가 다이얼로그를 띄우기 전에
    #   캡처가 먼저 찍히면 "폼이 안 떴다"로 오판한다(실제로 그 오판을 했다).
    #   드래그 선택 사각형이 남아 있는 캡처가 그 증거였다 — 그 사각형은 MouseUp 첫 줄에서 지워진다.
    Start-Sleep -Milliseconds 400
    Start-Sleep -Milliseconds 250
    Write-Step '=' ("3축 검증 통과(시작·종료 양쪽) — ({0},{1}) → ({2},{3}) ★{4}★ 드래그 완료 modifier={5}." -f $tx, $ty, $dx, $dy, $Button, $(if($ModifierKey){$ModifierKey}else{'none'}))
} else {
    switch ($Button) {
        'Right'  { $downFlag = [SafeClickNative]::MOUSEEVENTF_RIGHTDOWN;  $upFlag = [SafeClickNative]::MOUSEEVENTF_RIGHTUP }
        'Middle' { $downFlag = [SafeClickNative]::MOUSEEVENTF_MIDDLEDOWN; $upFlag = [SafeClickNative]::MOUSEEVENTF_MIDDLEUP }
        default  { $downFlag = [SafeClickNative]::MOUSEEVENTF_LEFTDOWN;   $upFlag = [SafeClickNative]::MOUSEEVENTF_LEFTUP }
    }
    [SafeClickNative]::mouse_event($downFlag, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 30
    [SafeClickNative]::mouse_event($upFlag, 0, 0, 0, [UIntPtr]::Zero)
    if ($DoubleClick) {
        # ★더블클릭★ — 2회차를 SM_CXDOUBLECLKTIME 안에 넣어야 OS 가 DBLCLK 로 합성한다.
        #   safe_click 을 두 번 따로 호출하면 프로세스 기동 시간(수 초) 때문에 절대 성립하지 않는다
        #   (2026-08-31 사이클60 실측 — 4.5초 간격이라 단일클릭 2회로 처리됐다).
        #   ⇒ 3축 가드는 그대로 통과한 뒤, 클릭 실행만 한 프로세스 안에서 연달아 수행한다.
        Start-Sleep -Milliseconds 60
        [SafeClickNative]::mouse_event($downFlag, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 30
        [SafeClickNative]::mouse_event($upFlag, 0, 0, 0, [UIntPtr]::Zero)
        Write-Step '=' ("3축 검증 통과 — ({0},{1}) ★{2}★ 더블클릭 완료." -f $tx, $ty, $Button)
    } else {
        Write-Step '=' ("3축 검증 통과 — ({0},{1}) ★{2}★ 클릭 완료." -f $tx, $ty, $Button)
    }
}

# 주입 완료 기록 — x·y 는 주입 뒤 커서가 있어야 할 자리(드래그는 종료점, 그 밖은 목표점)
$endX = if ($isDrag -and -not $NoClick -and $WheelDelta -eq 0) { $DragToX } else { $tx }
$endY = if ($isDrag -and -not $NoClick -and $WheelDelta -eq 0) { $DragToY } else { $ty }
$script:injDone = $true
Write-Injection $script:injKind $endX $endY

exit 0
