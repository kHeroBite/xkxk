// 시스템 메뉴(UI-17) 창 공용 열기 — 데스크톱은 이름별 별도 창 1개(이미 열려 있으면 활성화), 브라우저·Android(단일 뷰)는 호출 위치 아래 플라이아웃. 창 아이콘 키·앱 모달(설정 창) 선택
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

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
    /// iconKey 가 있으면 데스크톱 창 아이콘을 그 글리프로 단다(MenuIconBitmap — 메뉴 아이콘과 같은 키, 단일 뷰는 OS 창이 없어 무시).
    /// modal 이면 앱 모달이다(03_2 §8.2): 데스크톱은 ShowDialog(owner) + ModalScope 로 메인·MDI·SDI·다른 시스템 창을 막고,
    /// 단일 뷰는 Flyout 바깥 누름 닫기를 끄고 ModalScope 로 내용 밖 입력을 막는다(Esc·닫기 단추·뒤로 가기로만 닫힘).
    /// </summary>
    public static void Open(string name, string titleKey, Func<Control> create, Control? anchor = null, double width = 560, double height = 480,
        string? iconKey = null, bool modal = false)
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
            OpenWindow(name, titleKey, create, anchor, width, height, iconKey, modal);
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
        IDisposable? modalScope = null;
        if (modal)
        {
            flyout.Popup.IsLightDismissEnabled = false; // 바깥 누름으로 닫히지 않는다(PopupFlyoutBase 는 팝업 생성 때만 true 로 정하고 ShowAt 에서 다시 정하지 않는다)
            // 실제로 열린 뒤에만 범위를 연다 — 열리지 못한 Flyout 은 Closed 가 오지 않아 범위가 남을 수 있다. 내용이 화면에서 떨어져도 범위가 스스로 끝난다(이중 보장)
            flyout.Opened += (_, _) => modalScope ??= ModalScope.Begin(content, name);
        }

        flyout.Closed += (_, _) =>
        {
            modalScope?.Dispose();
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

    private static void OpenWindow(string name, string titleKey, Func<Control> create, Control? anchor, double width, double height,
        string? iconKey, bool modal)
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
        window.Bind(Window.TitleProperty, XkxkUi.Current.Localization.Observe(titleKey)); // 언어 전환 시 제목도 즉시 바뀐다
        if (iconKey is not null)
        {
            MenuIconBitmap.Apply(window, iconKey); // 메뉴 아이콘과 같은 키 — AppWindowIcon 은 Icon 이 이미 있으면 건드리지 않는다
        }

        IDisposable? modalScope = null;
        window.Closed += (_, _) =>
        {
            modalScope?.Dispose(); // 어떤 경로로 닫혀도(닫기 단추·Esc·Alt+F4·앱 종료) 범위를 끝낸다 — ModalScope 도 Closed 를 따로 받는다(이중 보장)
            Windows.Remove(name);
            (content.DataContext as IDisposable)?.Dispose();
        };
        Windows[name] = window;
        if (modal)
        {
            modalScope = ShowModal(window, owner, name);
        }
        else if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// 앱 모달로 띄운다. 보이는 소유 창이 있으면 ShowDialog(owner)(OS 가 owner 를 막음), 없으면(메인 숨김·트레이) 소유 관계 없이 Show.
    /// 어느 쪽이든 ModalScope 가 나머지 창(SDI·다른 시스템 창, owner 없을 때는 메인 포함)을 막는다.
    /// </summary>
    private static IDisposable? ShowModal(Window window, Window? owner, string name)
    {
        if (owner is { IsVisible: true })
        {
            try
            {
                _ = window.ShowDialog(owner);
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[경고][모달] ShowDialog 실패, 일반 표시로 대체: {ex.Message}");
                if (!window.IsVisible)
                {
                    window.Show();
                }
            }
        }
        else
        {
            window.Show();
        }

        try
        {
            return ModalScope.Begin(window, name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[경고][모달] 모달 범위 시작 실패: {ex.Message}");
            return null;
        }
    }
}
