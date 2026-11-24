// 문서 영역 탭 우클릭 메뉴에 §7.2 창 메뉴 공통 항목(이전 크기로·최소화·최대화·기본 위치로 이동·닫기)을 붙이고, 우클릭이 탭을 활성화하지 않게 한다
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 항목 생성·활성 규칙·실행은 WindowTitleMenu(Kind=Mdi: 대상은 메뉴 DataContext 의 문서)가 맡는다 — 새 이동·실행 경로를 만들지 않는다.
/// Dock 기본 탭 메뉴(분할 해제 포함)는 그대로 두고 맨 앞에 공통 항목만 덧붙이므로 분할 해제 항목이 유지된다.
/// 도구 패널 탭은 문서가 아니라 WindowTitleMenu 가 공통 항목을 숨긴다.
/// </summary>
internal static class DocumentTabMenu
{
    private static readonly ConditionalWeakTable<DocumentTabStripItem, object> s_guarded = new();
    private static bool s_installed;

    /// <summary>PanelPositionMenu.Install 이 부른다(탭이 만들어지기 전).</summary>
    internal static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        DocumentTabStripItem.DocumentContextMenuProperty.Changed.AddClassHandler<DocumentTabStripItem>((item, e) =>
        {
            if (e.NewValue is not ContextMenu menu)
            {
                return;
            }

            if (WindowTitleMenu.GetKind(menu) == WindowMenuKind.None)
            {
                WindowTitleMenu.SetKind(menu, WindowMenuKind.Mdi);
            }

            if (!s_guarded.TryGetValue(item, out _))
            {
                s_guarded.Add(item, new object());
                // 우클릭 누름이 탭을 선택·활성화하지 않게 한다(최소화 문서 복원·독립 창 앞으로 나옴 방지, MainView 제목 탭과 같다). 메뉴는 뗌에서 열린다.
                item.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
        });
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Avalonia.Visual).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
        }
    }
}
