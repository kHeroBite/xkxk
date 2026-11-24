// 시스템 메뉴(UI-17) 창 공용 열기 — 데스크톱은 이름별 별도 창 1개(이미 열려 있으면 활성화), 브라우저·Android(단일 뷰)는 호출 위치 아래 플라이아웃
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 시스템 메뉴 창을 연다. 같은 이름의 창은 하나만 둔다(테마 편집기 방식과 같음).
/// 창이 닫히면 내용의 DataContext 가 IDisposable 이면 Dispose 해 언어·타이머 구독을 푼다.
/// </summary>
public static class SystemDialogHost
{
    private static readonly Dictionary<string, Window> Windows = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Flyout> Flyouts = new(StringComparer.Ordinal);
    private static bool? s_activateOnce;

    /// <summary>
    /// 비전경(화면 밖) 시험 모드. 데스크톱 호스트가 --bg-pos 로 기동했을 때 켠다(Program.cs — odev-4b).
    /// 켜져 있으면 시스템 창을 활성화하지 않고(ShowActivated=false) 소유 창 기준 위치(화면 밖)에 두며,
    /// 탐색기·파일 대화상자 같은 OS 창은 열지 않고 '열었을 경로'만 기록한다(SystemViews.LastSuppressed).
    /// 꺼져 있으면(제품 기본) 정상 활성화·소유 창 가운데다.
    /// </summary>
    public static bool BackgroundMode { get; set; }

    /// <summary>다음 한 번의 창 열기에만 쓰는 활성화 여부(원격 sys.open activate= — 기본 false). 창을 열면 지워지고, null 을 주면 바로 지운다.</summary>
    public static void SetActivateOnce(bool? activate) => s_activateOnce = activate;

    /// <summary>
    /// name 은 창 식별자(AutomationId 접미사로도 쓴다), titleKey 는 제목 사전 키, create 는 내용을 만드는 함수다.
    /// anchor 가 null 이면 메인 창(데스크톱)·메인 뷰(단일 뷰) 기준이다.
    /// </summary>
    public static void Open(string name, string titleKey, Func<Control> create, Control? anchor = null, double width = 560, double height = 480)
    {
        var lifetime = Application.Current?.ApplicationLifetime;
        var hasCallerAnchor = anchor is not null;
        anchor ??= lifetime switch
        {
            IClassicDesktopStyleApplicationLifetime d => d.MainWindow,
            ISingleViewApplicationLifetime s => s.MainView,
            _ => null,
        };

        if (lifetime is IClassicDesktopStyleApplicationLifetime)
        {
            OpenWindow(name, titleKey, create, anchor, width, height);
            return;
        }

        s_activateOnce = null; // 단일 뷰 플라이아웃은 OS 창이 아니라 활성화 여부와 무관하다

        if (anchor is null)
        {
            return;
        }

        var content = create();
        Avalonia.Automation.AutomationProperties.SetAutomationId(content, "sys.dlg." + name);
        Close(name); // 같은 이름의 플라이아웃이 열려 있으면 닫고 새로 연다
        var flyout = FittedFlyout.Create(content, anchor, hasCallerAnchor, width, height, fixedHeight: false);
        flyout.Closed += (_, _) =>
        {
            if (Flyouts.TryGetValue(name, out var current) && ReferenceEquals(current, flyout))
            {
                Flyouts.Remove(name); // 닫힘 알림이 늦게 와도 새로 연 같은 이름 플라이아웃은 지우지 않는다
            }

            (content.DataContext as IDisposable)?.Dispose();
        };
        Flyouts[name] = flyout;
        flyout.ShowAt(anchor);
    }

    /// <summary>지금 열려 있는 시스템 창 이름 목록(시험 판정용 — 원격 명령 sys.get 이 읽는다).</summary>
    public static IReadOnlyCollection<string> OpenNames
    {
        get
        {
            var names = new SortedSet<string>(Windows.Keys, StringComparer.Ordinal);
            names.UnionWith(Flyouts.Keys);
            return names;
        }
    }

    /// <summary>열려 있는 시스템 창의 내용 DataContext(시험 판정용). 없으면 null.</summary>
    public static object? ContentOf(string name) =>
        Windows.TryGetValue(name, out var w) ? (w.Content as Control)?.DataContext
        : Flyouts.TryGetValue(name, out var f) ? (f.Content as Control)?.DataContext
        : null;

    /// <summary>열려 있는 시스템 창(데스크톱 창 또는 단일 뷰 플라이아웃)을 닫는다. 없으면 아무것도 하지 않는다.</summary>
    public static void Close(string name)
    {
        if (Windows.TryGetValue(name, out var w))
        {
            w.Close();
        }

        if (Flyouts.TryGetValue(name, out var f))
        {
            f.Hide();
        }
    }

    private static void OpenWindow(string name, string titleKey, Func<Control> create, Control? anchor, double width, double height)
    {
        var activate = s_activateOnce ?? !BackgroundMode;
        s_activateOnce = null;
        if (Windows.TryGetValue(name, out var existing))
        {
            if (activate)
            {
                existing.Activate();
            }

            return;
        }

        var content = create();
        var owner = TopLevel.GetTopLevel(anchor) as Window;
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            ShowActivated = activate,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (!activate && owner is not null)
        {
            // 비활성 열기: 소유 창(시험 중이면 화면 밖) 좌상단 기준에 둔다 — 가운데 배치가 화면 안 좌표로 보정되며 사용자 화면에 나타나는 것을 피한다
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = owner.Position + new PixelPoint(40, 40);
        }

        Avalonia.Automation.AutomationProperties.SetAutomationId(window, "sys.dlg." + name);
        window.Bind(Window.TitleProperty, Loc.Instance.Observe(titleKey)); // 언어 전환 시 제목도 즉시 바뀐다
        window.Closed += (_, _) =>
        {
            Windows.Remove(name);
            (content.DataContext as IDisposable)?.Dispose();
        };
        Windows[name] = window;
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
