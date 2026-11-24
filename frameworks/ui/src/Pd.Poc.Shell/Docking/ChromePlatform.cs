// 창 크롬 스타일을 실행 OS 에 맞춰 고른다 — 자동 판별(macOS·iOS → macOS 스타일)·웹 호스트 OS 힌트·검수용 강제 지정·macOS 네이티브 신호등 사용 여부
using System;

namespace Pd.Poc.Shell.Docking;

/// <summary>설정값(ChromeStyleKind)을 실제로 그릴 스타일로 바꾸는 규칙. 값이 바뀌면 WindowCommands.ApplyShadow 로 크롬 리소스를 다시 건다.</summary>
public static class ChromePlatform
{
    private static ChromeStyleKind? s_forced;
    private static bool? s_hostIsApple;

    /// <summary>
    /// 검수용 강제 스타일(데스크톱 호스트 인자 --chrome-style, 원격 chrome.tune forced=). null 이면 설정값을 따른다.
    /// 저장하지 않는다 — Windows 에서 macOS 스타일 렌더를 캡처 검수하려고 둔다.
    /// </summary>
    public static ChromeStyleKind? ForcedStyle
    {
        get => s_forced;
        set { s_forced = value == ChromeStyleKind.Auto ? null : value; WindowCommands.ApplyShadow(); }
    }

    /// <summary>
    /// 브라우저가 도는 OS(웹 헤드 BrowserHostOs 가 navigator 로 판별, true = macOS·iOS·iPadOS). null 이면 이 프로세스의 OS 로 판별한다.
    /// 웹 프로세스는 OS 가 browser 라 OperatingSystem.IsMacOS 로는 알 수 없다(team-lead 결정 ②, 실패 시 Windows).
    /// </summary>
    public static bool? HostIsApple
    {
        get => s_hostIsApple;
        set { s_hostIsApple = value; WindowCommands.ApplyShadow(); }
    }

    /// <summary>실행 OS 의 기본 스타일: macOS·iOS(iPad·iPhone)·Mac Catalyst·Apple 브라우저면 MacOS, 그 밖(Windows·Android·판별 실패)은 Windows.</summary>
    public static ChromeStyleKind PlatformDefault =>
        (s_hostIsApple ?? (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()))
            ? ChromeStyleKind.MacOS
            : ChromeStyleKind.Windows;

    /// <summary>실제로 그릴 스타일: 강제 지정 → 설정값(Auto 면 PlatformDefault) 순서.</summary>
    public static ChromeStyleKind Resolve(ChromeStyleKind setting) =>
        s_forced ?? (setting == ChromeStyleKind.Auto ? PlatformDefault : setting);

    /// <summary>
    /// macOS 에서 실제로 돌고 그리는 스타일도 macOS 이면 OS 최상위 창(메인·SDI)은 OS 가 그리는 신호등을 쓴다(team-lead 결정 ①).
    /// 근거: Apple HIG "Avoid creating custom window UI"(https://developer.apple.com/design/human-interface-guidelines/windows).
    /// Avalonia 12.1.4 macOS 백엔드는 WindowDecorations=Full 일 때만 신호등을 보인다(native/OSX/WindowImpl.mm hasTrafficLights).
    /// </summary>
    public static bool UseNativeMacCaption => OperatingSystem.IsMacOS() && WindowCommands.EffectiveStyle == ChromeStyleKind.MacOS;

    /// <summary>
    /// 네이티브 신호등 자리(제목줄 왼쪽 여백, DIP). PD 설계값, 공식값 아님, Mac 실측 과제
    /// (2차 출처: Zed 편집기 소스의 macOS 26 SDK 신호등 영역 78px).
    /// </summary>
    public const double NativeMacCaptionInset = 78;

    /// <summary>호스트 인자 --chrome-style 값(mac|win|mars, 그 밖·생략 = 강제 해제)을 ForcedStyle 로 건다.</summary>
    public static void SetForcedStyleArg(string? value) => ForcedStyle = value?.ToLowerInvariant() switch
    {
        "mac" => ChromeStyleKind.MacOS,
        "win" => ChromeStyleKind.Windows,
        "mars" => ChromeStyleKind.MarsClassic,
        _ => null,
    };
}
