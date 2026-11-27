// 창 배치 방식 — 값은 끝에만 추가한다(UADR-0009 원칙 4, UADR-0016)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 창을 여는 배치 방식이다.
/// 등록 값은 <see cref="UiWindowOptions.Placement"/>, 열 때 지정하는 값은 <see cref="UiWindowOpenOptions.Placement"/> 에 넣는다.
/// </summary>
/// <remarks>
/// <para>값이 추가될 수 있다. 모르는 값을 받으면 분리창(<see cref="Sdi"/>)이 아닌 것으로 보고 처리한다.</para>
/// <para>확장 규칙: 새 값은 끝에만 더하고 기존 값의 번호·의미는 바꾸지 않는다.</para>
/// </remarks>
public enum UiWindowPlacement
{
    /// <summary>
    /// 지정하지 않음. 등록 값이면 셸의 보통 문서 배치로 열고,
    /// 열기 옵션 값이면 창 정의의 배치(<see cref="UiWindowDefinition.Placement"/>)를 따른다.
    /// </summary>
    Default = 0,

    /// <summary>분리창(SDI)으로 연다.</summary>
    Sdi = 1,
}
