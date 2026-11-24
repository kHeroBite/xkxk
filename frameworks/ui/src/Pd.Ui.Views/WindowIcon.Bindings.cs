// WindowIcon 을 쓰는 axaml 자리의 바인딩 변환기 — 문서 찾기·표시 방식·탐색기·NAV 메뉴 아이콘 키
using System.Linq;
using Avalonia.Data.Converters;
using Pd.Client.Data.Navigation;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Theming;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

public sealed partial class WindowIcon
{
    /// <summary>창 DataContext(문서·탭 묶음) → 그 창에 보이는 문서.</summary>
    public static IValueConverter ToDocument { get; } = new FuncValueConverter<object?, PocDocument?>(WindowCommands.FindDocument);

    /// <summary>창 DataContext 에 문서가 있는가.</summary>
    public static IValueConverter HasDocument { get; } = new FuncValueConverter<object?, bool>(c => WindowCommands.FindDocument(c) is not null);

    /// <summary>DOCK 탭 아이콘 보임(표시 방식·title_only·IsBusyShown) — title 이면 inline 불러오는 중 표시 때만.</summary>
    public static IMultiValueConverter TabIconVisible { get; } = new FuncMultiValueConverter<object?, bool>(v =>
    {
        var a = v.ToArray();
        return a.Length == 3 && a[0] is TitleTabDisplay display
            && (display != TitleTabDisplay.Title || (a[1] is BusyTitleOnly.Inline && a[2] is true));
    });

    /// <summary>DOCK 탭 아래 진행 줄(title + underline + IsBusyShown).</summary>
    public static IMultiValueConverter TabUnderlineVisible { get; } = new FuncMultiValueConverter<object?, bool>(v =>
    {
        var a = v.ToArray();
        return a.Length == 3 && a[0] is TitleTabDisplay.Title && a[1] is BusyTitleOnly.Underline && a[2] is true;
    });

    /// <summary>DOCK 탭 제목 보임(icon 이면 숨김).</summary>
    public static IValueConverter TabTitleVisible { get; } = new FuncValueConverter<TitleTabDisplay, bool>(d => d != TitleTabDisplay.Icon);

    /// <summary>PoC 문서 종류 이름 → 아이콘 키(탐색기).</summary>
    public static IValueConverter KindIconKey { get; } = new FuncValueConverter<string?, string?>(kind => MenuIconCatalog.IconKeyFor(null, kind));

    /// <summary>NAV 메뉴 항목 → 아이콘 키. 화면·셸 아이콘이 없는 명령은 null.</summary>
    public static IValueConverter NavIconKey { get; } = new FuncValueConverter<NavEntry?, string?>(NavKey);

    /// <summary>NAV 메뉴 항목에 아이콘이 있는가.</summary>
    public static IValueConverter NavHasIcon { get; } = new FuncValueConverter<NavEntry?, bool>(e => NavKey(e) is not null);

    /// <summary>탐색기 별 채움(즐겨찾기 영역·항목 수, 인자 = 문서 종류 이름).</summary>
    public static IMultiValueConverter FavoriteStarOpacity { get; } = new FavoriteStarConverter();

    private static string? NavKey(NavEntry? entry) => entry switch
    {
        { Command: "Open", Param: { } kind } => MenuIconCatalog.IconKeyFor(null, kind),
        { Command: "Panel", Param: "settings" } => MenuIconCatalog.ShellIconKey("Settings"),
        { Command: "Panel", Param: "explorer" } => MenuIconCatalog.ShellIconKey("Explorer"),
        { Command: "Panel", Param: "notify" } => MenuIconCatalog.ShellIconKey("Notify"),
        { Command: "SystemView", Param: "LogView" } => MenuIconCatalog.ShellIconKey("Log"),
        _ => null,
    };

    private sealed class FavoriteStarConverter : IMultiValueConverter
    {
        // 두 번째 값(항목 수)은 다시 계산을 일으키는 용도다
        public object Convert(System.Collections.Generic.IList<object?> values, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            values.Count > 0 && values[0] is FavoriteBarViewModel bar && MenuIconCatalog.ScreenIdOfPocKind(parameter as string) is { } id
            && bar.Items.Any(i => i.ScreenId == id) ? 1.0 : 0.3;
    }
}
