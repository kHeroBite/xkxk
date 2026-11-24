// 웹·Android 관리형 독립 창(MdiDocumentWindow + ManagedDockWindowDocument) 제목줄 끌기를 엿보아 MDI 와 같은 규칙으로 칸에 배치하는 처리기 — 기준 영역은 뷰포트 안 관리형 창 레이어(11_창배치_스냅레이아웃.md §2·§3·§5, 04 W-24)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Settings;
using Pd.Client.Data.Logging;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 관리형 창은 MdiDocumentWindow 가 포인터를 캡처해 앱 PointerMoved 로 옮긴다(ManagedSdiTabJoin 주석 — 디컴파일 MdiDocumentWindow.BeginDrag).
/// MdiSnapDrag 는 MDI 창(PocDocument·묶음)만 다루므로 여기서는 DataContext 가 ManagedDockWindowDocument 인 창만 같은 방식(클래스 처리기, handledEventsToo)으로 엿본다.
/// 기준 영역 = 창이 속한 MdiLayoutPanel(관리형 창 레이어, 좌표 = 레이어 DIP = IDockWindow X/Y). 판정 문맥은 끌기 시작 때 1회, 이동마다는 포인터 판정만 한다.
/// 우선순위·옵션·막대 지연·Esc·막대 밖 이동·최소 크기 미달 비활성은 MdiSnapDrag 와 같다. 표시 창구도 같은 ISnapOverlay(SnapOverlayHost.For(레이어 패널), 끌기 시작 때 1회)를 쓴다.
/// 탭 합치기 우선: WindowTabJoinService 가 표적 창에 거는 강조 클래스(MdiTabJoinDrag.TargetClass)로 '지금 표적 위인가' 를 읽는다(서비스에 공개 조회가 없어서).
/// 좌표 쓰기·읽기는 '배치' 메뉴와 같은 ManagedWindowRect(WindowMenuOps.Snap.cs)를 쓴다.
/// 설치: Install() 을 ManagedSdiTabJoin.Install 다음에 부른다 — 같은 이동에서 탭 합치기 판정이 먼저 갱신되게(odev-9 연결). 멱등이다.
/// </summary>
public static class ManagedSnapDrag
{
    private static bool s_installed;
    private static MdiDocumentWindow? s_window;
    private static ManagedDockWindowDocument? s_managed;
    private static Visual? s_area;
    private static ISnapOverlay? s_overlay;
    private static IPointer? s_pointer;
    private static Point s_press;
    private static Point s_last;
    private static Rect s_start;
    private static SnapZoneContext s_context = SnapZoneContext.Disabled;
    private static SnapZone s_zone = SnapZone.None;
    private static List<MdiDocumentWindow> s_others = new();
    private static bool s_dragging;
    private static bool s_cancelled;
    private static bool s_captureLost;
    private static bool s_barVisible;
    private static DispatcherTimer? s_barTimer;

    /// <summary>스냅을 적용했다(창 제목, 구역 종류, 사각형) — 로그·시험 관측용.</summary>
    public static event Action<string, SnapZoneKind, Rect>? Snapped;

    /// <summary>마지막 놓기 판정 한 줄(진단·시험 관측용).</summary>
    public static string LastOutcome { get; private set; } = "none";

    /// <summary>지금 끌기 중 판정(끌기 밖이면 None).</summary>
    public static SnapZone CurrentZone => s_dragging ? s_zone : SnapZone.None;

    /// <summary>클래스 처리기를 1회 등록한다(멱등). 데스크톱(UseManagedWindows=false)에서는 누름 판정에서 바로 돌아간다.</summary>
    public static void Install()
    {
        if (s_installed) { return; }
        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 이동·놓기는 TopLevel 에서도 받는다(웹 AC-014 FAIL: 실끌기 managed-snap 로그 0건 — 이동이 창을 거치지 않을 수 있음). 대상은 누름 때 정한 s_window
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => { if (s_window is { } w) { OnMoved(w, e); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<MdiDocumentWindow>((w, e) => { if (ReferenceEquals(w, s_window)) { OnReleased(e, "window"); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        // 창을 거치지 않은 놓기·캡처 유실·Esc 뒤 놓기는 TopLevel 에서(창 처리기가 먼저 받았으면 Reset 으로 s_managed 가 비어 한 번만 처리)
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => { if (s_managed is not null) { OnReleased(e, "toplevel"); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((w, e) => { if (ReferenceEquals(e.Source, w) && ReferenceEquals(w, s_window)) { OnCaptureLost(); } },
            RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 웹: 포인터가 브라우저 표면 밖으로 나가면(pointerleave) 취소와 같게 시작 위치로(11 문서 §3.7)
        InputElement.PointerExitedEvent.AddClassHandler<TopLevel>(OnExited, RoutingStrategies.Direct, handledEventsToo: true);
    }

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        Reset();
        if (window.DataContext is not ManagedDockWindowDocument managedCtx) { return; } // MDI 창은 MdiSnapDrag 몫
        // 진단(LastPress): 관리형 창 누름마다 조건별 값을 남긴다(snapcell.lastdrag)
        var area = window.GetVisualAncestors().OfType<MdiLayoutPanel>().FirstOrDefault();
        LastPress = $"managed={DockSettings.UseManagedWindows} state={managedCtx.MdiState} window={managedCtx.Window is not null} left={e.GetCurrentPoint(window).Properties.IsLeftButtonPressed}"
            + $" title={PanelJoinTargets.IsTitlePress(e.Source as Visual, window)} src={(e.Source as Control)?.Name ?? e.Source?.GetType().Name} locked={IsLocked(managedCtx)} area={area?.Bounds.Size.ToString() ?? "none"}";
        if (DockSettings.UseManagedWindows && managedCtx is { MdiState: MdiWindowState.Normal, Window: { } dockWindow } managed
            && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed && PanelJoinTargets.IsTitlePress(e.Source as Visual, window) && !IsLocked(managed)
            && area is { Bounds: { Width: > 0, Height: > 0 } })
        {
            (s_window, s_managed, s_area, s_pointer, s_press) = (window, managed, area, e.Pointer, e.GetPosition(area));
            s_start = RectOf(managed, dockWindow);
            LastPress += " → armed";
        }
    }

    /// <summary>마지막 관리형 창 누름 판정(조건별 값·armed)과 끌기 중 받은 이동 수(진단·시험 관측용).</summary>
    public static string LastPress { get; private set; } = "none";
    public static int Moves { get; private set; }

    private static void OnMoved(MdiDocumentWindow window, PointerEventArgs e)
    {
        if (!ReferenceEquals(window, s_window) || s_area is not MdiLayoutPanel area || s_cancelled) { return; }
        Moves++;
        var point = e.GetPosition(area);
        if (!s_dragging)
        {
            if (!TabJoinRules.IsDrag(s_press, point)) { return; }
            s_dragging = true;
            // 끌기 시작 때 1회: 기준 영역(레이어 패널)·옵션·막대·칸 사각형, 탭 합치기 표적 후보(같은 레이어의 다른 관리형 창)
            s_context = UiSettings.Current.CreateSnapContext(new Rect(area.Bounds.Size), WindowMinSize.Current);
            // 패널의 직계 자식은 창 크기 래퍼(ContentPresenter 등)라 후손에서 찾는다 — 관리형 창 래퍼(ManagedDockWindowDocument)만이라 창 안 MDI 창은 섞이지 않는다
            s_others = area.GetVisualDescendants().OfType<MdiDocumentWindow>().Where(w => !ReferenceEquals(w, window) && w.DataContext is ManagedDockWindowDocument).ToList();
            s_overlay = s_context.Enabled ? SnapOverlayHost.For(area) : null; // 화면 트리에 붙은 지금 받는다(MdiSnapDrag 와 같음)
        }

        if (!s_context.Enabled) { return; } // 스냅 끔: 첫 판정에서 바로 돌아간다(§3.7)
        s_last = point;
        Update();
    }

    /// <summary>탭 합치기 표적 위인가 — 서비스가 머무는 표적 창에 거는 강조 클래스로 읽는다.</summary>
    private static bool OverTabJoin() =>
        WindowTabJoinService.IsActive && s_others.Any(w => w.Classes.Contains(MdiTabJoinDrag.TargetClass));

    private static void Update()
    {
        if (s_area is null) { return; }
        s_zone = s_context.Resolve(s_last, s_barVisible, OverTabJoin());
        if (s_zone.WantsBar && !s_barVisible)
        {
            ScheduleBar();
        }
        else if (!s_zone.WantsBar)
        {
            CancelBarTimer();
            if (s_barVisible) { s_barVisible = false; s_overlay?.HideBar(); }
        }

        s_overlay?.Show(s_zone); // 미리보기·칸 강조(TabJoin 이면 전부 숨김)
    }

    /// <summary>위쪽 띠에 든 뒤 SnapBarDelayMs 만큼 머무르면 막대를 띄운다. 포인터·손가락이 멈춰도 뜨게 타이머로 잰다(0 이면 바로).</summary>
    private static void ScheduleBar()
    {
        if (s_barTimer is not null) { return; }
        var delay = UiSettings.Current.SnapBarDelayMs;
        if (delay <= 0) { ShowBarNow(); return; }
        s_barTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
        s_barTimer.Tick += (_, _) => { CancelBarTimer(); ShowBarNow(); };
        s_barTimer.Start();
    }

    private static void ShowBarNow()
    {
        if (!s_dragging || s_cancelled || s_area is null || !s_context.HasBar || !s_zone.WantsBar) { return; }
        s_barVisible = true;
        s_overlay?.ShowBar(s_context);
        Update(); // 막대가 뜬 지금 위치가 칸 위일 수 있다
    }

    private static void CancelBarTimer()
    {
        s_barTimer?.Stop();
        s_barTimer = null;
    }

    private static void OnReleased(PointerReleasedEventArgs e, string via)
    {
        if (s_managed is not { } managed) { return; }
        var (dragging, cancelled, zone, start) = (s_dragging, s_cancelled, s_zone, s_start);
        var grabX = start.Width > 0 ? (s_press.X - start.X) / start.Width : 0.5; // 스냅 해제 때 포인터가 같은 비율 자리를 잡게
        var drop = s_area is { } area ? e.GetPosition(area) : s_last;
        // 마지막 이동 판정이 비었으면 놓은 자리(표면 좌표)로 한 번 더 판정한다(MdiSnapDrag.OnReleased 와 같은 규칙 — 어느 처리기가 받든 같다)
        if (dragging && !cancelled && zone.Kind == SnapZoneKind.None && s_context.Enabled) { zone = s_context.Resolve(drop, s_barVisible, false); }
        var moves = Moves;
        Reset();
        if (!dragging) { LastOutcome = $"{via}:no-drag|moves={moves}"; return; }
        LastOutcome = $"{via}:source={managed.Title}|moves={moves}|zone={zone.Kind}|layout={zone.LayoutId ?? "-"}|cell={zone.Cell}|target={zone.Target}|cancelled={cancelled}";
        PdLog.Info("managed-snap", LastOutcome);
        // Dock 의 놓기 처리(EndDrag)가 끝난 뒤 바꾼다(MdiSnapDrag·ManagedSdiTabJoin 과 같은 이유)
        Dispatcher.UIThread.Post(() => Apply(managed, cancelled, zone, start, grabX, drop));
    }

    private static void Apply(ManagedDockWindowDocument managed, bool cancelled, SnapZone zone, Rect start, double grabX, Point drop)
    {
        if (managed.Window is not { } dockWindow) { LastOutcome += "|apply=skipped"; return; }
        if (cancelled)
        {
            SetRect(dockWindow, start); // Esc·표면 밖: 시작 위치로
            LastOutcome += "|apply=restored";
        }
        else if (zone.Kind != SnapZoneKind.TabJoin && zone.HasTarget)
        {
            if (zone.Kind == SnapZoneKind.TopEdge || (zone.Kind == SnapZoneKind.BarCell && zone.LayoutId == SnapLayoutCatalog.Max))
            {
                SetRect(dockWindow, start); // 최대화 해제 때 끌기 전 자리로
                Maximize(managed, dockWindow);
                LastOutcome += "|apply=maximized";
            }
            else
            {
                SnapRestoreMemory.Remember(dockWindow, start, zone.Target);
                SetRect(dockWindow, zone.Target);
                LastOutcome += "|apply=snapped";
                WindowMenuOps.StartAssistFromManagedDrag(managed, dockWindow, zone); // 남은 칸 후보 고르기(11 문서 §3.9) — 다음 프레임, 옵션·제외는 세션이 판정
            }

            Snapped?.Invoke(managed.Title ?? string.Empty, zone.Kind, zone.Target);
        }
        else if (zone.Kind == SnapZoneKind.None && SnapRestoreMemory.TryTake(dockWindow, start, out var before))
        {
            // 스냅된 창을 칸 밖으로 끌어냄 → 스냅 전 크기로(놓은 자리 기준, 포인터가 잡은 비율 자리 유지, §3.6)
            var now = RectOf(managed, dockWindow);
            SetRect(dockWindow, new Rect(drop.X - (before.Width * grabX), now.Y, before.Width, before.Height));
            LastOutcome += "|apply=unsnapped";
        }
        else
        {
            LastOutcome += "|apply=move";
            return;
        }

        App.NotifyLayoutChanged(); // 놓기 뒤 경로로 바뀐 배치 — 자동 저장 대상
    }

    private static void Maximize(ManagedDockWindowDocument managed, IDockWindow dockWindow)
    {
        if (App.Shell is WindowCommands shell && WindowCommands.FindDocument(managed) is { } doc)
        {
            shell.SetState(doc, MdiWindowState.Maximized); // 창 메뉴 최대화와 같은 경로
            return;
        }

        dockWindow.WindowState = DockWindowState.Maximized; // 패널 탭만 남은 분리 창(WindowMenuOps 와 같은 처리)
        dockWindow.Host?.SetWindowState(DockWindowState.Maximized);
    }

    private static void OnKeyDown(TopLevel _, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !CancelToStart("esc")) { return; }
        e.Handled = true;
    }

    private static void OnExited(TopLevel top, PointerEventArgs e)
    {
        // 캡처 중 자식 경계를 지날 때 오는 통지는 무시하고 표면 밖일 때만
        if (ReferenceEquals(e.Source, top) && !new Rect(top.Bounds.Size).Contains(e.GetPosition(top)))
        {
            CancelToStart("leave");
        }
    }

    /// <summary>끌기 취소: 막대·미리보기를 닫고 시작 위치로 돌린 뒤 캡처를 놓아 Dock 끌기를 끝낸다. 놓기는 TopLevel 처리기가 받아 한 번 더 되돌린다.</summary>
    private static bool CancelToStart(string why)
    {
        if (!s_dragging || s_cancelled || s_managed?.Window is not { } dockWindow) { return false; }
        s_cancelled = true;
        HideAll();
        SetRect(dockWindow, s_start);
        s_pointer?.Capture(null);
        LastOutcome = $"{why}:source={s_managed.Title}";
        PdLog.Info("managed-snap", LastOutcome);
        return true;
    }

    /// <summary>창이 캡처를 잃음 — 500ms 안에 놓기가 없을 때만 취소(§3.7). 판정은 지우지 않는다(웹은 캡처 해제가 놓기보다 먼저 와 zone=None 이 됐다, AC-014 재시험 — MdiSnapDrag 와 같음).</summary>
    private static void OnCaptureLost()
    {
        s_captureLost = true;
        if (s_cancelled) { return; }
        var managed = s_managed;
        DispatcherTimer.RunOnce(() =>
        {
            if (s_captureLost && !s_cancelled && managed is not null && ReferenceEquals(s_managed, managed)) { LastOutcome = $"capture-lost-no-release:source={managed.Title}"; Reset(); }
        }, TimeSpan.FromMilliseconds(500));
    }

    private static void HideAll()
    {
        CancelBarTimer();
        s_barVisible = false;
        s_zone = SnapZone.None;
        s_overlay?.Hide();
    }

    private static void Reset()
    {
        if (s_dragging) { HideAll(); }
        (s_window, s_managed, s_area, s_overlay, s_pointer) = (null, null, null, null, null);
        Moves = 0;
        s_context = SnapZoneContext.Disabled;
        s_zone = SnapZone.None;
        s_others = new List<MdiDocumentWindow>();
        s_dragging = s_cancelled = s_captureLost = s_barVisible = false;
    }

    private static bool IsLocked(ManagedDockWindowDocument managed) =>
        WindowCommands.FindDocument(managed) is { } doc && WindowCommands.ChromeOf(doc).IsLocked; // 위치 고정 창은 대상이 아니다(§2)

    private static Rect RectOf(ManagedDockWindowDocument managed, IDockWindow window) => ManagedWindowRect.RectOf(managed, window);

    private static void SetRect(IDockWindow window, Rect r) => ManagedWindowRect.SetRect(window, r);
}
