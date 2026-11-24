// OS(Windows) 창 맞춤(Snap) 켜짐 판정 — SystemParametersInfo(SPI_GETWINARRANGING) 공식 문서 API 만 쓰고, 실기 확인 전에는 판정 불가(null)를 돌려 SDI 자동 방식이 '앱' 으로 동작하게 한다(11_창배치_스냅레이아웃.md §7)
using System.Runtime.InteropServices;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// UiSettings.ResolveSdiOwner 의 osSnapOn 인자를 만든다. true = OS Snap 켜짐(자동이면 OS 위임), false = 꺼짐(자동이면 앱), null = 판정 불가(자동이면 앱).
/// 근거(확인됨, learn.microsoft.com):
/// - SPI_GETWINARRANGING(0x0082): 창 배치(window arrangement)가 켜져 있는지 pvParam 의 BOOL 로 돌려준다. 도킹·스냅 크기 같은 개별 항목은 따로 있고,
///   이 값으로 전체 동작이 켜졌는지 판단하라고 문서가 안내한다.
///   https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow
/// 확실하지 않음:
/// - Windows 11 의 '창을 화면 위쪽으로 끌 때 맞춤 레이아웃 표시'(위쪽 막대) 개별 설정을 읽는 공식 API 는 찾지 못했다. SPI_GETWINARRANGING 이 켜져 있어도
///   위쪽 막대만 꺼져 있을 수 있다. 레지스트리 값 이름을 추측해 읽지 않는다(oplan 검증 F2).
/// - Pd SDI 는 BeginMoveDrag(OS 이동 루프)로 끈다. 이 경로에서 OS 위쪽 막대가 실제로 뜨는지는 실기 확인 전이다(otest-1, batch 3 에서 11 문서 §7 반영).
/// 그래서 <see cref="OsBarConfirmed"/> 가 false(기본)인 동안은 API 결과와 무관하게 null(판정 불가 → 앱)을 돌려준다.
/// 실기에서 BeginMoveDrag 경로의 OS 막대가 확인되면 이 상수 하나만 true 로 바꾼다(그때부터 SPI_GETWINARRANGING 결과를 쓴다).
/// </summary>
internal static class OsSnapProbe
{
    /// <summary>
    /// 실기 확인 결과 반영 스위치(oplan 계획: '상수 1개'). false = 실기 미확인 → 항상 판정 불가(null).
    /// true 로 바꾸는 조건: otest 실기에서 SdiTitleBar 끌기(BeginMoveDrag)로 위쪽에 OS Snap 막대가 뜨는 것이 스크린샷으로 확인됨.
    /// </summary>
    internal const bool OsBarConfirmed = false;

    private const uint SpiGetWinArranging = 0x0082;

    /// <summary>
    /// OS Snap 켜짐 판정. Windows 가 아니면 null(macOS 등은 OS 위임 대상 아님 — 자동이면 앱).
    /// 끌기 시작 때 1회 부른다(설정은 끄는 중 바뀌지 않는다고 본다). 호출 실패도 null 이다.
    /// </summary>
    public static bool? IsOsSnapOn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

#pragma warning disable CS0162 // OsBarConfirmed 는 실기 결과 반영용 상수 — 미확인 동안 아래 조회는 도달하지 않는다
        if (!OsBarConfirmed)
        {
            return null;
        }

        return QueryWinArranging();
#pragma warning restore CS0162
    }

    /// <summary>SPI_GETWINARRANGING 원값(진단·시험 보고용). 실패하거나 Windows 가 아니면 null. OsBarConfirmed 와 무관하게 실제 API 값을 읽는다.</summary>
    public static bool? QueryWinArranging()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var on = 0;
            return SystemParametersInfoW(SpiGetWinArranging, 0, ref on, 0) ? on != 0 : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>진단 한 줄(끌기 시작 로그용): 실효 판정·원값·확인 스위치.</summary>
    public static string Describe() =>
        $"osSnap={Fmt(IsOsSnapOn())} spiWinArranging={Fmt(QueryWinArranging())} osBarConfirmed={OsBarConfirmed}";

    private static string Fmt(bool? v) => v switch { true => "on", false => "off", _ => "unknown" };

    // pvParam 은 BOOL(4바이트) 을 받는다(위 문서 SPI_GETWINARRANGING 설명 — 결과를 pvParam 의 BOOL 에 쓴다).
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref int pvParam, uint winIni);
}
