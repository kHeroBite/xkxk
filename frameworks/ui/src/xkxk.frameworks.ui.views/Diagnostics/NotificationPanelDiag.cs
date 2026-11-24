// 알림 패널 화면 진단 계측(진단 빌드 전용) — 생성·부착 추적, 측정·배치 시간과 받은 크기 변화. --notify-panel empty 판독은 PocDockFactory.Diag 에 둔다
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Diagnostics; // D-LAYER: 진단 전용 파일이라 Views/ 에서 Diagnostics/ 로 옮겼다(team-lead 16:16)

/// <summary>
/// NotificationPanelView 가 필드 초기화자로 보유하는 진단 계측 객체(인스턴스 1개 = 화면 1개).
/// 생성자는 인자가 없고 화면은 메서드 인자로 받는다 — 필드 초기화자는 base 생성자보다 먼저 실행되기 때문이다.
/// 제품 빌드에는 이 파일의 내용이 없다.
/// </summary>
internal sealed class NotificationPanelDiag
{
    // ── Q4 계측(odev-3): 알림 패널의 측정·배치 시간과 '받은 크기가 바뀐 횟수' ──
    // perf 조작 기록 notify.measure·notify.arrange(ms, 호출마다), 지표 notify_measure_sizechg·notify_arrange_sizechg(받은 크기가 직전과 달라진 횟수).
    // Dock 알림 패널 안 화면만 센다(고아 화면 제외). 테마·글꼴 전환 1회 전후로 perf-reset → perf.get 하면 그 전환의 몫이 나온다.
    private static int s_measureSizeChanges;
    private static int s_arrangeSizeChanges;

    private Size _lastMeasure = new(-1, -1);
    private Size _lastArrange = new(-1, -1);
    private int _seq; // 이 화면의 생성 순번(CreatedCount 와 같은 번호)
    private int _attachN; // 이 화면이 붙은 횟수

    /// <summary>--notify-panel empty 면 true(패널 틀만 두고 내용 화면을 만들지 않음).</summary>
    internal static bool EmptyContent => DockFactoryDiag.NotificationContentEmpty;

    /// <summary>
    /// 화면 생성자에서 호출. 생성 수를 센 뒤 호출 경로를 남긴다.
    /// StackTrace(2) 가 이 메서드와 화면 생성자를 건너뛰어 '화면을 만든 쪽'부터 기록한다(앱 로그·Android logcat).
    /// 프레임마다 한 줄 — 한 줄로 이으면 logcat 줄 길이 제한에 3프레임에서 잘렸다(otest-m-1 C_nogrid). 같은 머리말로 grep 해 모은다.
    /// </summary>
    internal static void OnCreated()
    {
        NotificationPanelProbe.CreatedCount++;
        var st = new StackTrace(2, false);
        Console.WriteLine($"[notify] view created #{NotificationPanelProbe.CreatedCount} {DockFactoryDiag.NotificationDiagSwitches} frames={st.FrameCount}");
        for (var i = 0; i < st.FrameCount; i++)
        {
            var m = st.GetFrame(i)?.GetMethod();
            Console.WriteLine($"[notify] view created #{NotificationPanelProbe.CreatedCount} f{i:00} {(m is null ? "?" : m.DeclaringType?.FullName + "." + m.Name)}");
        }
    }

    /// <summary>Dock 알림 패널 안에 실제로 붙거나 떨어질 때 적용 방식을 기록한다(생성만 된 화면은 세지 않는다).</summary>
    internal static void ViewAttached(bool attached)
    {
        NotificationPanelProbe.AttachedCount = Math.Max(0, NotificationPanelProbe.AttachedCount + (attached ? 1 : -1));
        NotificationPanelProbe.LayoutMode = NotificationPanelProbe.AttachedCount > 0 ? "new" : null;
        Console.WriteLine($"[notify] view {(attached ? "attached" : "detached")} attachedNow={NotificationPanelProbe.AttachedCount} created={NotificationPanelProbe.CreatedCount}");
    }

    internal long LayoutBegin() => Stopwatch.GetTimestamp();

    internal void LayoutEnd(NotificationPanelView view, string phase, long t, Size given)
    {
        if (!view.IsDockHosted)
        {
            return;
        }

        var ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        PerfProbe.Current.RecordOperation("notify." + phase, ms, ms);
        ref var last = ref phase == "measure" ? ref _lastMeasure : ref _lastArrange;
        if (last.Width >= 0 && !last.Equals(given))
        {
            var n = phase == "measure" ? ++s_measureSizeChanges : ++s_arrangeSizeChanges;
            PerfProbe.Current.SetMetric($"notify_{phase}_sizechg", n);
            // 소수 셋째 자리까지 — Android 소수 배율(Fold5 약 2.6)에서 레이아웃 반올림으로 생기는 미세한 흔들림도 보이게
            PerfProbe.Current.SetText($"notify_{phase}_lastsize", $"{last.Width:0.###}x{last.Height:0.###}->{given.Width:0.###}x{given.Height:0.###}");
        }

        last = given;
    }

    /// <summary>
    /// 생성자에서 1회. 부착·분리마다 `[notify] view attach-chain #생성순번 ...` 한 줄을 남긴다(odev-3, Q4 고아 화면 추적).
    /// 생성자 시점에는 부모가 없어 사슬을 알 수 없으므로, 생성 직후 첫 부착(first=1)의 사슬이 곧 생성한 presenter 의 위치다.
    /// 사슬 항목: 형식#Name@템플릿주인형식, 화면에서 TopLevel 쪽으로. 끝에 TopLevel 형식, Overlay/Popup 조상 여부, DockControl 조상 여부.
    /// </summary>
    internal void TraceHost(NotificationPanelView view)
    {
        _seq = NotificationPanelProbe.CreatedCount;
        view.AttachedToVisualTree += (_, _) => Chain(view, ++_attachN == 1 ? "attach first=1" : "attach first=0");
        view.DetachedFromVisualTree += (_, _) => Chain(view, "detach");
    }

    private void Chain(NotificationPanelView view, string what)
    {
        var parts = new List<string>();
        bool overlay = false, popup = false, dock = false;
        foreach (var v in view.GetVisualAncestors())
        {
            var se = v as StyledElement;
            var type = v.GetType().Name;
            overlay |= v is OverlayLayer;
            popup |= v is PopupRoot || type == "OverlayPopupHost"; // 단일 뷰(Android·웹) 팝업은 OverlayPopupHost — 형식 이름으로 판정
            dock |= v is DockControl;
            parts.Add($"{type}#{(string.IsNullOrEmpty(se?.Name) ? "-" : se!.Name)}@{se?.TemplatedParent?.GetType().Name ?? "-"}");
        }

        var top = TopLevel.GetTopLevel(view)?.GetType().Name ?? "none";
        var dc = view.DataContext is null ? "null" : view.DataContext.GetType().Name;
        Console.WriteLine($"[notify] view attach-chain #{_seq} {what} top={top} overlay={(overlay ? 1 : 0)} popup={(popup ? 1 : 0)} dock={(dock ? 1 : 0)} visible={(view.IsEffectivelyVisible ? 1 : 0)} dc={dc} depth={parts.Count}");
        // 한 줄이 길면 logcat 에서 잘리므로 사슬은 10개씩 나눠 같은 머리말로 남긴다
        for (var i = 0; i < parts.Count; i += 10)
        {
            Console.WriteLine($"[notify] view attach-chain #{_seq} {what} c{i / 10:00} {string.Join(" > ", parts.GetRange(i, Math.Min(10, parts.Count - i)))}");
        }
    }
}
#endif
