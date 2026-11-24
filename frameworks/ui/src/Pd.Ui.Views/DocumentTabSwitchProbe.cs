// 탭형 문서 전환 단계별 시간 계측(진단판 전용) — 탭 누름→본문 교체 요청·칸 준비·표시 전환·측정·배치·그려짐을 PerfProbe 조작 기록 ui.tab.* 로 남긴다
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.Diagnostics;

namespace Pd.Ui.Views;

/// <summary>
/// DocumentTabKeepAlive 가 부른다. 제품판은 PerfProbe.IsEnabled 가 컴파일 상수 false 라 모든 분기가 빠지고 처리기도 걸지 않는다(비용 0).
/// 기록 이름(웹 진단판 perf JSON 의 operations, 단위 ms):
/// - ui.tab.input  : 탭 누름(DocumentTabStripItem PointerPressed, Tunnel) → 본문 교체 요청(템플릿 Build) 진입. Dock 모델 활성 변경과 ActiveChanged 구독자(상태줄·창 목록·자동 저장 표시 등) 몫이다.
/// - ui.tab.build  : 템플릿 Build 동기 시간(칸 찾기·정리, 머리 먼저 그리기가 아니면 표시 전환까지 포함).
/// - ui.tab.wait   : 머리 먼저 그리기 대기(Build 끝 → 다음 프레임 뒤 표시 전환 시작).
/// - ui.tab.fetch  : 처음 보는 탭의 문서 뷰를 공용 캐시에서 받아 칸에 붙이는 시간(재방문이면 기록 없음).
/// - ui.tab.show   : 칸 IsVisible 전환 시간. 보이게 될 때 Avalonia 가 하위 트리 전체를 훑는다(Layoutable.AncestorBecameVisible — 출처 아래).
/// - ui.tab.measure / ui.tab.arrange : 표시 전환 뒤 첫 측정·배치(칸 묶음 Panel 의 Measure/ArrangeOverride 안, 하위 문서 뷰 몫 포함).
/// - ui.tab.switch : sync = 교체 요청 → 표시 전환 끝, frame = 교체 요청 → 표시 전환 뒤 두 번째 프레임(새 본문이 그려짐).
/// 렌더 몫은 대략 switch.frame − (build + wait + fetch + show + measure + arrange) 로 본다(추정 분해 — 프레임 경계가 섞인다).
/// 출처: Avalonia master src/Avalonia.Base/Layout/Layoutable.cs — IsVisible 이 true 가 되면 자기 측정을 무효화하고 모든 하위 요소에 AncestorBecameVisible 을 재귀 호출(무효인 것만 다시 측정 등록).
/// </summary>
internal static class DocumentTabSwitchProbe
{
    private static bool s_installed;
    private static double s_pressedAt = -1;
    private static bool s_layoutOpen;

    /// <summary>탭 누름 시각 기록 처리기를 1회 건다(진단판만).</summary>
    public static void EnsureInstalled()
    {
        if (!PerfProbe.IsEnabled || s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<DocumentTabStripItem>(
            (_, _) => s_pressedAt = PerfProbe.Current.NowMs, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>지금 시각(진단판). 제품판은 0.</summary>
    public static double Now() => PerfProbe.IsEnabled ? PerfProbe.Current.NowMs : 0;

    /// <summary>본문 교체 요청(Build) 진입. 1초 안의 탭 누름이 있으면 누름→요청 시간을 ui.tab.input 으로 남긴다.</summary>
    public static double BeginBuild()
    {
        if (!PerfProbe.IsEnabled)
        {
            return 0;
        }

        var now = PerfProbe.Current.NowMs;
        if (s_pressedAt >= 0 && now - s_pressedAt < 1000)
        {
            Record("input", now - s_pressedAt);
        }

        s_pressedAt = -1;
        return now;
    }

    /// <summary>start 부터 지금까지를 ui.tab.{part} 로 남긴다.</summary>
    public static void End(string part, double start)
    {
        if (PerfProbe.IsEnabled)
        {
            Record(part, PerfProbe.Current.NowMs - start);
        }
    }

    /// <summary>표시 전환 직후: 다음 측정·배치 1회를 기록하게 하고, 두 번째 프레임까지를 ui.tab.switch 로 남긴다.</summary>
    public static void Shown(Visual anchor, double switchStart)
    {
        if (!PerfProbe.IsEnabled || switchStart <= 0)
        {
            return;
        }

        s_layoutOpen = true;
        var probe = PerfProbe.Current;
        var sync = probe.NowMs - switchStart;
        if (TopLevel.GetTopLevel(anchor) is { } top)
        {
            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => probe.RecordOperation("ui.tab.switch", sync, probe.NowMs - switchStart)));
        }
        else
        {
            probe.RecordOperation("ui.tab.switch", sync, sync);
        }
    }

    /// <summary>칸 묶음 Panel 의 측정. 표시 전환 뒤 첫 측정만 잰다.</summary>
    public static Size Measure(Size available, Func<Size, Size> core)
    {
        if (!PerfProbe.IsEnabled || !s_layoutOpen)
        {
            return core(available);
        }

        var t = PerfProbe.Current.NowMs;
        var size = core(available);
        End("measure", t);
        return size;
    }

    /// <summary>칸 묶음 Panel 의 배치. 표시 전환 뒤 첫 배치만 재고 기록 창을 닫는다.</summary>
    public static Size Arrange(Size final, Func<Size, Size> core)
    {
        if (!PerfProbe.IsEnabled || !s_layoutOpen)
        {
            return core(final);
        }

        s_layoutOpen = false;
        var t = PerfProbe.Current.NowMs;
        var size = core(final);
        End("arrange", t);
        return size;
    }

    private static void Record(string part, double ms) => PerfProbe.Current.RecordOperation("ui.tab." + part, ms, ms);
}
