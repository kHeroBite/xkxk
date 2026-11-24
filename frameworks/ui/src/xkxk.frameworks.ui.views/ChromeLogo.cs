// 문서 창 제목줄 왼쪽 로고 버튼 — 클릭하면 가장 가까운 상위 요소의 창 메뉴(ContextMenu)를 연다(Mars 로고 18×22)
using xkxk.frameworks.ui.shell;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>제목줄 왼쪽 로고(Mars 18×22): 클릭하면 가장 가까운 상위 요소의 창 메뉴(ContextMenu)를 연다.</summary>
public sealed class ChromeLogo : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    // Button 이 눌림을 처리해 창 쪽 활성화 처리기에 닿지 않으므로, MDI·웹 관리형 창에서는 눌림 때 그 창을 먼저 활성으로 만든다(Windows 시스템 아이콘 동작).
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && this.FindAncestorOfType<MdiDocumentWindow>() is { } window
            && WindowCommands.FindDocument(window.DataContext) is { } doc
            && App.Shell is IShellCommands shell)
        {
            shell.Activate(doc);
        }

        base.OnPointerPressed(e);
    }

    protected override void OnClick()
    {
        base.OnClick();
        for (var c = this.GetVisualParent() as Control; c is not null; c = c.GetVisualParent() as Control)
        {
            if (c.ContextMenu is { } menu)
            {
                menu.Open(c);
                return;
            }
        }
    }
}
