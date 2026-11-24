// MDI 창 하나가 움직이는 동안 같은 MDI 영역의 움직이지 않는 다른 창에 가벼운 크롬(.pdMovingSibling)을 걸고 떼는 적용부(옵션 MdiMoveLight.Siblings, 판정은 MdiMoveLightener)
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Ui.Views;

/// <summary>
/// 끄는 창이 지나가는 화면 다시 그리기 영역(옛 위치+새 위치)에 걸린 정지 창은 프레임마다 그림자·둥근 경로 클립과 함께 다시 그려진다(Avalonia 12.1.4 디컴파일, odev-11 설계).
/// 옵션이 켜져 있으면 이동이 시작될 때(첫 위치 변화) 같은 MDI 영역의 움직이지 않는 다른 보통 상태 창에 .pdMovingSibling 을 걸어
/// 테마가 그림자를 이동용 리소스(기본 없음)로, 바깥 모서리·잘라냄 반경을 0 으로 바꾸게 하고, 이동이 모두 끝나면 뗀다.
/// 화면 차이(끄는 동안 뒤 창 그림자·둥근 모서리가 사라짐)는 놓은 뒤 QuietMs 에 돌아오는 일시 변화라 기본은 켬이다(c12 판정, 끄는 창 자신의 가벼운 크롬과 같은 원칙). 테마 규칙은 Themes/MdiDocumentWindowTheme.axaml 의 .pdMoving 절, 규칙 정본은 plan/09_화면설계/01_화면설계_공통원칙.md §4.2(MdiMoveLightSiblings).
/// </summary>
internal static partial class MdiStaticLight
{
    public const string SiblingClass = "pdMovingSibling";

    // 이 클래스가 가벼운 크롬을 건 창(원복 때 이 창들만 되돌린다)
    private static readonly HashSet<MdiDocumentWindow> s_lit = new();

    // 같은 디스패처 차례에 움직이기 시작한 창(ScheduleMoveStarted). 정지 창 찾기(MdiStaticCache.Siblings)는 MDI 영역 시각 트리 전체를 훑으므로
    // 창마다 하지 않고 영역마다 한 번만 한다 — 전체 이동 20창에서 20번 돌아 회차 첫 이동이 336~394ms 걸렸다(odebug-a2 실측, siblings 끔 62~88ms)
    private static readonly List<MdiDocumentWindow> s_started = new();
    private static Func<MdiDocumentWindow, bool>? s_isMoving;

    /// <summary>
    /// moving 이 움직이기 시작했다(MdiMoveLightener). moving 에 걸려 있던 정지 창 가벼운 크롬·캐시는 바로 떼고, 다른 정지 창에 거는 일(옵션 Siblings·StaticCache)은
    /// 이번 디스패처 차례가 끝난 뒤 한 번에 한다. Normal 우선순위라 다음 렌더(Render)보다 먼저 돈다. 그 사이 함께 움직이기 시작한 창(전체 이동·묶음)은 정지 창으로 보지 않는다.
    /// </summary>
    public static void ScheduleMoveStarted(MdiDocumentWindow moving, Func<MdiDocumentWindow, bool> isMoving)
    {
        FlushRestore(); // P2: 앞 이동의 정지 창 복원이 남았으면 새 이동을 걸기 전에 모두 마친다(MdiStaticLight.Restore.cs)
        Release(moving);
        MdiStaticCache.Release(moving);
        MdiMoveHitTest.Begin(moving); // K3: 끄는 창 내용 히트 테스트 끔은 바로 건다(원복은 아래 Release — End 의 QuietMs 원복과 같은 시점)
        s_isMoving = isMoving;
        s_started.Add(moving);
        if (s_started.Count == 1)
        {
            Dispatcher.UIThread.Post(FlushStarted, DispatcherPriority.Normal);
        }
    }

    private static void FlushStarted()
    {
#if PD_DIAGNOSTICS
        using var diag = Pd.Poc.Shell.Diagnostics.MdiDragFrameDiag.Time(Pd.Poc.Shell.Diagnostics.MdiDragFrameDiag.Stage.FlushStarted); // 01a P1: 정지 창 찾기·걸기 시간
#endif
        var isMoving = s_isMoving;
        var covered = new HashSet<MdiDocumentWindow>(); // 이미 찾은 정지 창 목록에 든 창 = 같은 MDI 영역(창마다 시각 부모가 달라 부모로는 못 묶는다 — odebug-a2 1차 실측)
        foreach (var w in s_started)
        {
            if (isMoving is null || !isMoving(w) || covered.Contains(w))
            {
                continue; // 그새 원복된 창(EndAll 등)·같은 영역 창은 건너뛴다
            }

            var siblings = MdiStaticCache.Siblings(w);
            covered.UnionWith(siblings);
            var spread = Pd.Poc.Shell.Docking.MdiMoveLight.Siblings && Pd.Poc.Shell.Docking.MdiMoveLight.SiblingsPerFrame > 0; // 옵션 B(01a) — 0 이면 아래 지금 경로 그대로
            if (Pd.Poc.Shell.Docking.MdiMoveLight.Siblings) { Light(w, siblings, isMoving); } // 캐시보다 먼저 걸어 캐시가 가벼운 모양을 담게 한다
            if (Pd.Poc.Shell.Docking.MdiMoveLight.StaticCache)
            {
                if (spread) { s_spreadCache.Add(w); } // 분산이면 캐시는 가벼운 크롬을 다 건 뒤 1회(가벼운 모양을 담는 순서 유지)
                else { MdiStaticCache.OnMoveStarted(w, isMoving); }
            }
        }

        s_started.Clear();
        StartSpread(isMoving);
    }

    // 옵션 B(MdiMoveLight.SiblingsPerFrame > 0): 아직 걸지 않은 정지 창·분산 뒤 캐시를 걸 끄는 창. s_spreadGen 이 바뀌면(ReleaseAll) 남은 프레임 콜백은 아무것도 하지 않는다
    private static readonly List<MdiDocumentWindow> s_spreadQueue = new();
    private static readonly List<MdiDocumentWindow> s_spreadCache = new();
    private static int s_spreadGen;
    private static bool s_spreadRunning;

    private static void StartSpread(Func<MdiDocumentWindow, bool>? isMoving)
    {
        if (s_spreadRunning || isMoving is null || (s_spreadQueue.Count == 0 && s_spreadCache.Count == 0))
        {
            return; // 이미 돌고 있으면 큐에 더한 것을 이어서 건다
        }

        var anchor = s_spreadQueue.Count > 0 ? s_spreadQueue[0] : s_spreadCache[0];
        if (Avalonia.Controls.TopLevel.GetTopLevel(anchor) is not { } top)
        {
            SpreadStep(s_spreadGen, int.MaxValue, isMoving); // 프레임을 얻을 수 없으면 한꺼번에(지금 동작과 같은 결과)
            return;
        }

        s_spreadRunning = true;
        var gen = s_spreadGen;
        top.RequestAnimationFrame(_ => SpreadFrame(top, gen, isMoving));
    }

    private static void SpreadFrame(Avalonia.Controls.TopLevel top, int gen, Func<MdiDocumentWindow, bool> isMoving)
    {
        if (gen != s_spreadGen)
        {
            return; // 취소됨(ReleaseAll 이 s_spreadRunning 도 풀었다)
        }

#if PD_DIAGNOSTICS
        Pd.Poc.Shell.Docking.MdiMoveLight.SpreadFrames++;
        Pd.Poc.Shell.Diagnostics.MdiDragFrameDiag.NoteSpreadStep(); // 01a P1 프레임별 기록 표시
#endif
        if (SpreadStep(gen, Pd.Poc.Shell.Docking.MdiMoveLight.SiblingsPerFrame, isMoving))
        {
            s_spreadRunning = false;
            return;
        }

        top.RequestAnimationFrame(_ => SpreadFrame(top, gen, isMoving));
    }

    // 큐 앞에서 count 개를 건다. 큐가 비면 미뤄 둔 캐시를 걸고 true. 옵션이 그새 0 이 됐으면 남은 것을 한꺼번에 건다
    private static bool SpreadStep(int gen, int count, Func<MdiDocumentWindow, bool> isMoving)
    {
        if (count <= 0) { count = int.MaxValue; }
        var take = Math.Min(count, s_spreadQueue.Count);
        for (var i = 0; i < take; i++) { LightOne(s_spreadQueue[i], isMoving); }
        s_spreadQueue.RemoveRange(0, take);
        if (s_spreadQueue.Count > 0) { return false; }

        foreach (var m in s_spreadCache.ToArray())
        {
            if (gen == s_spreadGen && isMoving(m)) { MdiStaticCache.OnMoveStarted(m, isMoving); }
        }

        s_spreadCache.Clear();
        return true;
    }

    private static void CancelSpread()
    {
#if PD_DIAGNOSTICS
        if (s_spreadQueue.Count > 0 || s_spreadCache.Count > 0) { Pd.Poc.Shell.Docking.MdiMoveLight.SpreadCanceled++; }
#endif
        s_spreadGen++;
        s_spreadRunning = false;
        s_spreadQueue.Clear();
        s_spreadCache.Clear();
    }

    /// <summary>moving 이 움직이기 시작했다. 같은 MDI 영역의 다른 정지 창(siblings, 미리 찾은 목록)에 가벼운 크롬을 건다. moving 자신은 뺀다(전체 이동처럼 여러 창이 함께 움직이는 경우).</summary>
    private static void Light(MdiDocumentWindow moving, List<MdiDocumentWindow> siblings, Func<MdiDocumentWindow, bool> isMoving)
    {
        ReleaseSibling(moving); // 정지 창 크롬만 뗀다 — 막 끈 K3 히트 테스트는 그대로 둔다
        if (Pd.Poc.Shell.Docking.MdiMoveLight.SiblingsPerFrame > 0)
        {
            s_spreadQueue.AddRange(siblings); // 옵션 B: 다음 프레임부터 나눠 건다(거는 시점에 아래 조건을 다시 본다)
            return;
        }

        foreach (var w in siblings)
        {
            LightOne(w, isMoving);
        }
    }

    private static void LightOne(MdiDocumentWindow w, Func<MdiDocumentWindow, bool> isMoving)
    {
        if (isMoving(w) || s_lit.Contains(w) || w.DataContext is not IMdiDocument { MdiState: MdiWindowState.Normal } || !w.IsVisible)
        {
            return;
        }

        w.Classes.Add(SiblingClass);
        MdiMoveLightener.SyncShadowOff(w);
        if (Pd.Poc.Shell.Docking.MdiMoveLight.SquareCorners) { w.Classes.Add(MdiMoveLightener.SquareClass); } // 모서리 0 은 옵션일 때만(2026-11-11)
        s_lit.Add(w);
    }

    /// <summary>창 하나의 가벼운 크롬을 뗀다(이 클래스가 건 것만).</summary>
    public static void Release(MdiDocumentWindow w)
    {
        MdiMoveHitTest.Release(w); // K3 원복(이 창을 끈 경우만)
        ReleaseSibling(w);
        ReleaseRestoring(w); // P2: 복원 대기 중이던 창(닫힘·분리·이동 시작)은 바로 떼고 목록에서 뺀다
        if (s_spreadQueue.Count > 0) { s_spreadQueue.RemoveAll(x => x == w); } // 옵션 B: 닫힘·분리·이동 시작한 창은 남은 적용에서 뺀다
        if (s_spreadCache.Count > 0) { s_spreadCache.Remove(w); }
    }

    private static void ReleaseSibling(MdiDocumentWindow w)
    {
        if (s_lit.Remove(w))
        {
            w.Classes.Remove(SiblingClass);
            w.Classes.Remove(MdiMoveLightener.SquareClass);
            MdiMoveLightener.SyncShadowOff(w);
        }
    }

    /// <summary>이동이 모두 끝났거나 옵션이 바뀌었다 — 건 가벼운 크롬을 모두 뗀다.</summary>
    public static void ReleaseAll()
    {
        MdiMoveHitTest.ReleaseAll(); // K3 원복
        FlushRestore(); // P2: 분산 복원 중이었으면 남은 것을 지금 모두 뗀다(옵션 변경·전부 해제)
        CancelSpread(); // 옵션 B: 다 걸기 전에 끝났으면 남은 적용 취소(원복은 걸린 창 s_lit 만 — 종전과 같은 결과)
        foreach (var w in s_lit)
        {
            w.Classes.Remove(SiblingClass);
            w.Classes.Remove(MdiMoveLightener.SquareClass);
            MdiMoveLightener.SyncShadowOff(w);
        }

        s_lit.Clear();
    }
}
