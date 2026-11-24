// MDI 이동이 모두 끝났을 때 정지 창 가벼운 크롬·캐시를 프레임당 N개씩 나눠 떼는 P2 경로(옵션 MdiMoveLight.RestorePerFrame, 01a P2 절)
using System.Collections.Generic;
using Avalonia.Media;
using Dock.Avalonia.Controls;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 지금 동작(RestorePerFrame=0): 끄는 창이 QuietMs 에 원복되면 MdiMoveLightener.ReleaseStaticIfIdle 이 정지 창 캐시·가벼운 크롬을 한 프레임에 모두 뗀다(창 4개면 4창이 한꺼번에 다시 그려진다).
/// P2(RestorePerFrame&gt;0): 끄는 창 자신은 이미 그 자리에서 원복됐고(MdiMoveLightener.End — 클래스·캐시·K3), 정지 창은 다음 프레임부터 한 프레임에 N개씩 뗀다.
/// 안전 경로: 새 이동 시작(ScheduleMoveStarted)·옵션 변경(ReleaseAll)은 FlushRestore 로 남은 것을 그 자리에서 모두 떼고, 창 닫힘·분리(Release)는 그 창만 바로 뗀다.
/// 세대 번호(s_restoreGen)가 바뀌면 남은 프레임 콜백은 아무것도 하지 않는다. 프레임을 얻을 수 없으면(TopLevel 없음) 한 번에 뗀다.
/// </summary>
internal static partial class MdiStaticLight
{
    // 아직 떼지 않은 정지 창(가벼운 크롬)과 캐시(MdiStaticCache 가 넘긴 것). 한 창이 둘 다에 있을 수 있다
    private static readonly List<MdiDocumentWindow> s_restoreLight = new();
    private static readonly List<MdiDocumentWindow> s_restoreCache = new();
    private static int s_restoreGen;
    private static bool s_restoreRunning;

    /// <summary>
    /// 움직이는 창이 하나도 남지 않았다(MdiMoveLightener.ReleaseStaticIfIdle). P2 가 0 이면 지금처럼 캐시·가벼운 크롬을 모두 바로 떼고,
    /// 0 보다 크면 K3 히트 테스트만 바로 원복하고 정지 창 해제는 다음 프레임부터 나눈다.
    /// </summary>
    public static void ReleaseAllWhenIdle()
    {
        if (xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RestorePerFrame <= 0)
        {
            MdiStaticCache.ReleaseAll();
            ReleaseAll();
            return;
        }

        MdiMoveHitTest.ReleaseAll(); // K3 는 화면 변화가 없어 바로 원복(놓은 창 자신의 원복을 늦추지 않는다)
        CancelSpread(); // 옵션 B: 다 걸기 전에 끝났으면 남은 적용 취소(ReleaseAll 과 같다)
        MdiStaticCache.HandOffAll(s_restoreCache);
        foreach (var w in s_lit)
        {
            if (!s_restoreLight.Contains(w)) { s_restoreLight.Add(w); }
        }

#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RestoreQueued += s_lit.Count;
#endif
        s_lit.Clear(); // 넘긴 창은 '걸린 창' 목록에서 뺀다(떼기 전까지 클래스는 그대로 남는다)
        StartRestore();
    }

    private static void StartRestore()
    {
        if (s_restoreRunning || (s_restoreLight.Count == 0 && s_restoreCache.Count == 0))
        {
            return; // 이미 돌고 있으면 목록에 더한 것을 이어서 뗀다
        }

        var anchor = s_restoreLight.Count > 0 ? s_restoreLight[0] : s_restoreCache[0];
        if (Avalonia.Controls.TopLevel.GetTopLevel(anchor) is not { } top)
        {
            RestoreStep(int.MaxValue); // 프레임을 얻을 수 없으면 한꺼번에(지금 동작과 같은 결과)
            return;
        }

        s_restoreRunning = true;
        var gen = s_restoreGen;
        top.RequestAnimationFrame(_ => RestoreFrame(top, gen));
    }

    private static void RestoreFrame(Avalonia.Controls.TopLevel top, int gen)
    {
        if (gen != s_restoreGen)
        {
            return; // FlushRestore 가 이미 다 뗐다(s_restoreRunning 도 풀었다)
        }

#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RestoreFrames++;
        xkxk.frameworks.ui.shell.Diagnostics.MdiDragFrameDiag.NoteRestoreStep();
#endif
        if (RestoreStep(xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RestorePerFrame))
        {
            s_restoreRunning = false;
            return;
        }

        top.RequestAnimationFrame(_ => RestoreFrame(top, gen));
    }

    // 앞에서 count 개 창을 뗀다(캐시 먼저, 같은 창의 가벼운 크롬 다음 — 한 창이 한 프레임에 끝나게). 다 떼면 true. 옵션이 그새 0 이 됐으면 남은 것을 한꺼번에 뗀다
    private static bool RestoreStep(int count)
    {
        if (count <= 0) { count = int.MaxValue; }
        for (var i = 0; i < count && (s_restoreLight.Count > 0 || s_restoreCache.Count > 0); i++)
        {
            var w = s_restoreLight.Count > 0 ? s_restoreLight[0] : s_restoreCache[0];
            RestoreOne(w);
        }

        return s_restoreLight.Count == 0 && s_restoreCache.Count == 0;
    }

    private static void RestoreOne(MdiDocumentWindow w)
    {
        if (s_restoreCache.Remove(w) && w.CacheMode is BitmapCache)
        {
            w.CacheMode = null;
        }

        if (s_restoreLight.Remove(w))
        {
            w.Classes.Remove(SiblingClass);
            w.Classes.Remove(MdiMoveLightener.SquareClass);
            MdiMoveLightener.SyncShadowOff(w);
        }
    }

    /// <summary>남은 복원을 그 자리에서 모두 마친다(새 이동 시작·옵션 변경). 돌던 프레임 콜백은 세대 번호로 취소한다.</summary>
    private static void FlushRestore()
    {
        if (s_restoreLight.Count == 0 && s_restoreCache.Count == 0)
        {
            return;
        }

#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RestoreFlushed++;
#endif
        s_restoreGen++;
        s_restoreRunning = false;
        RestoreStep(int.MaxValue);
    }

    // 창 하나(닫힘·분리·이동 시작)가 복원 대기 중이면 바로 뗀다
    private static void ReleaseRestoring(MdiDocumentWindow w)
    {
        if (s_restoreLight.Count > 0 || s_restoreCache.Count > 0)
        {
            RestoreOne(w);
        }
    }
}
