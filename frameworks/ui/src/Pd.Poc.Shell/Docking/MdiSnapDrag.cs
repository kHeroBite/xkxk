// MDI 문서 창 제목줄 끌기를 엿보아 위쪽 막대·모서리·위쪽 가장자리(최대화)·좌우 가장자리 칸으로 배치하는 처리기(11_창배치_스냅레이아웃.md §3·§5, ADR-099)
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Client.Data.Logging;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 막대·미리보기 표시 창구(odev-3 SnapOverlayLayer 가 구현). Shell 은 화면 레이어(Pd.Ui.Views)를 참조하지 않으므로 인터페이스로만 부른다.
/// 좌표는 area(기준 영역 컨트롤) 로컬 DIP — SnapZoneContext 는 new Rect(area.Bounds.Size) 로 만든다.
/// </summary>
public interface ISnapOverlay
{
    bool IsBarVisible { get; }
    void ShowBar(SnapZoneContext ctx);
    void HideBar();
    void Highlight(string? layoutId, int cell);
    void ShowPreview(Rect target);
    void HidePreview();
    /// <summary>판정 하나로 미리보기·칸 강조를 맞춘다(TabJoin 이면 전부 숨김). 막대 표시 여부는 호출자가 ShowBar/HideBar 로 정한다.</summary>
    void Show(SnapZone zone);
    void Hide();
}

/// <summary>표시 창구 조회. 화면 레이어가 모듈 초기화로 Factory 를 등록한다(SnapOverlayLayer.Attach). 등록이 없으면 null — 표시 없이 판정·배치만 한다.</summary>
public static class SnapOverlayHost
{
    public static Func<Control, ISnapOverlay?>? Factory { get; set; }
    public static ISnapOverlay? For(Control area) => Factory?.Invoke(area);
}

/// <summary>
/// MdiTabJoinDrag 와 같은 방식(MdiDocumentWindow 누름·이동·놓기 클래스 처리기, handledEventsToo)으로 엿본다 — Dock 의 MDI 이동·자석은 그대로 두고,
/// 놓은 뒤(Dock EndDrag 다음) 스냅 사각형으로 MdiBounds 를 바꾼다(자석 결과를 대신함, 11 문서 §5).
/// 판정 문맥(기준 영역·옵션·막대·칸 사각형)은 끌기 시작 때 1회만 만들고(SnapZoneContext), 이동마다는 포인터 판정만 한다(W4·B2 끌기 비용 회귀 방지).
/// 우선순위는 SnapZoneContext.Resolve(탭 합치기 > 막대 칸 > 모서리 > 위쪽 최대화 > 좌우). 막대 밖·띠 밖에 놓으면 이동만, Esc 는 시작 위치로 되돌린다.
/// 스냅 전 크기는 메모리에만 둔다(저장 형식 무변경, §3.6). 칸 사각형은 기준 영역 안이라 끌 수 있는 구간 50px 규칙(MdiDragLimit)을 늘 만족한다.
/// 설치: 테마(MdiDocumentWindowTheme)가 Enabled=True 를 거는 첫 창에서 1회 등록한다. MdiTabJoinDrag 설치 뒤에 두어 같은 이동에서 표적 판정이 먼저 갱신되게 한다.
/// </summary>
public static partial class MdiSnapDrag
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MdiDocumentWindow, bool>("Enabled", typeof(MdiSnapDrag));

    private static bool s_installed;
    private static MdiDocumentWindow? s_window;
    private static IMdiDocument? s_doc;
    private static Control? s_area;
    private static Rect s_areaRect;
    private static Vector s_offset; // MdiBounds 원점의 s_area 좌표(칸 사각형 → MdiBounds 변환에 뺀다)
    private static ISnapOverlay? s_overlay;
    private static IPointer? s_pointer;
    private static Point s_press;
    private static DockRect s_start;
    private static SnapZoneContext s_context = SnapZoneContext.Disabled;
    private static SnapZone s_zone = SnapZone.None;
    private static Point s_last;
    private static bool s_dragging, s_cancelled, s_captureLost, s_barVisible;
    private static DispatcherTimer? s_barTimer;

    static MdiSnapDrag() => EnabledProperty.Changed.AddClassHandler<MdiDocumentWindow>((_, e) => { if (e.NewValue is true) { Install(); } });

    public static bool GetEnabled(MdiDocumentWindow w) => w.GetValue(EnabledProperty);
    public static void SetEnabled(MdiDocumentWindow w, bool value) => w.SetValue(EnabledProperty, value);

    /// <summary>스냅을 적용했다(창 키, 구역 종류, 사각형) — 로그·시험 관측용.</summary>
    public static event Action<string, SnapZoneKind, Rect>? Snapped;

    /// <summary>마지막 놓기 판정 한 줄(진단·시험 관측용).</summary>
    public static string LastOutcome { get; private set; } = "none";

    /// <summary>지금 끌기 중 판정(진단·시험 관측용, 끌기 밖이면 None).</summary>
    public static SnapZone CurrentZone => s_dragging ? s_zone : SnapZone.None;

    /// <summary>마지막 놓기 때 판정 근거 한 줄(기준 영역·원점 차이·이동 수·캡처 잃음·마지막/최종 판정·문맥 옵션) — 원격 조회(snapcell.lastdrag)·로그용.</summary>
    public static string LastDrag { get; private set; } = "none";

    /// <summary>끄는 동안의 현재 판정 한 줄(놓지 않고 머문 상태 조회용 — snapcell.lastdrag 가 함께 돌려준다).</summary>
    public static string LiveState => $"dragging={s_dragging} last={s_last} moves={s_moves} zone={s_zone.Kind}:{s_zone.Target} wantsBar={s_zone.WantsBar} bar={s_barVisible} area={s_areaRect} off={s_offset} overlay={s_overlay is not null}";

    private static int s_moves;

    /// <summary>클래스 처리기를 1회 등록한다(멱등).</summary>
    public static void Install()
    {
        if (s_installed) { return; }
        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>(OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<MdiDocumentWindow>((_, e) => OnReleasedDiag(e, "window"), RoutingStrategies.Bubble, handledEventsToo: true);
        // 창이 캡처를 먼저 잃으면(또는 Esc 로 놓아 준 뒤) 놓기는 포인터 아래 요소로 간다 — TopLevel 에서 받는다(MdiTabJoinDrag 와 같은 경로)
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => { if (s_captureLost || s_cancelled) { OnReleasedDiag(e, "toplevel"); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((w, e) => { if (ReferenceEquals(e.Source, w) && ReferenceEquals(w, s_window)) { OnCaptureLost(); } },
            RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        Reset();
        if (window.DataContext is IMdiDocument doc and (PocDocument or MdiTabGroupDock) && doc.Owner is IDocumentDock mdi
            && doc.MdiState == MdiWindowState.Normal && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            && PanelJoinTargets.IsTitlePress(e.Source as Visual, window) && !IsLocked(doc)
            && FindArea(window, mdi, doc.MdiBounds) is { } a)
        {
            (s_window, s_doc, s_area, s_areaRect, s_offset) = (window, doc, a.Host, a.Area, a.Offset);
            (s_pointer, s_press, s_start) = (e.Pointer, e.GetPosition(a.Host), doc.MdiBounds);
            if (MdiMoveLight.ReleaseFlushFrame) { MdiMoveLight.HookReleaseFlush(TopLevel.GetTopLevel(window)); } // K6(01a)
        }
    }

    /// <summary>
    /// 기준 영역 = MDI 문서 칸(MdiTabJoinDrag.FindHost — DataContext 가 그 MDI dock 인 조상). 창의 시각 부모는 창 크기 래퍼라 영역이 아니다(otest-1 실측 300x250).
    /// 래퍼는 MdiBounds 자리에 배치되므로(MagnetMdiLayoutManager.Arrange) 래퍼의 host 좌표 − MdiBounds = MdiBounds 원점(Offset).
    /// 영역 사각형은 래퍼의 부모(배치 패널)가 그 원점에 있으면 그 크기, 아니면 host 전체. 칸을 못 찾으면 null(스냅 안 함).
    /// </summary>
    private static (Control Host, Rect Area, Vector Offset)? FindArea(MdiDocumentWindow window, IDocumentDock mdi, DockRect b)
    {
        var wrapper = window.GetVisualParent() ?? window;
        if (MdiTabJoinDrag.FindHost(window, mdi) is not Control { Bounds: { Width: > 0, Height: > 0 } } host || ReferenceEquals(host, wrapper)
            || wrapper.TranslatePoint(default, host) is not { } at)
        {
            return null;
        }

        var offset = new Vector(at.X - b.X, at.Y - b.Y);
        var area = wrapper.GetVisualParent() is { Bounds: { Width: > 0, Height: > 0 } } panel && !ReferenceEquals(panel, host)
            && panel.TranslatePoint(default, host) is { } p && Math.Abs(p.X - offset.X) < 0.5 && Math.Abs(p.Y - offset.Y) < 0.5
            ? new Rect(p, panel.Bounds.Size)
            : new Rect(host.Bounds.Size);
        return (host, area, offset);
    }

    private static void OnMoved(MdiDocumentWindow window, PointerEventArgs e)
    {
        if (!ReferenceEquals(window, s_window) || s_area is not { } area || s_cancelled) { return; }
        var point = e.GetPosition(area);
        if (!s_dragging)
        {
            if (!TabJoinRules.IsDrag(s_press, point)) { return; }
            s_dragging = true;
            // 끌기 시작 때 1회: 기준 영역(MDI 문서 칸, host 좌표)·옵션·막대·칸 사각형
            s_context = UiSettings.Current.CreateSnapContext(s_areaRect, WindowMinSize.Current);
            s_overlay = s_context.Enabled ? SnapOverlayHost.For(area) : null;
        }

        if (!s_context.Enabled) { return; } // 스냅 끔: 첫 판정에서 바로 돌아간다(§3.7)
        s_last = point;
        s_moves++;
        Update(MdiTabJoinDrag.IsOverTarget);
    }

    /// <summary>현재 포인터로 판정하고 막대(머무름 지연)·미리보기를 맞춘다.</summary>
    private static void Update(bool overTabJoin)
    {
        if (s_area is null) { return; }
        s_zone = s_context.Resolve(s_last, s_barVisible, overTabJoin);
        if (s_zone.WantsBar && !s_barVisible)
        {
            ScheduleBar();
        }
        else if (!s_zone.WantsBar)
        {
            CancelBarTimer();
            if (s_barVisible) { s_barVisible = false; s_overlay?.HideBar(); }
        }

        s_overlay?.Show(s_zone); // TabJoin 은 WantsBar=false 라 위에서 막대가 닫히고, Show 가 미리보기도 숨긴다(탭 합류가 이긴다)
    }

    /// <summary>위쪽 띠에 든 뒤 SnapBarDelayMs 만큼 머무르면 막대를 띄운다. 포인터가 멈춰도 뜨게 타이머로 잰다(0 이면 바로).</summary>
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
        Update(MdiTabJoinDrag.IsOverTarget); // 막대가 뜬 지금 위치가 칸 위일 수 있다
    }

    private static void CancelBarTimer() { s_barTimer?.Stop(); s_barTimer = null; }

    private static void OnReleased(PointerReleasedEventArgs e, string via)
    {
        if (s_doc is not { } doc || s_window is null) { return; }
        var dropHost = s_area is { } area ? e.GetPosition(area) : s_last;
        // 마지막 이동 판정이 비었으면 놓은 자리로 한 번 더 판정한다(이동 이벤트 없이 놓인 경우 — 같은 점이면 결과도 같다). TabJoin·칸 판정은 그대로 둔다
        var hostZone = s_dragging && !s_cancelled && s_zone.Kind == SnapZoneKind.None && s_context.Enabled ? s_context.Resolve(dropHost, s_barVisible, false) : s_zone;
        LastDrag = $"host={s_area?.GetType().Name} area={s_areaRect} off={s_offset} press={s_press} last={s_last} moves={s_moves} bar={s_barVisible} lostCapture={s_captureLost} "
            + $"lastZone={s_zone.Kind}:{s_zone.Target} drop={dropHost} final={hostZone.Kind}:{hostZone.Target} ctx=en:{s_context.Enabled},bar:{s_context.HasBar},d:{s_context.Distance},edge:{s_context.EdgeEnabled},top:{s_context.TopEdgeMaximize}";
        // 판정은 MDI 칸(host) 좌표 → 적용은 MdiBounds 좌표: 원점 차이(s_offset)를 뺀다
        var (dragging, cancelled, zone, start, off) = (s_dragging, s_cancelled, hostZone with { Target = hostZone.Target.Translate(-s_offset) }, s_start, s_offset);
        var grabX = s_start.Width > 0 ? (s_press.X - off.X - s_start.X) / s_start.Width : 0.5; // 스냅 해제 때 포인터가 같은 비율 자리를 잡게
        var drop = dropHost - off;
        ResetOnRelease();
        if (!dragging) { return; }
        MdiMoveLight.ReleaseFlushPending = MdiMoveLight.ReleaseFlushFrame; // K6: 끌기였던 놓기만(클릭 놓기 제외)
        LastOutcome = $"{via}:source={doc.Id}|zone={zone.Kind}|layout={zone.LayoutId ?? "-"}|cell={zone.Cell}|target={zone.Target}|cancelled={cancelled}";
        LogRelease(LastOutcome + " || " + LastDrag);
        // Dock 의 놓기 처리(EndDrag·자석)가 끝난 뒤 바꾼다(MdiTabJoinDrag 와 같은 이유)
        Dispatcher.UIThread.Post(() => ApplyDiag(doc, cancelled, zone, start, grabX, drop));
    }

    /// <summary>스냅 전 크기는 창 메뉴 '배치' 와 같은 보관소(SnapRestoreMemory, 키 = MDI 창 문서)를 쓴다 — 메뉴로 배치한 창도 끌어내면 돌아간다(§3.6).</summary>
    private static void Apply(IMdiDocument doc, bool cancelled, SnapZone zone, DockRect start, double grabX, Point drop)
    {
        if (doc.Owner is not IDocumentDock { VisibleDockables: { } children } || !children.Contains(doc)) { LastOutcome += "|apply=skipped"; return; }
        if (cancelled) { doc.MdiBounds = start; LastOutcome += "|apply=restored"; } // Esc: 시작 위치로
        else if (zone.Kind != SnapZoneKind.TabJoin && zone.HasTarget)
        {
            var startRect = new Rect(start.X, start.Y, start.Width, start.Height);
            if (zone.Kind == SnapZoneKind.TopEdge || (zone.Kind == SnapZoneKind.BarCell && zone.LayoutId == SnapLayoutCatalog.Max))
            {
                doc.MdiBounds = start; // 최대화 해제 때 끌기 전 자리로
                doc.MdiState = MdiWindowState.Maximized;
                LastOutcome += "|apply=maximized";
            }
            else
            {
                SnapRestoreMemory.Remember(doc, startRect, zone.Target); // 이미 스냅된 창이면 처음 스냅 전 크기를 유지한다
                doc.MdiBounds = new DockRect(zone.Target.X, zone.Target.Y, zone.Target.Width, zone.Target.Height);
                LastOutcome += "|apply=snapped";
                WindowMenuOps.StartAssistFromMdiDrag(doc, zone); // 남은 칸 후보 고르기(11 문서 §3.9) — 다음 프레임, 옵션·제외는 세션이 판정
            }

            Snapped?.Invoke(doc.Id, zone.Kind, zone.Target);
        }
        else if (zone.Kind == SnapZoneKind.None && SnapRestoreMemory.TryTake(doc, new Rect(start.X, start.Y, start.Width, start.Height), out var before))
        {
            // 스냅된 창을 칸 밖으로 끌어냄 → 스냅 전 크기로(놓은 자리 기준, 포인터가 잡은 비율 자리 유지). 맞지 않는 기억은 TryTake 가 지운다
            var b = doc.MdiBounds;
            doc.MdiBounds = new DockRect(drop.X - (before.Width * grabX), b.Y, before.Width, before.Height);
            LastOutcome += "|apply=unsnapped";
        }
        else { LastOutcome += "|apply=move"; return; }

        MagnetMdiLayoutManager.Instance.InvalidateDragSnapshot(); // 끌기가 아닌 경로로 배치가 바뀌었다
        // 진단: 적용 뒤 값이 Dock 의 끌기 종료·배치에 덮어써졌는지 한 박자 뒤에 기록한다
        Dispatcher.UIThread.Post(() => PdLog.Info("mdi-snap", $"after source={doc.Id} bounds={doc.MdiBounds} state={doc.MdiState} | {LastOutcome}"), DispatcherPriority.Background);
    }

    private static void OnKeyDown(TopLevel _, KeyEventArgs e)
    {
        if (!s_dragging || s_cancelled || e.Key != Key.Escape || s_doc is not { } doc) { return; }
        s_cancelled = true;
        HideAll();
        e.Handled = true;
        var start = s_start;
        doc.MdiBounds = start; // 바로 시작 위치로(놓기 때 한 번 더 — Dock 이동이 이어졌을 수 있다)
        s_pointer?.Capture(null); // Dock 끌기를 끝낸다. 놓기는 TopLevel 처리기가 받는다
        LastOutcome = $"esc:source={doc.Id}";
        PdLog.Info("mdi-snap", LastOutcome);
    }

    /// <summary>창 자신이 캡처를 잃음. 놓기가 곧 올 수 있어(Dock 이 놓기 처리 중 캡처를 풀면 놓기보다 먼저 온다) 판정·표시를 지우지 않고 500ms 뒤에만 취소한다(§3.7).
    /// 예전에는 여기서 HideAll 로 판정(s_zone)을 지워 바로 뒤 놓기가 늘 None(일반 이동)이 될 수 있었다. Esc 취소 중이면 놓기를 기다린다.</summary>
    private static void OnCaptureLost()
    {
        s_captureLost = true;
        if (s_cancelled) { return; }
        var doc = s_doc;
        DispatcherTimer.RunOnce(() =>
        {
            if (s_captureLost && !s_cancelled && doc is not null && ReferenceEquals(s_doc, doc)) { LastOutcome = $"capture-lost-no-release:source={doc.Id}"; Reset(); }
        }, TimeSpan.FromMilliseconds(500));
    }

    private static void HideAll() { CancelBarTimer(); s_barVisible = false; s_zone = SnapZone.None; s_overlay?.Hide(); }

    private static void Reset()
    {
        if (s_dragging) { HideAll(); }
        (s_window, s_doc, s_area, s_overlay, s_pointer) = (null, null, null, null, null);
        s_context = SnapZoneContext.Disabled;
        s_zone = SnapZone.None;
        s_dragging = s_cancelled = s_captureLost = s_barVisible = MdiMoveLight.ReleaseFlushPending = false; // K6 표시도 지운다(다른 TopLevel 에서 놓인 경우)
        s_moves = 0;
    }

    private static bool IsLocked(IMdiDocument document) =>
        WindowCommands.FindDocument(document) is { } doc && WindowCommands.ChromeOf(doc).IsLocked;
}
