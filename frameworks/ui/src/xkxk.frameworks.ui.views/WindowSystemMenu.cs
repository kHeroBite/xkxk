// 창 메뉴(시스템 메뉴) 열기·로고 더블클릭 닫기(U9 ⑤⑥⑧) — 메인·SDI 창 제목줄 메뉴와 MDI 문서 창 제목줄 메뉴를 키·로고 입력으로 연다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 창 메뉴 자체(항목·문구)는 WindowTitleMenu 와 각 제목줄 axaml 의 ContextMenu 가 정본이다. 여기서는 그 메뉴를 찾아 여는 규칙만 둔다.
/// - 창 메뉴: SDI 창이면 SdiTitleBar, 메인 창이면 MainTitleBar 안에서 ContextMenu 를 가진 첫 요소(제목줄 바탕).
/// - 문서 창 메뉴: MDI 문서 창 PART_Header 의 ContextMenu(Dock DocumentContextMenu).
/// - 로고(시스템 아이콘) 더블클릭 = 그 창 닫기(MS Learn 제목줄 사용자 지정: 시스템 아이콘 더블클릭은 창을 닫는다).
/// </summary>
public static class WindowSystemMenu
{
    private static bool s_installed;

    /// <summary>앱 시작 시 1회: 로고 더블클릭 닫기 처리기를 모든 TopLevel 에 건다.</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        // 첫 클릭으로 연 창 메뉴의 바깥 누름 닫기(light dismiss)가 두 번째 누름을 Handled 로 만들 수 있어 handledEventsToo 로 받는다.
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// 창 메뉴를 연다. 이 창에 제목줄 메뉴가 없으면 false. screen 은 화면 좌표(비클라이언트 우클릭 자리)이고, 없으면 제목줄 왼쪽 아래다(Alt+Space — Windows 시스템 메뉴 자리).
    /// 포인터 위치 기준은 쓰지 않는다. 단축키에서는 포인터가 어디에 있든 상관없어야 한다. 위치 지정은 메뉴가 닫힐 때 WindowTitleMenu 가 되돌린다.
    /// </summary>
    public static bool OpenWindowMenu(TopLevel top, PixelPoint? screen = null)
    {
        // 숨은 탭 문서 하위는 건너뛴다(TabSlotMarker) — 메인 창은 SdiTitleBar 가 없어 트리 끝까지 훑는다
        Control? bar = TabSlotMarker.Descendants(top).OfType<SdiTitleBar>().FirstOrDefault();
        bar ??= TabSlotMarker.Descendants(top).OfType<MainTitleBar>().FirstOrDefault();
        var owner = bar is null ? null : bar.GetSelfAndVisualDescendants().OfType<Control>().FirstOrDefault(c => c.ContextMenu is not null);
        if (owner?.ContextMenu is not { } menu)
        {
            return false;
        }

        menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        menu.PlacementRect = screen is { } at ? new Rect(owner.PointToClient(at), new Size(1, 1)) : null;
        menu.Open(owner);
        return true;
    }

    /// <summary>MDI 문서 창의 창 메뉴(Win32 MDI 의 Alt+- 대응)를 연다. 문서가 이 창의 MDI 창에 없으면 false.</summary>
    public static bool OpenDocumentMenu(TopLevel top, PocDocument? document)
    {
        var header = document is null ? null : HeaderOf(top, document);
        if (header?.ContextMenu is not { } menu)
        {
            return false;
        }

        menu.Open(header);
        return true;
    }

    private static Control? HeaderOf(TopLevel top, PocDocument document) =>
        TabSlotMarker.Descendants(top).OfType<MdiDocumentWindow>()
            .Where(w => ReferenceEquals(WindowCommands.FindDocument(w.DataContext), document))
            .SelectMany(w => w.GetVisualDescendants().OfType<Control>())
            .FirstOrDefault(c => c.Name == "PART_Header");

    /// <summary>로고 두 번째 누름(ClickCount=2) → 첫 클릭이 연 메뉴를 닫고 로고가 속한 창(MDI 문서·SDI 문서·메인 창)을 닫는다.</summary>
    private static void OnPressed(TopLevel top, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(top).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<ChromeLogo>(includeSelf: true) is not { } logo)
        {
            return;
        }

        foreach (var c in logo.GetSelfAndVisualAncestors().OfType<Control>())
        {
            c.ContextMenu?.Close();
        }

        e.Handled = true;
        if (logo.FindAncestorOfType<MdiDocumentWindow>() is { } mdi)
        {
            Close(WindowCommands.FindDocument(mdi.DataContext));
        }
        else if (logo.FindAncestorOfType<SdiTitleBar>() is { } sdi)
        {
            Close(WindowCommands.FindDocument(sdi.DataContext));
        }
        else if (top is Window window)
        {
            window.Close(); // 메인 창: 닫기 버튼과 같은 경로(App 종료 처리·자동 저장)
        }
    }

    private static void Close(PocDocument? document)
    {
        if (document is not null && App.Shell is { } shell)
        {
            shell.CloseDocument(document);
        }
    }
}
