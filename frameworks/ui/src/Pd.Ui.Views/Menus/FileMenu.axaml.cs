// 제목줄 '파일' 상위 메뉴 — 문서 열기(MDI·독립 창)·벤치 열기·레이아웃 저장/복원/관리·화면 캡처 방지·종료 코드 비하인드
using Pd.Poc.Shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pd.Ui.Views.Menus;

/// <summary>제목줄 '파일' 상위 메뉴 — 문서 열기(MDI·독립 창)·벤치 열기·레이아웃 저장/복원/관리·화면 캡처 방지·종료. MainTitleBar.MainMenu 의 직속 MenuItem 이라 더보기 재배치(ReflowMenu)·키보드 탐색이 그대로 동작한다.</summary>
public partial class FileMenu : MenuItem
{
    // 파생 형식은 기본 MenuItem 테마를 찾지 못하므로 테마 키를 MenuItem 으로 둔다(ChromeLogo 와 같은 방식)
    protected override Type StyleKeyOverride => typeof(MenuItem);

    public FileMenu() => InitializeComponent();

    /// <summary>종료 항목은 최상위가 데스크톱 창일 때만 보인다(웹·Android 의 TopLevel 은 Window 가 아니다).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ExitItem.IsVisible = ExitSeparator.IsVisible = TopLevel.GetTopLevel(this) is Window;
    }

    // 메인 창 닫기와 같은 경로 — App 종료 처리(자동 저장·설정 플러시)를 그대로 탄다(MainTitleBar.OnClose 와 같다)
    private void OnExit(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
