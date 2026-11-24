// 기기 판별 입력(플랫폼·입력 장치·화면 크기·기기 종류)을 담는 UI 비의존 값 레코드 — 각 호스트(Android·브라우저·데스크톱)가 채우고 UiStyleRules 가 판정한다
namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>실행 플랫폼 종류.</summary>
public enum PlatformKind
{
    Unknown,
    Windows,
    MacOS,
    Linux,
    Android,
    IOS,
    Browser,
}

/// <summary>iOS userInterfaceIdiom 에 대응하는 기기 종류(iOS 외에는 Unknown).</summary>
public enum DeviceIdiom
{
    Unknown,
    Phone,
    Pad,
}

/// <summary>
/// 호스트가 수집한 기기 정보. null 은 '알 수 없음'이며 판정은 정보가 없으면 크기 폴백으로 간다
/// (정본 plan/05_아키텍처/08_지원_폼팩터_용어.md §1-3-1 판정표).
/// </summary>
/// <param name="Platform">실행 플랫폼.</param>
/// <param name="HasTouch">터치 입력 가능(주 입력이 아니어도).</param>
/// <param name="PrecisePointer">정밀 포인터(마우스·터치패드) 있음. 웹은 any-pointer: fine.</param>
/// <param name="HoverCapable">웹 hover 미디어 쿼리.</param>
/// <param name="SmallestWidthDp">기기 기준 짧은 변(Android smallestScreenWidthDp).</param>
/// <param name="WindowWidthDp">앱 창 폭(분할 화면이면 창 폭).</param>
/// <param name="IsPcClass">ChromeOS(org.chromium.arc)·FEATURE_PC.</param>
/// <param name="IsFoldableCapable">힌지 센서 기능 플래그(참고값, 판정에 쓰지 않는다).</param>
/// <param name="IsMultiWindow">Activity.isInMultiWindowMode.</param>
/// <param name="Idiom">iOS userInterfaceIdiom.</param>
public sealed record DeviceTraits(
    PlatformKind Platform,
    bool? HasTouch = null,
    bool? PrecisePointer = null,
    bool? HoverCapable = null,
    double? SmallestWidthDp = null,
    double? WindowWidthDp = null,
    bool? IsPcClass = null,
    bool? IsFoldableCapable = null,
    bool? IsMultiWindow = null,
    DeviceIdiom Idiom = DeviceIdiom.Unknown);
