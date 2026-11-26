// 구성 오류 처리 정책 열거형 — 정의 밖 값은 Collect 로 안전 처리한다(UADR-0009 원칙 4)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="XkxkUiBuilder.Build"/> 가 Error 진단을 만났을 때의 처리 정책이다.
/// </summary>
/// <remarks>정의되지 않은 값은 <see cref="Collect"/> 로 취급한다. 값은 끝에만 추가하고 기존 숫자는 바꾸지 않는다.</remarks>
public enum UiErrorPolicy
{
    /// <summary>진단을 모으고 예외를 던지지 않는다(기본값).</summary>
    Collect = 0,

    /// <summary>Error 진단이 있으면 Build 끝에 <see cref="UiConfigurationException"/> 을 던진다.</summary>
    Throw = 1,
}
