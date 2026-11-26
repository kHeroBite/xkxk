// 최대화·이전 크기 단추 호버 처리기 — 포인터가 단추 위에 SnapFlyoutDelayMs 머무르면 단추 아래에 배치 칸 팝업을 띄우고, 칸을 누르면 WindowMenuOps.ApplyCell 로 배치한다(사용자 요구 2026-11-11 16:58)
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>
/// axaml: 최대화 단추에 <c>snap:SnapFlyoutHover.Enabled="True"</c>. MDI 문서 창·웹 관리형 창(MdiDocumentWindowTheme)·SDI 제목줄(SdiTitleBar)·메인 제목줄(MainTitleBar)이 쓴다.
/// 띄우지 않는 대상은 BlockReason(SnapFlyoutHover.Target.cs)에서 판정한다(SDI Off 모드, Windows 메인 창).
/// 규칙: 지연 전에 단추를 벗어나면 띄우지 않는다. 뜬 뒤에는 단추↔팝업 사이 이동 동안 닫지 않고(유예 LeaveGraceMs), 둘 다 벗어나거나 Esc·바깥 클릭이면 닫는다.
/// 단추 클릭(최대화·복원)은 그대로 둔다 — 누르면 지연·팝업만 거두고 이벤트는 소비하지 않는다. 팝업이 떠 있을 때 바깥 클릭도 그대로 통과시킨다(OverlayDismissEventPassThrough).
/// 터치 입력은 띄우지 않는다. 대상에 적용 가능한 칸이 하나도 없으면(위치 고정·DOCK·NAV·기준 영역 없음) 띄우지 않는다.
/// 상태는 SnapFlyoutState(셸 층)에 써서 원격 명령이 읽게 한다. 모든 처리는 UI 스레드.
/// </summary>
public static partial class SnapFlyoutHover
{
    /// <summary>단추↔팝업 사이를 지나가는 동안 닫지 않고 기다리는 시간(ms, 초기 제시값).</summary>
    public const int LeaveGraceMs = 300;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("Enabled", typeof(SnapFlyoutHover));

    private static readonly List<WeakReference<Button>> s_buttons = new();
    private static readonly DispatcherTimer s_delay = new() { IsEnabled = false };
    private static readonly DispatcherTimer s_grace = new() { Interval = TimeSpan.FromMilliseconds(LeaveGraceMs), IsEnabled = false };
    private static Button? s_button;
    private static Flyout? s_flyout;
    private static SnapLayoutFlyout? s_content;
    private static TopLevel? s_keyTop;
    private static InputElement? s_presenter;

    static SnapFlyoutHover()
    {
        EnabledProperty.Changed.AddClassHandler<Button>(OnEnabledChanged);
        s_delay.Tick += (_, _) =>
        {
            s_delay.Stop();
            if (!s_simulated && !ReferenceEquals(s_hovered, s_button))
            {
                CancelPending("left-before-delay"); // 이탈 통지를 못 받았어도 지연 끝에 포인터가 단추에 없으면 띄우지 않는다
                return;
            }

            OpenFor(s_button);
        };
        InstallPointer();
        s_grace.Tick += (_, _) => { s_grace.Stop(); CloseIfOutside(); };
        SnapFlyoutState.Hover = SimulateHover;
        SnapFlyoutState.Leave = () => { s_simulated = false; CancelPending("left-before-delay"); Close(); };
        SnapFlyoutState.Pick = Pick;
        SnapFlyoutState.Close = Close;
    }

    public static bool GetEnabled(Button b) => b.GetValue(EnabledProperty);

    public static void SetEnabled(Button b, bool value) => b.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Button b, AvaloniaPropertyChangedEventArgs e)
    {
        // 진입·이탈은 단추의 PointerEntered/Exited 가 아니라 TopLevel 포인터 이동으로 판정한다(SnapFlyoutHover.Pointer.cs — 웹 실마우스 AC-025 FAIL 대응)
        if (e.NewValue is not true)
        {
            b.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            return;
        }

        b.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        s_buttons.RemoveAll(w => !w.TryGetTarget(out _));
        s_buttons.Add(new WeakReference<Button>(b));
    }

    /// <summary>단추 누름: 최대화·복원은 그대로 진행되게 두고(소비하지 않음) 지연·팝업만 거둔다.</summary>
    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelPending("pressed");
        Close();
    }

    /// <summary>머무름 지연 시작. 옵션이 꺼졌거나 대상이 없으면 시작하지 않는다.</summary>
    private static void Start(Button b)
    {
        Close();
        if (!SnapFlyoutState.Enabled)
        {
            SnapFlyoutState.LastSkipReason = "disabled";
            return;
        }

        if (ResolveTarget(b) is not { } target)
        {
            SnapFlyoutState.LastSkipReason = "no-target";
            return;
        }

        if (BlockReason(target) is { } blocked)
        {
            SnapFlyoutState.LastSkipReason = blocked;
            return;
        }

        s_button = b;
        SnapFlyoutState.Target = target;
        SnapFlyoutState.Pending = true;
        var delay = XkxkUi.Current.Settings.SnapFlyoutDelayMs;
        if (delay <= 0)
        {
            s_delay.Stop();
            OpenFor(b);
            return;
        }

        s_delay.Interval = TimeSpan.FromMilliseconds(delay);
        s_delay.Stop();
        s_delay.Start();
    }

    private static void CancelPending(string reason)
    {
        if (SnapFlyoutState.Pending)
        {
            SnapFlyoutState.LastSkipReason = reason;
        }

        s_delay.Stop();
        SnapFlyoutState.Pending = false;
        if (!SnapFlyoutState.IsOpen)
        {
            SnapFlyoutState.Target = null;
            s_button = null;
        }
    }

    private static void OpenFor(Button? b)
    {
        SnapFlyoutState.Pending = false;
        if (b is null || !b.IsEffectivelyVisible || !SnapFlyoutState.Enabled || ResolveTarget(b) is not { } target)
        {
            SnapFlyoutState.LastSkipReason = "no-target";
            return;
        }

        if (!SnapFlyoutState.HasApplicableCell(target))
        {
            SnapFlyoutState.LastSkipReason = "no-cell";
            return;
        }

        s_content ??= CreateContent();
        s_content.SetLayouts(SnapFlyoutState.Layouts, (id, i) => WindowMenuOps.CanApplyCell(target, id, i));
        s_flyout ??= new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Transient, // 초점을 가져가지 않는다(창 활성·키 입력 유지)
            OverlayDismissEventPassThrough = true, // 바깥 클릭(단추 클릭 포함)은 닫기만 하고 원래 대상에도 전달
            Content = s_content,
        };
        s_flyout.Closed -= OnFlyoutClosed;
        s_flyout.Closed += OnFlyoutClosed;
        s_flyout.OverlayInputPassThroughElement = b;
        s_button = b;
        SnapFlyoutState.Target = target;
        SnapFlyoutState.Highlighted = (null, -1);
        s_flyout.ShowAt(b);
        HookPresenter(s_content.Parent as InputElement);
        SnapFlyoutState.IsOpen = true;
        SnapFlyoutState.LastSkipReason = null;
        s_keyTop = TopLevel.GetTopLevel(b);
        s_keyTop?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PdLog.Info("snap-flyout", $"open kind={target.Kind} doc={target.Document?.Id}");
    }

    private static SnapLayoutFlyout CreateContent()
    {
        var content = new SnapLayoutFlyout();
        content.CellHovered += (id, i) => SnapFlyoutState.Highlighted = (id, i);
        content.CellPicked += (id, i) => Pick(id, i);
        content.PointerEntered += (_, _) => s_grace.Stop();
        content.PointerExited += (_, _) => s_grace.Start();
        return content;
    }

    /// <summary>칸 클릭과 원격 snapflyout.pick 의 공통 경로. 팝업을 닫은 뒤 배치한다(닫히는 팝업과 활성화가 겹치지 않게 Post).</summary>
    private static bool Pick(string layoutId, int cell)
    {
        if (!SnapFlyoutState.IsOpen || SnapFlyoutState.Target is not { } target || !WindowMenuOps.CanApplyCell(target, layoutId, cell))
        {
            return false;
        }

        Close();
        // 배치 뒤 남은 칸 창 고르기(Snap Assist)를 띄운다 — 판정·범위는 ApplyCell 이 한다(계획 §5.3, Windows 메인 창은 BlockReason 에서 이미 빠짐)
        Dispatcher.UIThread.Post(() => WindowMenuOps.ApplyCell(target, layoutId, cell, SnapAssistSource.Flyout));
        PdLog.Info("snap-flyout", $"pick {layoutId}#{cell} kind={target.Kind} doc={target.Document?.Id}");
        return true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SnapFlyoutState.IsOpen)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>팝업 틀(FlyoutPresenter — 안쪽 여백 포함)의 포인터 진입·이탈도 유예 판정에 넣는다. 틀은 처음 열 때 한 번 생긴다.</summary>
    private static void HookPresenter(InputElement? presenter)
    {
        if (presenter is null || ReferenceEquals(presenter, s_presenter))
        {
            return;
        }

        s_presenter = presenter;
        presenter.PointerEntered += (_, _) => s_grace.Stop();
        presenter.PointerExited += (_, _) => s_grace.Start();
    }

    /// <summary>유예가 끝났을 때 포인터가 단추에도 팝업(틀 포함)에도 없으면 닫는다.</summary>
    private static void CloseIfOutside()
    {
        // 포인터 판정(s_hovered·s_inPopup, Pointer.cs)이 주 판정이고 IsPointerOver 는 보조로 '머무름' 쪽에만 쓴다. 원격 hover 로 연 팝업은 원격 leave·close 로만 닫는다
        if (SnapFlyoutState.IsOpen && !s_simulated && s_hovered is null && !s_inPopup
            && s_content?.IsPointerInside != true && s_presenter?.IsPointerOver != true)
        {
            Close();
        }
    }

    private static void Close()
    {
        s_grace.Stop();
        if (s_flyout is { IsOpen: true })
        {
            s_flyout.Hide(); // OnFlyoutClosed 가 상태를 지운다
        }
        else
        {
            OnFlyoutClosed(null, EventArgs.Empty);
        }
    }

    private static void OnFlyoutClosed(object? sender, EventArgs e)
    {
        s_keyTop?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        s_keyTop = null;
        var wasOpen = SnapFlyoutState.IsOpen;
        SnapFlyoutState.IsOpen = false;
        SnapFlyoutState.Highlighted = (null, -1);
        if (!SnapFlyoutState.Pending)
        {
            SnapFlyoutState.Target = null;
            s_button = null;
        }

        if (wasOpen)
        {
            PdLog.Info("snap-flyout", "close");
        }
    }
}
