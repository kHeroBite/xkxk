// 제목줄 '창' 상위 메뉴(U16 — 옛 '윈도우' 통합) — 정렬·창 상태·독립 창·모니터·닫기·창 옵션·열린 창 목록 코드 비하인드
using xkxk.frameworks.ui.shell;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>제목줄 '창' 상위 메뉴 — 정렬·창 상태·독립 창·모니터·닫기·창 옵션·열린 창 목록. MainTitleBar.MainMenu 의 직속 MenuItem 이라 더보기 재배치(ReflowMenu)·키보드 탐색이 그대로 동작한다.</summary>
public partial class WindowMenu : MenuItem
{
    // 파생 형식은 기본 MenuItem 테마를 찾지 못하므로 테마 키를 MenuItem 으로 둔다(ChromeLogo 와 같은 방식)
    protected override Type StyleKeyOverride => typeof(MenuItem);

    public WindowMenu()
    {
        InitializeComponent();
        SubmenuOpened += OnSubmenuOpened;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // 화면에 붙은 뒤(배치 끝, 데스크톱은 메인 창 등록 뒤) 사유 툴팁을 다시 계산한다 — 붙기 전 '실행 불가'로 읽힌 값이 남지 않게
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Vm?.WindowMenu.RefreshTips(), DispatcherPriority.Background);
    }

    // 열 때마다 열린 창 목록·사유 툴팁을 다시 채운다 — 이름 변경·호스트 상태 변화처럼 알림이 없는 변화도 반영
    private void OnSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.Source, this) && Vm is { } vm)
        {
            vm.WindowMenu.OpenWindows.Refresh();
            vm.WindowMenu.RefreshTips();
            RefreshSnapMenu(vm);
            SyncActiveChecks();
        }
    }

    // 열린 창 칸의 ✓(토글 칸)를 실제 활성 값으로 맞춘다. 메뉴는 CheckBox 항목을 누르면 IsChecked 를 SetCurrentValue 로 뒤집는데,
    // 이미 활성인 창을 다시 고르면 IsActive 가 안 바뀌어 바인딩이 값을 되돌려 주지 않는다. SetCurrentValue 라 OneWay 바인딩은 유지된다
    private void SyncActiveChecks()
    {
        foreach (var item in Items.OfType<MenuItem>())
        {
            if (item.CommandParameter is OpenWindowSlot slot && item.IsChecked != slot.IsActive)
            {
                item.SetCurrentValue(MenuItem.IsCheckedProperty, slot.IsActive);
            }
        }
    }

    // '배치' 하위 메뉴: 옵션에 켜진 칸 종류만 보이고, 칸마다 대상 창에 적용할 수 있을 때만 활성
    private void RefreshSnapMenu(MainViewModel vm)
    {
        var target = SnapTarget(vm);
        var shown = WindowMenuOps.PlacementLayouts.Select(l => l.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SnapMenu.IsVisible = shown.Count > 0;
        foreach (var layout in SnapMenu.Items.OfType<MenuItem>())
        {
            layout.IsVisible = shown.Contains(layout.Tag as string ?? "");
            foreach (var cell in layout.Items.OfType<MenuItem>())
            {
                cell.IsEnabled = target is not null && cell.Tag is string tag && TryParseCell(tag, out var layoutId, out var index)
                    && WindowMenuOps.CanApplyCell(target, layoutId, index);
            }
        }
    }

    private void OnSnapCellClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && TryParseCell(tag, out var layoutId, out var index) && Vm is { } vm && SnapTarget(vm) is { } target)
        {
            Dispatcher.UIThread.Post(() => WindowMenuOps.ApplyCell(target, layoutId, index));
        }
    }

    // 대상: 활성 문서(MDI·독립 창 구분은 WindowMenuOps 가 판별), 없으면 메인 창
    private WindowMenuTarget? SnapTarget(MainViewModel vm) =>
        vm.ActiveDocument is { } doc ? new WindowMenuTarget(WindowMenuKind.Mdi, doc, null)
        : TopLevel.GetTopLevel(this) is Window main ? new WindowMenuTarget(WindowMenuKind.Main, null, main)
        : null;

    // '창 전체 보기'(REQ-104) — 단축키·원격과 같은 요청 경로로 전체 보기 덮개를 켜고 끈다(기준 화면은 MainView 가 등록한 문서 영역). 메뉴가 닫힌 뒤 열리게 Post
    private void OnOverviewClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(xkxk.frameworks.ui.shell.Theming.WindowOverviewHost.Request);
    }

    private static bool TryParseCell(string tag, out string layoutId, out int index)
    {
        var parts = tag.Split('|');
        layoutId = parts[0];
        index = 0;
        return parts.Length == 2 && int.TryParse(parts[1], out index);
    }
}
