// 상단 메뉴 항목 아이콘 키 첨부 속성 — 키가 있으면 pdCmdIcon 클래스를 걸어 MenuIconStyles 의 Icon 템플릿(WindowIcon)이 붙게 한다(22 §2.3)
using Avalonia;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// 메뉴 항목에 아이콘 리소스 키(PdCmdIcon·PdShellIcon·PdMenuIcon)를 단다. 그림은 <c>Themes/MenuIconStyles.axaml</c> 의
/// <c>MenuItem.pdCmdIcon</c> 스타일이 항목마다 새 <see cref="WindowIcon"/> 으로 만든다. 키 단일 출처는 <c>CmdIconKeys</c>·<c>MenuIconCatalog</c> 다.
/// </summary>
public static class MenuIcon
{
    /// <summary>스타일이 고르는 클래스 이름.</summary>
    public const string ClassName = "pdCmdIcon";

    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, string?>("Key", typeof(MenuIcon));

    static MenuIcon()
    {
        // 스타일 Setter(모니터 라디오 목록)로 단 키도 같은 길로 클래스가 걸린다
        KeyProperty.Changed.AddClassHandler<MenuItem>((item, e) =>
            item.Classes.Set(ClassName, e.NewValue is string { Length: > 0 }));
    }

    public static string? GetKey(MenuItem item) => item.GetValue(KeyProperty);

    public static void SetKey(MenuItem item, string? value) => item.SetValue(KeyProperty, value);
}
