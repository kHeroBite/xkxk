// OS '애니메이션 줄이기' 설정을 플랫폼 공급자로 읽어 전환 애니메이션 생략 여부를 알려주는 UI 레이어 보조 클래스
using System;

namespace xkxk.frameworks.ui.Motion;

/// <summary>
/// 애니메이션 줄이기(Windows 클라이언트 영역 애니메이션 끔, 브라우저 prefers-reduced-motion, Android 애니메이터 배율 0) 판정.
/// Avalonia 공통 코드에는 이 설정을 읽는 API 가 없어 플랫폼 호스트(Desktop·Browser·Android Program)가 <see cref="Provider"/> 를 채운다.
/// 공급자가 없거나 예외를 던지면 false(애니메이션 사용)로 본다. 시험용 강제값은 <see cref="ForcedReduced"/>.
/// </summary>
public static class MotionPreference
{
    /// <summary>플랫폼 호스트가 기동 시 채우는 조회 함수. 참이면 '줄이기' 켜짐.</summary>
    public static Func<bool>? Provider { get; set; }

    /// <summary>시험·진단용 강제값(null 이면 사용 안 함). 사용자 OS 설정은 바꾸지 않고 이 값으로만 재현한다.</summary>
    public static bool? ForcedReduced { get; set; }

    /// <summary>지금 애니메이션 줄이기가 켜져 있는가. 호출 때마다 공급자를 다시 읽어 실행 중 설정 변경을 반영한다.</summary>
    public static bool Reduced
    {
        get
        {
            if (ForcedReduced is { } forced)
            {
                return forced;
            }

            try
            {
                return Provider?.Invoke() ?? false;
            }
            catch
            {
                return false; // 설정을 못 읽으면 기본 동작(애니메이션 사용)
            }
        }
    }
}
