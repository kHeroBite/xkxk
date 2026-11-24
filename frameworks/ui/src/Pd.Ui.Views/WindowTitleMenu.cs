// 창 메뉴 공통 컴포넌트 — ContextMenu 에 붙이는 속성 하나로 메인·SDI·MDI 제목 줄과 제목 탭에 같은 항목을 넣고, 열릴 때마다 대상 창의 상태로 활성 여부를 맞춘다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;

namespace Pd.Ui.Views;

/// <summary>
/// 항목 순서는 Windows 시스템 메뉴와 같다(이전 크기로·최소화·최대화·닫기). '이동·크기 조정'은 넣지 않는다. 끌기와 '기본 위치로 이동'이 대신하고, 키보드 이동 모드는 MDI 에 없어 위치마다 동작이 달라진다.
/// Avalonia ContextMenu.Opening 은 포인터 경로에서만, 그것도 대상이 정해지기 전에 온다(디컴파일 12.1.4). 로고·단축키의 Open() 도 함께 받으려고 Opened 에서 맞춘다.
/// </summary>
public sealed class WindowTitleMenu : AvaloniaObject
{
    /// <summary>이 메뉴가 붙는 위치. axaml: views:WindowTitleMenu.Kind="Main".</summary>
    public static readonly AttachedProperty<WindowMenuKind> KindProperty =
        AvaloniaProperty.RegisterAttached<WindowTitleMenu, ContextMenu, WindowMenuKind>("Kind");

    private static readonly (WindowMenuAction Action, string Key, string IdSuffix)[] Items =
    {
        (WindowMenuAction.Restore, "chrome.winmenu.restore", "Restore"),
        (WindowMenuAction.Minimize, "chrome.winmenu.minimize", "Minimize"),
        (WindowMenuAction.Maximize, "chrome.winmenu.maximize", "Maximize"),
        (WindowMenuAction.MoveToDefault, "chrome.winmenu.moveDefault", "MoveDefault"),
        (WindowMenuAction.Close, "chrome.winmenu.close", "Close"),
    };

    // 약한 키 표: 메뉴(와 그 제목 줄·창)가 닫혀 사라지면 항목 목록도 함께 사라진다. 강한 Dictionary 는 지우는 곳이 없어
    // 닫힌 SDI 창의 메뉴 항목 → SdiTitleBar → 창 시각 트리 전체를 붙잡았다(A11 힙 덤프 gcroot, 분리 12회 = 남은 SdiWindowHost 12개)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContextMenu, List<(MenuItem Item, WindowMenuAction Action, string Key)>> s_built = new();

    static WindowTitleMenu() => KindProperty.Changed.AddClassHandler<ContextMenu>((menu, e) =>
    {
        if (e.NewValue is WindowMenuKind kind && kind != WindowMenuKind.None && !s_built.TryGetValue(menu, out _))
        {
            Build(menu, kind);
        }
    });

    private WindowTitleMenu()
    {
    }

    public static void SetKind(ContextMenu menu, WindowMenuKind value) => menu.SetValue(KindProperty, value);

    public static WindowMenuKind GetKind(ContextMenu menu) => menu.GetValue(KindProperty);

    private static void Build(ContextMenu menu, WindowMenuKind kind)
    {
        var own = new List<(MenuItem, WindowMenuAction, string)>();
        var index = 0;
        foreach (var (action, key, suffix) in Items)
        {
            if (action == WindowMenuAction.Close)
            {
                menu.Items.Insert(index++, new Separator());
            }

            var item = new MenuItem { Header = Loc.Tr(key), InputGesture = Gesture(kind, action) };
            AutomationProperties.SetAutomationId(item, IdPrefix(kind) + suffix);
            item.Click += (_, _) => OnClick(menu, action);
            menu.Items.Insert(index++, item);
            own.Add((item, action, key));
        }

        if (kind != WindowMenuKind.Main)
        {
            menu.Items.Insert(index, new Separator()); // 위치별 추가 항목과 나눈다(SDI 합치기·탭 이름 변경·MDI 분리와 배치)
        }

        s_built.AddOrUpdate(menu, own);
        menu.Opened += (_, _) => OnOpened(menu, kind);
        menu.Closed += (_, _) => OnClosed(menu, kind);
    }

    /// <summary>자동화 id. 메인·탭은 기존 id(shell.sys*·shell.ctx*)를 유지한다.</summary>
    private static string IdPrefix(WindowMenuKind kind) => kind switch
    {
        WindowMenuKind.Main => "shell.sys",
        WindowMenuKind.Tab => "shell.ctx",
        WindowMenuKind.Sdi => "chrome.sdiSys",
        _ => "chrome.mdiSys",
    };

    /// <summary>표시만 하는 단축키. 실제 처리는 OS·MainView·WindowKeyGestures 가 맡는다. 웹은 브라우저 예약키라(W-04) 표시하지 않는다.</summary>
    private static KeyGesture? Gesture(WindowMenuKind kind, WindowMenuAction action)
    {
        if (action != WindowMenuAction.Close || OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid())
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return kind == WindowMenuKind.Main ? null : new KeyGesture(Key.W, KeyModifiers.Meta);
        }

        return kind == WindowMenuKind.Main ? new KeyGesture(Key.F4, KeyModifiers.Alt) : new KeyGesture(Key.F4, KeyModifiers.Control);
    }

    private static void OnOpened(ContextMenu menu, WindowMenuKind kind)
    {
        var target = Resolve(menu, kind);
        menu.Tag = target;
        var facts = WindowMenuOps.Facts(target);
        var applies = kind == WindowMenuKind.Main || target.Document is not null || target.PanelWindow is not null; // MDI 메뉴는 관리형 도구 창에도 열린다. 문서도 분리 창도 아니면 창 항목을 숨긴다
        if (!s_built.TryGetValue(menu, out var own))
        {
            return;
        }

        foreach (var (item, action, key) in own)
        {
            item.Header = Loc.Tr(key); // 언어 전환 반영
            item.IsEnabled = facts.IsEnabled(action);
            item.IsVisible = applies;
        }

        foreach (var separator in menu.Items.OfType<Separator>().Take(kind == WindowMenuKind.Main ? 1 : 2))
        {
            separator.IsVisible = applies;
        }

        if (kind == WindowMenuKind.Tab)
        {
            // 탭 메뉴의 나머지 항목(다른 창 닫기·이름 변경·독립 창 전환·핀)도 우클릭한 탭에 적용한다
            foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Command is not null))
            {
                item.CommandParameter = target.Document;
            }
        }
    }

    private static void OnClosed(ContextMenu menu, WindowMenuKind kind)
    {
        // 위치 지정 열기(WindowSystemMenu)를 다음 우클릭까지 끌고 가지 않는다
        menu.Placement = PlacementMode.Pointer;
        menu.PlacementRect = null;
        if (kind == WindowMenuKind.Tab)
        {
            WindowMenuOps.TabTarget = null;
        }
    }

    private static void OnClick(ContextMenu menu, WindowMenuAction action)
    {
        if (menu.Tag is WindowMenuTarget target)
        {
            // 메뉴가 닫히고 포커스가 돌아온 뒤에 실행한다(활성화·창 닫기가 닫히는 팝업과 겹치지 않게)
            Dispatcher.UIThread.Post(() => WindowMenuOps.Run(target, action));
        }
    }

    private static WindowMenuTarget Resolve(ContextMenu menu, WindowMenuKind kind)
    {
        var placement = (menu.Parent as Popup)?.PlacementTarget;
        return kind switch
        {
            WindowMenuKind.Main => new WindowMenuTarget(kind, null, placement is null ? null : TopLevel.GetTopLevel(placement) as Window),
            WindowMenuKind.Sdi => placement?.FindAncestorOfType<SdiTitleBar>(includeSelf: true) is { } bar
                ? new WindowMenuTarget(kind, bar.Document, null, bar.PanelOnlyWindow)
                : new WindowMenuTarget(kind, null, null),
            WindowMenuKind.Tab => new WindowMenuTarget(kind, WindowMenuOps.TabTarget ?? App.Shell?.ActiveDocument, null),
            _ => WindowCommands.FindDocument(menu.DataContext) is { } doc
                ? new WindowMenuTarget(kind, doc, null)
                : new WindowMenuTarget(kind, null, null, (menu.DataContext as ManagedDockWindowDocument)?.Window),
        };
    }
}
