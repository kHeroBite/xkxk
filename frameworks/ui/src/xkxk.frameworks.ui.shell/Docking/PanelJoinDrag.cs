// MDI 창 제목줄 끌기로 측면 패널 탭에 합류(UI-14 실제 끌기) — Dock MDI 이동은 그대로 두고, 놓는 지점이 측면 패널이면 탭으로 보낸다. 작업표시줄 탭 끌기용 공개 API 포함
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(Dock.Avalonia 12.1.0.12 디컴파일):
/// ① MdiDocumentWindow.HeaderPressedHandler 는 제목 영역 누름을 MDI 창 이동(BeginDrag→DragMove 가 MdiBounds 만 바꿈)으로 처리하고 Handled=true 로 끝낸다.
///   도킹 드롭(DockControlState)은 DockProperties.IsDragArea 컨트롤(PdMdiChrome 의 PART_DragHandle, 4px)에서만 시작된다.
/// ② 그래서 사용자가 자연스럽게 잡는 제목줄로 끌면 Dock 드롭 안내가 뜨지 않고 패널 합류도 일어나지 않는다(웹·데스크톱 같은 코드 경로).
/// 대응: MdiDocumentWindow 의 누름·이동·놓기를 클래스 처리기(handledEventsToo)로 엿보고,
/// 놓는 지점이 보이는 패널(ToolDockControl) 안이면 SendDocumentToPanel 로 탭에 넣는다.
/// 오판 방지: 누른 곳에서 MinDragDistance 이상 움직여야 하고, 같은 패널 위에 DwellMs 이상 머문 뒤 놓아야 합류한다.
/// 끄는 동안 대상 패널에 pdJoinTarget 클래스를 붙인다(강조 스타일은 odev-7c 테마).
/// </summary>
public static class PanelJoinDrag
{
    public const string TargetClass = "pdJoinTarget";

    /// <summary>합류 판정을 시작하는 최소 이동 거리(DIP). MDI 창을 살짝 옮기는 동작은 합류 대상이 아니다.</summary>
    public const double MinDragDistance = 8;

    /// <summary>패널 위에 머물러야 하는 최소 시간(ms). 패널 근처로 창을 옮기다 지나치는 경우를 거른다.</summary>
    public const long DwellMs = 250;

    private static PocDockFactory? _factory;
    private static PocDocument? _document;
    private static DockRect _startBounds;
    private static Point _pressPoint;
    private static bool _moved;
    private static Control? _hover;
    private static long _hoverSince;
    private static bool _installed;

    /// <summary>합류 판정을 거쳐 탭으로 보낸 뒤(로그·시험 관측용). 인자는 문서 Id 와 탭 영역 Id.</summary>
    public static event Action<string, string>? Joined;

    /// <summary>팩터리를 연결하고 클래스 처리기를 1회 등록한다.</summary>
    public static void Install(PocDockFactory factory)
    {
        _factory = factory;
        if (_installed)
        {
            return;
        }

        _installed = true;
        CenterDropToMdi.Install(); // 끌기 중 마지막 포인터 기억(U13 가운데 놓기·U14 칸 합류 판정)
        DocumentTabMenu.InstallUnsplit(); // 탭 문서 모드 탭 우클릭 '분할 해제'(U14)
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>(OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<MdiDocumentWindow>(OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        // 캡처 잃음은 MDI 창 자신이 잃을 때만 끌기 취소로 본다. 누를 때 Avalonia 가 눌린 하위 요소(제목 TextBlock 등)에 암묵 캡처를 걸고,
        // MdiDocumentWindow.BeginDrag 가 캡처를 자기에게 옮기면 하위 요소의 PointerCaptureLost 가 창까지 올라온다.
        // 이를 취소로 처리하면 끌기 시작 직후 상태가 지워져 합류·강조가 전혀 일어나지 않는다(i0136 웹 Q15 pdJoinTarget 0건).
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((w, e) =>
        {
            if (ReferenceEquals(e.Source, w))
            {
                OnWindowCaptureLost();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        // 창이 캡처를 먼저 잃으면(i0215 웹 실측: 놓은 직후 pressed=none, 합류 없음) 놓기 이벤트는 MDI 창이 아니라 포인터 아래 요소(패널)로 간다.
        // 그래서 TopLevel 까지 올라오는 놓기를 함께 받아, 캡처를 잃은 끌기가 남아 있으면 그 지점으로 합류를 판정한다.
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(OnTopLevelReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static bool _captureLost;
    private static string _lastOutcome = "none";

    /// <summary>MDI 창 자신이 캡처를 잃음. 놓기보다 먼저 올 수 있으므로 바로 지우지 않고 잠시(500ms) 놓기를 기다린 뒤에만 취소한다.</summary>
    private static void OnWindowCaptureLost()
    {
        if (_document is null)
        {
            return;
        }

        _captureLost = true;
        var doc = _document;
        DispatcherTimer.RunOnce(() =>
        {
            if (_captureLost && ReferenceEquals(_document, doc))
            {
                _lastOutcome = $"capture-lost-no-release:doc={doc.Id}";
                Reset();
            }
        }, TimeSpan.FromMilliseconds(500));
    }

    private static void OnTopLevelReleased(TopLevel top, PointerReleasedEventArgs e)
    {
        if (_document is not { } doc || !_captureLost)
        {
            return; // 정상 경로는 MdiDocumentWindow 의 OnReleased 가 먼저 처리하고 상태를 지운다
        }

        Commit(doc, _moved ? FindTargetAt(top, e.GetPosition(top)) : null, "toplevel");
    }

    /// <summary>놓기 판정 공통 — 체류 조건을 보고 합류하거나 취소한다. 결과는 진단용으로 남긴다.</summary>
    private static void Commit(PocDocument doc, Control? target, string via)
    {
        var dwell = Environment.TickCount64 - _hoverSince;
        var accepted = IsDwellSatisfied(target);
        var start = _startBounds;
        var moved = _moved;
        _lastOutcome = $"{via}:doc={doc.Id}|moved={_moved}|target={(target?.DataContext as IDock)?.Id ?? "none"}"
            + $"|hover={(_hover?.DataContext as IDock)?.Id ?? "none"}|dwellMs={dwell}|accepted={accepted}";
        Reset();
        if (accepted)
        {
            Join(doc, target!, start);
        }
        else if (_factory is { } factory && moved && target is null && SplitMergeService.TryMergeAt(factory, doc))
        {
            _lastOutcome += "|merged=true"; // 다른 MDI 문서 칸으로 합류(U14 — 빈 분할 칸은 정리)
        }
    }

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        Reset();
        // 제목줄(PART_Header) 누름만 본다. 내용 영역(그리드 끌어 선택 등)·캡션 버튼·Dock 자체 끌기 손잡이는 제외한다.
        if (window.DataContext is PocDocument doc && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            && PanelJoinTargets.IsTitlePress(e.Source as Visual, window) && TopLevel.GetTopLevel(window) is { } top)
        {
            _document = doc;
            _startBounds = doc.MdiBounds;
            _pressPoint = e.GetPosition(top);
#if PD_DIAGNOSTICS
            PanelJoinDiag.DragStart(doc.Id); // 진단 빌드 전용: hover 이력 구간 표시
#endif
        }
    }

    private static void OnMoved(MdiDocumentWindow window, PointerEventArgs e)
    {
        if (_document is null || !ReferenceEquals(window.DataContext, _document) || TopLevel.GetTopLevel(window) is not { } top)
        {
            return;
        }

        var point = e.GetPosition(top);
        if (!_moved)
        {
            var d = point - _pressPoint;
            if (Math.Sqrt((d.X * d.X) + (d.Y * d.Y)) < MinDragDistance)
            {
                return;
            }

            _moved = true;
        }

        SetHover(PanelJoinMoveCandidates.TargetAt(_factory, top, point)); // W4: 이동 중에는 후보 캐시 사용(놓기 판정은 새로 찾음)
    }

    private static void OnReleased(MdiDocumentWindow window, PointerReleasedEventArgs e)
    {
        if (_document is not { } doc)
        {
            return;
        }

        Control? target = null;
        if (_moved && ReferenceEquals(window.DataContext, doc) && TopLevel.GetTopLevel(window) is { } top)
        {
            target = FindTargetAt(top, e.GetPosition(top));
        }

        Commit(doc, target, "window");
    }

    /// <summary>
    /// 탭으로 보낸다. Dock 의 놓기 처리(EndDragOrResize)가 끝난 뒤 트리를 바꾸도록 Post 한다.
    /// 먼저 탭으로 옮긴 뒤 MDI 위치를 되돌린다 — 창이 이미 떨어져 나간 뒤라 끌기 전 자리로 튀어 보이지 않는다.
    /// </summary>
    private static bool Join(PocDocument doc, Control target, DockRect? restoreBounds)
    {
        if (target.DataContext is not IDock { Id: { } paneId } || _factory is not { } factory)
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!factory.SendDocumentToPanel(doc, paneId))
            {
                return;
            }

            if (restoreBounds is { } bounds)
            {
                doc.MdiBounds = bounds; // 나중에 문서 영역으로 돌아오면 끌기 전 자리에 놓인다
            }

            if (PocDockFactory.PanelOf(doc) is { } tabsId)
            {
                Joined?.Invoke(doc.Id, tabsId);
            }
        });
        return true;
    }

    private static bool IsDwellSatisfied(Control? target) =>
        target is not null && ReferenceEquals(target, _hover) && Environment.TickCount64 - _hoverSince >= DwellMs;

    /// <summary>점 아래의 보이는 측면 패널(바깥 ToolDockControl). 집중 숨김 중이거나 없으면 null(PanelJoinTargets).</summary>
    private static Control? FindTargetAt(TopLevel top, Point point) => PanelJoinTargets.FindAt(_factory, top, point);

    private static void SetHover(Control? target)
    {
        if (ReferenceEquals(_hover, target))
        {
            return;
        }

        var now = Environment.TickCount64;
#if PD_DIAGNOSTICS
        PanelJoinDiag.HoverChanged(_hover, target, now); // 진단 빌드 전용 hover 이력(제품은 호출이 빠진다)
#endif
        _hover?.Classes.Remove(TargetClass);
        _hover = target;
        _hoverSince = now;
        _hover?.Classes.Add(TargetClass);
    }

    /// <summary>
    /// 끄는 중 포인터 위치를 알려 강조·체류 시간을 갱신한다(작업표시줄 탭 끌기용 — odev-2).
    /// screenPoint 가 null 이면 강조를 지운다. 포인터 아래 합류 가능한 패널 Id(없으면 null)를 돌려준다.
    /// </summary>
    public static string? UpdateHoverAt(TopLevel top, PixelPoint? screenPoint)
    {
        var target = screenPoint is { } p ? FindTargetAt(top, top.PointToClient(p)) : null;
        SetHover(target);
        return (target?.DataContext as IDock)?.Id;
    }

    /// <summary>
    /// 화면점에서 놓았을 때 합류를 시도한다(작업표시줄 탭 끌기용). 직전 UpdateHoverAt 로 같은 패널 위에 DwellMs 이상 머물렀어야 한다.
    /// 합류하면 true. 최소 이동 거리 판정은 호출자 몫이다.
    /// </summary>
    public static bool TryJoinAt(PocDocument document, TopLevel top, PixelPoint screenPoint)
    {
        ArgumentNullException.ThrowIfNull(document);
        var target = FindTargetAt(top, top.PointToClient(screenPoint));
        var accepted = IsDwellSatisfied(target);
        SetHover(null);
        return accepted && Join(document, target!, null);
    }

    /// <summary>UpdateHoverAt 의 클라이언트 좌표(TopLevel 기준 DIP) 형식. null 이면 강조를 지운다.</summary>
    public static string? UpdateHoverAt(TopLevel top, Point? clientPoint)
    {
        var target = clientPoint is { } p ? FindTargetAt(top, p) : null;
        SetHover(target);
        return (target?.DataContext as IDock)?.Id;
    }

    /// <summary>TryJoinAt 의 클라이언트 좌표(TopLevel 기준 DIP) 형식.</summary>
    public static bool TryJoinAt(PocDocument document, TopLevel top, Point clientPoint)
    {
        ArgumentNullException.ThrowIfNull(document);
        var target = FindTargetAt(top, clientPoint);
        var accepted = IsDwellSatisfied(target);
        SetHover(null);
        return accepted && Join(document, target!, null);
    }

    /// <summary>미리보기 전용 — 점 아래 합류 가능한 패널 Id(없으면 null). 강조·체류 상태를 바꾸지 않는다.</summary>
    public static string? PanelAt(TopLevel top, Point clientPoint) => (FindTargetAt(top, clientPoint)?.DataContext as IDock)?.Id;

#if PD_DIAGNOSTICS
    /// <summary>진단 빌드 전용 — 점 아래 합류 대상 판정 과정 한 줄(원격 panel.joinProbe 용, 형식은 PanelJoinDiag.Probe).</summary>
    public static string Probe(TopLevel top, Point point) => PanelJoinDiag.Probe(top, point);

    /// <summary>진단 빌드 전용 — PanelJoinDiag 가 읽는 끌기 상태 사본.</summary>
    internal static (bool Installed, PocDockFactory? Factory, PocDocument? Pressed, bool Moved, Control? Hover, bool CaptureLost, long HoverSince, string LastOutcome) DiagState
        => (_installed, _factory, _document, _moved, _hover, _captureLost, _hoverSince, _lastOutcome);
#endif

    private static void Reset()
    {
        SetHover(null);
        _document = null;
        _moved = false;
        _captureLost = false;
        PanelJoinMoveCandidates.Clear();
    }
}
