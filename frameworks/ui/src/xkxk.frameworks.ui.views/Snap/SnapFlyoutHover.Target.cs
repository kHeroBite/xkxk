// 최대화 단추 호버 처리기의 대상 판정 — 단추가 속한 창을 창 메뉴 대상(WindowMenuTarget)으로 바꾸고, 원격 snapflyout.hover 가 대상 창의 단추를 찾는다
using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Snap;

public static partial class SnapFlyoutHover
{
    /// <summary>단추가 속한 창 → 창 메뉴 대상(WindowTitleMenu.Resolve 와 같은 규칙). SDI 제목줄이면 SDI, MDI 테마면 문서 또는 관리형 창.</summary>
    private static WindowMenuTarget? ResolveTarget(Control button)
    {
        if (button.FindAncestorOfType<MainTitleBar>() is not null)
        {
            return TopLevel.GetTopLevel(button) is Window main ? new WindowMenuTarget(WindowMenuKind.Main, null, main) : null;
        }

        if (button.FindAncestorOfType<SdiTitleBar>() is { } bar)
        {
            return bar.Document is null && bar.PanelOnlyWindow is null ? null : new WindowMenuTarget(WindowMenuKind.Sdi, bar.Document, null, bar.PanelOnlyWindow);
        }

        var context = button.FindAncestorOfType<MdiDocumentWindow>()?.DataContext ?? button.DataContext;
        if (context is ManagedDockWindowDocument managed)
        {
            // 웹·Android 관리형 창 = SDI(분리 창). 문서가 있으면 문서로, 패널만 남은 창이면 창으로(SdiTitleBar 와 같은 규칙, AC-025 kind 보고 정정)
            return WindowCommands.FindDocument(managed) is { } sdiDoc ? new WindowMenuTarget(WindowMenuKind.Sdi, sdiDoc, null)
                : managed.Window is { } window ? new WindowMenuTarget(WindowMenuKind.Sdi, null, null, window) : null;
        }

        return WindowCommands.FindDocument(context) is { } doc ? new WindowMenuTarget(WindowMenuKind.Mdi, doc, null) : null;
    }

    /// <summary>
    /// 이 대상에 앱 팝업을 띄우지 않는 이유(없으면 null).
    /// sdi-off: 데스크톱 SDI 이고 SnapSdiMode=Off(앱 스냅 안 씀). Os 모드는 SDI 제목줄이 앱이 그리는 단추라 OS 팝업이 뜨지 않으므로 앱 팝업을 띄운다(team-lead 결정).
    /// os-flyout: Windows 메인 창. 최대화 단추에 ElementRole=MaximizeButton 이 있어 OS(Win11) Snap 메뉴가 그 단추를 맡는다고 보고 겹쳐 띄우지 않는다(MainTitleBar.axaml 주석 근거, 실기 확인 전).
    /// </summary>
    private static string? BlockReason(WindowMenuTarget target) => target.Kind switch
    {
        WindowMenuKind.Sdi when ShellPlatform.IsSdiWindowSupported && XkxkUi.Current.Settings.SnapSdiMode == SnapSdiMode.Off => "sdi-off",
        WindowMenuKind.Main when OperatingSystem.IsWindows() => "os-flyout",
        _ => null,
    };

    /// <summary>원격 snapflyout.hover: 대상 창의 최대화 단추(보이는 것)를 찾아 포인터 진입과 같은 지연을 시작한다. 문서·창 참조로 맞춘다(종류는 보지 않는다).</summary>
    private static bool SimulateHover(WindowMenuTarget target)
    {
        foreach (var weak in s_buttons.ToArray())
        {
            if (!weak.TryGetTarget(out var b) || !b.IsEffectivelyVisible || ResolveTarget(b) is not { } t)
            {
                continue;
            }

            var same = target.Kind == WindowMenuKind.Main ? t.Kind == WindowMenuKind.Main
                : target.Document is { } d ? ReferenceEquals(t.Document, d) : target.PanelWindow is { } w && ReferenceEquals(t.PanelWindow, w);
            if (same)
            {
                Start(b);
                s_simulated = true; // 실제 포인터 이동으로 취소되지 않게(원격 leave·close·pick 으로만 끝난다)
                return true;
            }
        }

        SnapFlyoutState.LastSkipReason = "no-button";
        return false;
    }
}
