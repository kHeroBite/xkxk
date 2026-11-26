// 메뉴 항목 종류 열거형 — 값은 끝에만 추가한다(UADR-0009 원칙 4, UADR-0014)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>메뉴 항목의 종류.</summary>
/// <remarks>
/// <para>값이 추가될 수 있다. 화면은 모르는 값의 항목을 그리지 않는다.</para>
/// <para>값은 끝에만 추가하고 기존 숫자는 바꾸지 않는다.</para>
/// </remarks>
public enum UiMenuItemKind
{
    /// <summary>명령을 실행하는 항목(<see cref="UiMenuItemOptions.CommandId"/> 필수).</summary>
    Command = 0,

    /// <summary>자식 항목을 담는 하위 메뉴. 다른 항목의 부모가 될 수 있는 유일한 종류다.</summary>
    Submenu = 1,

    /// <summary>구분선.</summary>
    Separator = 2,

    /// <summary>화면이 <see cref="UiMenuItemOptions.Section"/> 키로 내용을 채우는 영역.</summary>
    Section = 3,
}
