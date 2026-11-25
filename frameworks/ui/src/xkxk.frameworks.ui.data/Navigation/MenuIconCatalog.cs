// 메뉴 아이콘 카탈로그(손 작성 부분) — 화면 ID·PoC 문서 종류에서 메뉴 ID·아이콘 리소스 키를 찾는다. UI 비의존 문자열만 다룬다(생성 부분은 MenuIconCatalog.Generated.cs)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Navigation;

/// <summary>
/// 아이콘 키 찾기 순서: 화면 ID → 메뉴 ID → PdMenuIcon&lt;메뉴ID&gt;. 화면 ID 가 없으면 PoC 문서 종류로 대표 화면을 고른다.
/// 그래도 없으면 셸 기본 문서 아이콘(PdShellIconDocument)이고, 키 자체를 못 찾는 화면 쪽 대체(제목 첫 글자 원형 표지)는 UI 층 몫이다(22 설계).
/// </summary>
public static partial class MenuIconCatalog
{
    /// <summary>메뉴 ID 를 모르는 문서 창의 기본 아이콘 키.</summary>
    public const string DefaultDocumentIconKey = ShellIconKeyPrefix + "Document";

    /// <summary>
    /// PoC 샘플 문서 종류(Shell DocumentKind 이름) → 대표 화면 ID. 후보 대응이며 이 표 한 곳에만 둔다(22 설계·plan §4.3).
    /// 데이터 층은 Shell 의 enum 을 모르므로 이름 문자열로 받는다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ScreenIdByPocKind { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Grid"] = "SCR-PRJ-01",
        ["Gantt"] = "SCR-SCH-01",
        ["Form"] = "SCR-PRJ-02",
    };

    /// <summary>새 그리드 문서 창의 아이콘 키 = IconKeyFor(null, "Grid"). 상단 메뉴 '새 그리드' 항목과 그 창이 같이 쓴다(22 설계 §2.1).</summary>
    public static string GridIconKey => IconKeyFor(null, "Grid");

    /// <summary>새 간트 문서 창의 아이콘 키 = IconKeyFor(null, "Gantt").</summary>
    public static string GanttIconKey => IconKeyFor(null, "Gantt");

    /// <summary>새 입력폼 문서 창의 아이콘 키 = IconKeyFor(null, "Form").</summary>
    public static string FormIconKey => IconKeyFor(null, "Form");

    /// <summary>화면 ID → 메뉴 ID. 모르면 null.</summary>
    public static string? MenuIdOf(string? screenId) =>
        !string.IsNullOrEmpty(screenId) && MenuIdByScreenId.TryGetValue(screenId, out var menuId) ? menuId : null;

    /// <summary>PoC 문서 종류 이름 → 대표 화면 ID. 모르면 null.</summary>
    public static string? ScreenIdOfPocKind(string? kind) =>
        !string.IsNullOrEmpty(kind) && ScreenIdByPocKind.TryGetValue(kind, out var screenId) ? screenId : null;

    /// <summary>메뉴 ID → 아이콘 리소스 키. 모르면 null.</summary>
    public static string? IconKeyOfMenu(string? menuId) =>
        !string.IsNullOrEmpty(menuId) && IconKeyByMenuId.TryGetValue(menuId, out var key) ? key : null;

    /// <summary>문서의 화면 ID(없으면 PoC 종류)로 아이콘 키를 정한다. 끝까지 못 찾으면 DefaultDocumentIconKey.</summary>
    public static string IconKeyFor(string? screenId, string? pocKind) =>
        IconKeyOfMenu(MenuIdOf(string.IsNullOrEmpty(screenId) ? ScreenIdOfPocKind(pocKind) : screenId)) ?? DefaultDocumentIconKey;

    /// <summary>문서의 메뉴 ID(화면 ID 우선, 없으면 PoC 종류). 못 찾으면 null.</summary>
    public static string? MenuIdFor(string? screenId, string? pocKind) =>
        MenuIdOf(string.IsNullOrEmpty(screenId) ? ScreenIdOfPocKind(pocKind) : screenId);

    /// <summary>셸 창 아이콘 키(이름은 ShellIconNames 중 하나). 모르는 이름은 기본 문서 아이콘.</summary>
    public static string ShellIconKey(string? name)
    {
        foreach (var n in ShellIconNames)
        {
            if (string.Equals(n, name, StringComparison.Ordinal)) { return ShellIconKeyPrefix + n; }
        }

        return DefaultDocumentIconKey;
    }
}
