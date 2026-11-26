// 메뉴를 보는 화면 종류 — 값은 끝에만 추가한다(UADR-0009 원칙 4, UADR-0014)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 메뉴 항목 상태를 평가하는 화면 종류다.
/// <see cref="IUiMenuRegistry.GetState(string, UiMenuSurface)"/> 에 넘기고 <see cref="UiMenuContext.Surface"/> 로 읽는다.
/// </summary>
/// <remarks>확장 규칙: 새 값은 끝에만 더하고 기존 값의 번호·의미는 바꾸지 않는다. 모르는 값이 올 수 있다고 보고 처리한다.</remarks>
public enum UiMenuSurface
{
    /// <summary>제목줄 메뉴가 있는 화면(데스크톱·웹의 PC형·태블릿형).</summary>
    Desktop = 0,

    /// <summary>모바일형 화면(하단 탭·삼선 서랍).</summary>
    Mobile = 1,
}
