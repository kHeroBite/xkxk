// MDI 끌기 프레임별 진단의 누름·이동·놓기 단계 시간(TopLevel 경로 전체·캡처 해제 시점·Pd 처리기별 범위 시간) — PD_DIAGNOSTICS 전용, 01a P1
#if PD_DIAGNOSTICS
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// 단계 열(행마다, 그 rAF 간격 안에서 일어난 몫의 합):
/// down_ms = 누름 TopLevel 터널~버블(Dock 누름·Pd 누름 처리기 합), move_ms·move_max_ms = 포인터 이동 TopLevel 터널~버블 합·최대(Dock 끌기 계산·자석·Pd 이동 처리기),
/// tj_begin_ms = 탭 합류 끌기 시작(표적 수집·아이콘 켜기), flush_started_ms = 첫 이동 뒤 정지 창 찾기·가벼운 크롬 걸기(MdiStaticLight.FlushStarted, 디스패처 Normal),
/// rel_total_ms = 놓기 TopLevel 터널~버블, rel_caplost_ms = 놓기 시작부터 끄는 창 캡처 해제(PointerCaptureLost)까지(그 앞 몫 ≈ Dock EndDrag),
/// rel_tabjoin_ms·rel_tabjoin_reset_ms = 탭 합류 놓기 처리 전체·그 안 Reset, rel_snap_ms·rel_snap_reset_ms·rel_snap_log_ms = 스냅 놓기 처리 전체·Reset·결과 로그,
/// apply_ms = 놓기 뒤 Post 된 스냅 적용 실행. 놓기 나머지(rel_total − Pd 처리기 − 캡처 앞 몫)는 캡처 해제 뒤 호버 재계산 등 Avalonia 입력 처리로 본다(근사).
/// 범위 시간은 Scope 구조체(using var — 박싱·할당 없음)로 잰다. 기록 중이 아니면 Time 은 빈 Scope 를 돌려 아무것도 하지 않는다.
/// </summary>
public static partial class MdiDragFrameDiag
{
    /// <summary>Pd 처리기 범위 시간 종류.</summary>
    public enum Stage { TabJoinBegin, FlushStarted, RelTabJoin, RelTabJoinReset, RelSnap, RelSnapReset, RelSnapLog, Apply }

    private static long s_downStart, s_moveStart;

    /// <summary>범위 시간 1회(using var). Dispose 때 현재 행의 해당 단계 열에 더한다.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly Stage _stage;
        private readonly long _t0;

        internal Scope(Stage stage, long t0) => (_stage, _t0) = (stage, t0);

        public void Dispose()
        {
            if (_t0 != 0) { Add(_stage, Ms(Now - _t0)); }
        }
    }

    /// <summary>기록 중이면 지금 시각으로 범위를 연다(아니면 빈 범위).</summary>
    public static Scope Time(Stage stage) => s_active ? new Scope(stage, Now) : default;

    private static void Add(Stage stage, float ms)
    {
        if (!s_active || !s_haveFrame) { return; }
        switch (stage)
        {
            case Stage.TabJoinBegin: s_cur.TjBeginMs += ms; break;
            case Stage.FlushStarted: s_cur.FlushStartedMs += ms; break;
            case Stage.RelTabJoin: s_cur.RelTabJoinMs += ms; break;
            case Stage.RelTabJoinReset: s_cur.RelTabJoinResetMs += ms; break;
            case Stage.RelSnap: s_cur.RelSnapMs += ms; break;
            case Stage.RelSnapReset: s_cur.RelSnapResetMs += ms; break;
            case Stage.RelSnapLog: s_cur.RelSnapLogMs += ms; break;
            case Stage.Apply: s_cur.ApplyMs += ms; break;
        }
    }

    private static void InstallStages()
    {
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => { if (s_armed) { s_downStart = Now; } }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => OnPressedEnd(), RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, _) => { if (s_active && !s_released) { s_moveStart = Now; } }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, _) => OnMovedEnd(), RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((_, _) => OnCaptureLost(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // 누름 처리 끝. 첫 rAF 전이면 첫 행에 싣도록 보류한다(OnFrame)
    private static void OnPressedEnd()
    {
        if (s_downStart == 0) { return; }
        var ms = Ms(Now - s_downStart);
        s_downStart = 0;
        if (!s_active) { return; } // MDI 창 누름이 아니었다
        if (s_haveFrame) { s_cur.DownMs = ms; }
        else { s_pendDownMs = ms; }
    }

    private static void OnMovedEnd()
    {
        if (s_moveStart == 0) { return; }
        var ms = Ms(Now - s_moveStart);
        s_moveStart = 0;
        if (!s_active || !s_haveFrame) { return; }
        s_cur.MoveMs += ms;
        if (ms > s_cur.MoveMaxMs) { s_cur.MoveMaxMs = ms; }
    }

    // 놓기 처리 중 첫 캡처 해제 시점(놓기 시작 기준)
    private static void OnCaptureLost()
    {
        if (s_active && s_haveFrame && s_relStart > 0 && s_cur.RelCapLostMs < 0) { s_cur.RelCapLostMs = Ms(Now - s_relStart); }
    }
}
#endif
