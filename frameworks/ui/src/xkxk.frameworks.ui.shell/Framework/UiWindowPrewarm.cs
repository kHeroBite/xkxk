// 창 종류의 예열 수준 열거형 — 값 추가만 허용(UADR-0009, UADR-0017)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 창 종류를 처음 열기 전에 미리 준비해 두는 수준이다. <see cref="UiWindowOptions.Prewarm"/> 로 정한다.
/// </summary>
/// <remarks>확장 규칙(UADR-0009): 값은 추가만 하고 기존 값의 숫자와 의미는 바꾸지 않는다.</remarks>
public enum UiWindowPrewarm
{
    /// <summary>예열하지 않는다(기본값). 처음 열 때 내용을 만든다.</summary>
    None = 0,

    /// <summary>앱이 한가할 때 내용을 한 번 미리 만들어 첫 열기를 빠르게 한다.</summary>
    Warm = 1,

    /// <summary>시작 뒤 유휴 때 미리 만들고, 열 때 준비된 뷰를 먼저 쓰며(GridViewPool 설정이 켜진 경우), 쓴 뒤 유휴 때 하나를 다시 채운다.</summary>
    KeepReady = 2,
}
