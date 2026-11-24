// 최대화된 MDI 문서 창 제목줄 끌기 → 이전 크기로 복원하며 커서를 같은 가로 비율 위치에 둔 채 계속 끌기(U9 ③, Windows 끌어서 복원 동작 대응)
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Mdi;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Dock MdiDocumentWindow 는 최대화 상태 제목줄 누름을 무시한다(끌기는 보통 상태만). 그래서 최대화 창만 여기서 받는다:
/// 누름 기록 → 문턱 이상 움직이면 SetState(Normal)(WindowCommands 공개 경로) 후 복원 크기를 커서 기준으로 놓고, 놓을 때까지 MdiBounds 를 따라 옮긴다.
/// 보통 상태 끌기·더블클릭 토글은 Dock 이 그대로 처리한다. 위치 고정(IsLocked) 창은 대상이 아니다. OS 창(메인·SDI)은 OS 가 같은 동작을 한다.
/// </summary>
public static class TitleDragRestore
{
    /// <summary>복원 시작 문턱(DIP) — 클릭·더블클릭을 끌기로 오인하지 않는 거리.</summary>
    private const double Threshold = 4;

    private static bool s_installed;
    private static MdiDocumentWindow? s_window;
    private static PocDocument? s_doc;
    private static Point s_press;      // 누른 점(부모 패널 기준)
    private static double s_ratioX;    // 누른 점의 제목줄 가로 비율(0~1)
    private static Point s_grab;       // 복원 뒤 창 왼쪽 위에서 커서까지 거리
    private static bool s_dragging;

    /// <summary>웹 보조 더블클릭 판정: 두 누름 위치 허용 거리(DIP)와 첫 누름부터의 절대 상한(ms).</summary>
    private const double PairSlop = 4;
    private const long PairCapMs = 1500;

    private static MdiDocumentWindow? s_pairWindow; // 웹 보조 판정용 직전 누름 창
    private static Point s_pairPoint;                // 직전 누름 위치(TopLevel 기준)
    private static long s_pairPressAt;               // 직전 누름 처리 시각(ms)
    private static long s_pairIdleAt;                // 직전 누름 뒤 앱이 한가해진 시각(ms, 0 = 아직 바쁨)
    private static int s_pairSeq;                    // 한가 시각 콜백이 오래된 누름 것인지 가리는 순번
    private static MdiDocumentWindow? s_auxWindow;   // 보조 판정으로 토글한 창
    private static long s_auxToggleAt;               // 그 토글 시각(ms)
    private static double? s_pairRawAt;              // 직전 누름의 원래 입력 시각(PressTimeSource, 없으면 null)

    /// <summary>누름 원래 시각 공급자(웹 호스트가 설정, 데스크톱·모바일은 null). 있으면 웹 더블클릭 판정이 처리 지연과 무관한 간격을 쓴다(D-C2-19).</summary>
    public static IPressTimeSource? PressTimeSource { get; set; }

    /// <summary>앱 시작 시 1회.</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        // handledEventsToo: 웹 실측(01:37)에서 최대화 제목줄 누름·이동이 이 처리기에 오지 않았다(조상 처리기가 Handled 로 만든 것으로 추정) — 판정은 제목줄·상태 조건으로만 한다
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 이동·놓기는 최상위에서 받는다 — 문턱을 넘기 전 포인터가 다른 MDI 창 위로 가도(캡처 전) 끌기를 이어 가기 위해서다
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => OnMoved(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => OnReleased(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        // 캡처 잃음으로 끝내지 않는다 — 복원·활성화로 Z순서가 바뀌면 캡처가 풀려 끌기가 중간에 멈췄다(web_c2dev_h 실측: 10단계 중 6~7단계에서 정지). 끝은 놓기(TopLevel)가 정한다
        InputElement.DoubleTappedEvent.AddClassHandler<InputElement>(OnDoubleTapped);
    }

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        Reset();
#if PD_DIAGNOSTICS
        Pd.Client.Data.Logging.PdLog.Info("window", $"제목줄 누름 진단 click={e.ClickCount} hit={Hit(window, e)?.GetType().Name} src={(e.Source as Visual)?.GetType().Name} header={IsOnHeader(window, Hit(window, e))} state={WindowCommands.FindDocument(window.DataContext)?.MdiState} handled={e.Handled}");
#endif
        // 원래 누름 시각을 알면(웹 실제 입력) 그 시각 차만으로 짝을 정한다 — ClickCount 는 처리 시점 기준이라 지연에 따라 끊기기도(첫 누름 처리 지연),
        // 이어지기도(지연된 첫 누름 직후 두 번째) 한다(D-C2-19b). 시각을 모르는 누름(데스크톱·자동화 주입)만 아래 ClickCount·보조 판정을 쓴다.
        var rawPair = RawPair(window, e);
        if (rawPair == true && TryToggle(window, e))
        {
            ClearPair(); // 세 번째 누름은 직전 짝 기록이 없어 새 짝의 첫 누름이 된다
            return;
        }

        // 짝수번째 누름 = 더블클릭(Avalonia Gestures 와 같은 규칙) — 0.5초 안 연속 더블클릭이 3·4 로 세어져 빠지던 경우(web_c2dev_o 실측)
        if (rawPair is null && e.ClickCount % 2 == 0 && !IsAuxTail(window) && TryToggle(window, e))
        {
            ClearPair();
            return;
        }

        // 웹 보조 판정 — OS(브라우저 입력 처리)의 누름 횟수가 앱 처리 지연으로 끊긴 경우만 메운다(데스크톱은 거치지 않음)
        if (rawPair is null && e.ClickCount % 2 == 1 && IsAuxSecond(window, e) && TryToggle(window, e))
        {
#if PD_DIAGNOSTICS
            Pd.Client.Data.Logging.PdLog.Info("window", $"제목줄 보조 더블클릭 토글 gap={Environment.TickCount64 - s_pairPressAt} idle={(s_pairIdleAt == 0 ? -1 : Environment.TickCount64 - s_pairIdleAt)}");
#endif
            (s_auxWindow, s_auxToggleAt) = (window, Environment.TickCount64);
            ClearPair();
            return;
        }

        RecordPress(window, e);

        // 두 번째 누름(짝)이면 끌어 복원을 시작하지 않는다. 원래 시각으로 짝이 아니라고 정한 누름은 첫 누름이다
        if ((rawPair ?? e.ClickCount % 2 == 0) || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            || WindowCommands.FindDocument(window.DataContext) is not { MdiState: MdiWindowState.Maximized } doc
            || WindowCommands.ChromeOf(doc).IsLocked || !IsOnHeader(window, Hit(window, e))
            || PanelOf(window) is not { } parent)
        {
            return;
        }

        (s_window, s_doc) = (window, doc);
        s_press = e.GetPosition(parent);
        s_ratioX = window.Bounds.Width > 0 ? Math.Clamp(e.GetPosition(window).X / window.Bounds.Width, 0, 1) : 0;
        s_grab = new Point(0, e.GetPosition(window).Y);
    }

    private static void OnMoved(PointerEventArgs e)
    {
        if (s_window is not { } window || s_doc is null || PanelOf(window) is not { } parent)
        {
            return;
        }

        var p = e.GetPosition(parent);
        if (!s_dragging)
        {
            if (Math.Abs(p.X - s_press.X) < Threshold && Math.Abs(p.Y - s_press.Y) < Threshold)
            {
                return;
            }

            var normal = s_doc.MdiBounds; // 최대화 동안에도 보통 상태 경계가 남아 있다
            App.Shell?.SetState(s_doc, MdiWindowState.Normal);
            App.Shell?.Activate(s_doc); // 끌어 복원한 창이 맨 위·활성(Windows 끌어 복원과 같음)
            s_grab = new Point(s_ratioX * normal.Width, s_grab.Y);
            s_dragging = true;
            e.Pointer.Capture(window);
        }

        var b = s_doc.MdiBounds;
        s_doc.MdiBounds = new DockRect(p.X - s_grab.X, Math.Max(0, p.Y - s_grab.Y), b.Width, b.Height);
        e.Handled = true;
    }

    private static void OnReleased(PointerReleasedEventArgs e)
    {
        if (s_window is not null && s_dragging)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        Reset();
    }

    /// <summary>
    /// U9 ① 제목줄 두 번째 누름 = 최대화↔복원. Dock 은 보통 상태 첫 누름에서 끌기를 시작(포인터 캡처·Handled)해 DoubleTapped 제스처가 생기지 않았다
    /// (웹 실측 01:28 — 더블클릭해도 Normal 유지). 그래서 누름 횟수로 직접 판정하고, 남은 Dock DoubleTapped 처리는 OnDoubleTapped 가 막아 두 번 바뀌지 않게 한다.
    /// </summary>
    private static bool TryToggle(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || !IsOnHeader(window, Hit(window, e))
            || WindowCommands.FindDocument(window.DataContext) is not { } doc
            || (WindowCommands.ChromeOf(doc).IsLocked && WindowCommands.LockBlocksMinMax))
        {
            return false;
        }

        App.Shell?.SetState(doc, doc.MdiState == MdiWindowState.Maximized ? MdiWindowState.Normal : MdiWindowState.Maximized);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// 제목줄 DoubleTapped 는 TryToggle 이 이미 처리했다 — Dock 의 머리줄 처리기(HeaderDoubleTappedHandler)가 한 번 더 뒤집지 않게 소비한다.
    /// DoubleTapped 는 버블 전용이라 머리줄보다 먼저 받으려면 원천 요소 자신에서 받아야 한다(모든 InputElement 클래스 처리기, 원천일 때만 판정).
    /// </summary>
    private static void OnDoubleTapped(InputElement sender, TappedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source) && sender is Visual v && v.FindAncestorOfType<MdiDocumentWindow>(includeSelf: true) is { } window
            && IsOnHeader(window, v))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 웹 보조 더블클릭 판정 — 이번 누름이 직전 누름의 짝인가.
    /// 원인(확인): Avalonia.Browser 는 누름 시각을 JS 이벤트 시각이 아니라 처리 시점 Stopwatch 로 찍는다(vendor/avalonia-12.1.4/src/Browser/Avalonia.Browser/BrowserInputHandler.cs:49).
    /// 그래서 첫 누름 처리로 주 스레드가 바쁘면 그 지연이 두 누름 간격에 더해져 더블탭 시간을 넘고 ClickCount 가 1 로 다시 세어진다
    /// (web_c2dev_t 실측: 바둑판 24창에서 두 누름 411~786ms → 3회 중 2회 click=1, 단독 창 문턱 372ms 이음·515ms 끊김 — logs/odev_c2/fix/cause.md).
    /// 판정(이 앱의 규칙, 추정 기반): 같은 창·4 DIP 안·첫 누름부터 1.5초 안이고, 첫 누름 뒤 앱이 한가해진 시각부터 이번 누름까지가 더블탭 시간 안이면 짝이다.
    /// 앱이 한가해지기 전에 온 누름은 사용자 간격을 알 수 없어 짝으로 본다(1.5초 상한이 오인 범위를 막는다). 데스크톱은 OS 누름 횟수만 쓴다.
    /// 원래 시각을 아는 누름은 이 추정을 거치지 않는다(RawPair). 이 경로는 시각을 모르는 누름(자동화 주입 등)만 쓴다.
    /// </summary>
    private static bool IsAuxSecond(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        if (!OperatingSystem.IsBrowser() || !ReferenceEquals(s_pairWindow, window) || TopLevel.GetTopLevel(window) is not { } top)
        {
            return false;
        }

        var now = Environment.TickCount64;
        var p = e.GetPosition(top);
        var userGap = s_pairIdleAt == 0 ? 0 : now - s_pairIdleAt;
        return now - s_pairPressAt <= PairCapMs && userGap <= DoubleTapMs()
            && Math.Abs(p.X - s_pairPoint.X) <= PairSlop && Math.Abs(p.Y - s_pairPoint.Y) <= PairSlop;
    }

    /// <summary>보조 판정으로 토글한 직후 OS 누름 횟수가 짝수로 이어진 누름(세 번째 누름)인가 — 한 번 더 뒤집지 않고 새 짝의 첫 누름으로 센다.</summary>
    private static bool IsAuxTail(MdiDocumentWindow window) =>
        ReferenceEquals(s_auxWindow, window) && Environment.TickCount64 - s_auxToggleAt <= DoubleTapMs();

    /// <summary>
    /// 원래 입력 시각 기준 짝 판정(D-C2-19·19b). 지금 누름의 시각을 모르면 null(호출자는 ClickCount·보조 판정을 쓴다).
    /// 알면 true/false 로 확정한다: 직전 누름이 같은 창·4 DIP 안이고 두 원래 시각 차가 더블탭 시간 안이면 짝이다. 직전 기록이 없거나 그 시각을 모르면 첫 누름(false).
    /// 사용자 실제 간격이라 처리 지연과 무관하다 — 1.5초 상한·한가 추정을 쓰지 않고, ClickCount=2 라도 간격이 길면 별개의 두 클릭으로 본다.
    /// </summary>
    private static bool? RawPair(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        if (PressTimeSource?.CurrentPressTime() is not { } now)
        {
            return null;
        }

        var pair = s_pairRawAt is { } before && now >= before && now - before <= DoubleTapMs()
            && ReferenceEquals(s_pairWindow, window) && TopLevel.GetTopLevel(window) is { } top
            && Math.Abs(e.GetPosition(top).X - s_pairPoint.X) <= PairSlop && Math.Abs(e.GetPosition(top).Y - s_pairPoint.Y) <= PairSlop;
#if PD_DIAGNOSTICS
        Pd.Client.Data.Logging.PdLog.Info("window", $"제목줄 원래 시각 판정 click={e.ClickCount} raw={(s_pairRawAt is { } b ? now - b : -1)} pair={pair}");
#endif
        return pair;
    }

    /// <summary>웹에서 왼쪽 버튼 누름만 짝의 첫 누름으로 기록하고(다른 버튼은 기록을 지워 짝이 되지 않게), 앱이 한가해진 시각을 Background 우선순위 콜백으로 찍는다.</summary>
    private static void RecordPress(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        if (!OperatingSystem.IsBrowser() || TopLevel.GetTopLevel(window) is not { } top || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            ClearPair();
            return;
        }

        var seq = ++s_pairSeq;
        (s_pairWindow, s_pairPoint, s_pairPressAt, s_pairIdleAt) = (window, e.GetPosition(top), Environment.TickCount64, 0);
        s_pairRawAt = PressTimeSource?.CurrentPressTime();
        s_auxWindow = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (seq == s_pairSeq)
            {
                s_pairIdleAt = Environment.TickCount64;
            }
        }, DispatcherPriority.Background);
    }

    private static void ClearPair() => (s_pairWindow, s_pairIdleAt, s_pairRawAt) = (null, 0, null);

    private static long DoubleTapMs() =>
        (long)(Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse).TotalMilliseconds ?? 500);

    private static void Reset()
    {
        (s_window, s_doc, s_dragging) = (null, null, false);
    }

    /// <summary>
    /// MdiBounds 의 좌표 기준 = MDI 배치 패널. 창의 바로 위 부모는 창마다 자기 자리에 놓인 래퍼라 그 기준으로 재면 창의 원래 위치만큼 어긋났다
    /// (web_c2dev_n 실측: 끌어 복원 결과가 정확히 (-48,-48) — 창의 원래 위치).
    /// </summary>
    private static Visual? PanelOf(MdiDocumentWindow window) => window.FindAncestorOfType<MdiLayoutPanel>();

    /// <summary>
    /// 포인터 아래 실제 요소. 앞선 누름으로 Dock 이 포인터를 캡처한 채이면 e.Source 가 캡처 대상(창 자신)이 되어 제목줄 판정이 빗나간다
    /// (web_c2dev_l 실측: 더블클릭·끌어 복원이 h 에서는 됐다가 l 에서 안 됨 — 원인 추정). 그래서 좌표로 다시 찾는다.
    /// </summary>
    private static Visual? Hit(MdiDocumentWindow window, PointerEventArgs e) =>
        window.InputHitTest(e.GetPosition(window)) as Visual ?? e.Source as Visual;

    /// <summary>원천이 제목줄(PART_Header) 안이고 버튼(캡션·기능 버튼·로고) 위가 아닌가.</summary>
    private static bool IsOnHeader(MdiDocumentWindow window, Visual? source)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, window); v = v.GetVisualParent())
        {
            if (v is Button)
            {
                return false;
            }

            if (v is Control { Name: TemplateParts.Header })
            {
                return true;
            }
        }

        return false;
    }
}
