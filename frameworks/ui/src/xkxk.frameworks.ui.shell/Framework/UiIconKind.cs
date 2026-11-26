// 아이콘 원천 종류 열거형 — 값은 끝에만 추가한다(UADR-0009 원칙 4, UADR-0013)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>아이콘 정의의 그림 원천 종류.</summary>
/// <remarks>
/// <para>값이 추가될 수 있다. 모르는 값을 받으면 그리지 못하는 아이콘으로 보고 폴백한다.</para>
/// <para>값은 끝에만 추가하고 기존 숫자는 바꾸지 않는다.</para>
/// </remarks>
public enum UiIconKind
{
    /// <summary>SVG path 문법 문자열(<see cref="UiIconDefinition.PathData"/>).</summary>
    PathData = 0,

    /// <summary>Avalonia 리소스 키(<see cref="UiIconDefinition.ResourceKey"/>) — 테마별 값을 둘 수 있다.</summary>
    Resource = 1,
}
