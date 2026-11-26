// 디자인 테마 해석기의 가변 상태 보관소(B05) — 앱 컨텍스트(UiAppContext.DesignTheme)가 소유하고 DesignThemeResolver 정적 표면이 위임한다
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// DesignThemeResolver 의 가변 상태 11종과 정적 이벤트 구독 목록(B05, UADR-0012). 해석기는 실효 ID 를 계산하는 정적 조회 함수로 남고
/// (a2 §3 대비 예외), 상태만 이 형식으로 옮겨 앱 컨텍스트가 소유한다. 이름·초기값은 옮기기 전 정적 필드·속성과 같다.
/// 스레드 동기화는 옮기기 전과 같이 하지 않는다(UI 스레드에서 쓰고 읽는다).
/// </summary>
internal sealed class DesignThemeState
{
    /// <summary>사용자가 저장한 디자인 테마 ID(비면 선택 안 함).</summary>
    internal string _selected = "";

    /// <summary>사용자가 저장한 움직임 수준 원본값.</summary>
    internal string _motionLevel = "";

    /// <summary>효과 덮어쓰기(짧은 키 또는 전체 설정 키). 기본은 빈 사전(Ordinal).</summary>
    internal IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>진단 전용 소프트웨어 렌더 흉내.</summary>
    internal bool _simulateSoftware;

    /// <summary>마지막으로 Changed 를 낸 실효 상태 서명.</summary>
    internal string _signature = "";

    /// <summary>사용자가 고른 효과 단계 원본값.</summary>
    internal string _effectLevel = "";

    /// <summary>Changed 이벤트 구독 목록(DesignThemeResolver.Changed 의 add/remove 가 위임).</summary>
    internal Action? Changed;

    /// <summary>사용자 배경 이미지 읽기 실패 여부.</summary>
    internal bool DecorImageFailed { get; set; }

    /// <summary>열린 문서 창 수 공급자(화면 층이 등록).</summary>
    internal Func<int>? WindowCount { get; set; }

    /// <summary>지금 화면이 NAV 인가 공급자(화면 층이 등록).</summary>
    internal Func<bool>? NavLayout { get; set; }

    /// <summary>지금 플랫폼. 기본은 실행 중인 OS 판정이고 시험만 바꾼다.</summary>
    internal MotionPlatform Platform { get; set; } =
        System.OperatingSystem.IsBrowser() ? MotionPlatform.Web : System.OperatingSystem.IsAndroid() ? MotionPlatform.Android : MotionPlatform.Desktop;

    /// <summary>규칙 미리 계산용 요소 속성 층 테마 ID(null 이면 실효 디자인 테마).</summary>
    internal string? ElementThemeOverride { get; set; }
}
