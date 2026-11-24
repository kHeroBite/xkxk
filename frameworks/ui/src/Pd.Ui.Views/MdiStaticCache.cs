// MDI 창 하나가 움직이는 동안 같은 MDI 영역의 움직이지 않는 다른 창에 비트맵 캐시를 걸고 떼는 적용부(옵션 MdiMoveLight.StaticCache, 판정은 MdiMoveLightener)
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Ui.Views;

/// <summary>
/// 끄는 창이 아래 창 위를 지나가면 화면 다시 그리기 영역(옛 위치+새 위치)에 든 아래 창들이 프레임마다 다시 그려진다(웹 W4 c9 실측: 같은 gantt 를 혼자 끌면 rAF 합 73~101ms/회, 아래 창 4개 위면 198~229ms/회).
/// Avalonia 12.1.4 는 BitmapCache 가 걸린 요소의 내용이 바뀌지 않으면 하위 트리를 다시 그리지 않고 저장 비트맵만 그린다(ServerCompositionVisualCache.Draw, odev-10 디컴파일 확인).
/// 그래서 이동이 시작되면(첫 위치 변화) 움직이지 않는 다른 보통 상태 창 전체(그림자 포함 템플릿)에 캐시를 걸고, 이동이 모두 끝나면 뗀다.
/// 다른 곳이 이미 건 CacheMode 는 건드리지 않는다. 규칙 정본 plan/09_화면설계/01_화면설계_공통원칙.md §4.2(MdiMoveLightStaticCache).
/// </summary>
internal static class MdiStaticCache
{
    // 이 클래스가 캐시를 건 창(원복 때 이 창들만 되돌린다)
    private static readonly HashSet<MdiDocumentWindow> s_cached = new();

    /// <summary>moving 이 움직이기 시작했다. 같은 MDI 영역의 다른 정지 창에 캐시를 건다. moving 자신은 캐시에서 뺀다(전체 이동처럼 여러 창이 함께 움직이는 경우).</summary>
    public static void OnMoveStarted(MdiDocumentWindow moving, System.Func<MdiDocumentWindow, bool> isMoving)
    {
        Release(moving);
        var applied = 0;
        foreach (var w in Siblings(moving))
        {
            if (isMoving(w) || s_cached.Contains(w) || w.CacheMode is not null || w.DataContext is not IMdiDocument { MdiState: MdiWindowState.Normal } || !w.IsVisible)
            {
                continue;
            }

            w.CacheMode = new BitmapCache(); // 창마다 따로 만든다(캐시 객체는 요소 하나에만 붙는다)
            s_cached.Add(w);
            applied++;
        }

        Pd.Poc.Shell.Docking.MdiMoveLight.NoteStaticCache(applied, s_cached.Count); // 진단 카운터(원격 chrome.moveLight)
    }

    /// <summary>창 하나의 캐시를 뗀다(이 클래스가 건 것만).</summary>
    public static void Release(MdiDocumentWindow w)
    {
        if (s_cached.Remove(w))
        {
            if (w.CacheMode is BitmapCache)
            {
                w.CacheMode = null;
            }

            Pd.Poc.Shell.Docking.MdiMoveLight.NoteStaticCache(0, s_cached.Count);
        }
    }

    /// <summary>이동이 모두 끝났거나 옵션이 바뀌었다 — 건 캐시를 모두 뗀다.</summary>
    public static void ReleaseAll()
    {
        foreach (var w in s_cached)
        {
            if (w.CacheMode is BitmapCache)
            {
                w.CacheMode = null;
            }
        }

        s_cached.Clear();
        (s_earlyFor, s_early) = (null, null); // K7: 이동이 모두 끝나면 미리 계산한 목록도 버린다(다음 누름에서 다시 잰다)
        Pd.Poc.Shell.Docking.MdiMoveLight.NoteStaticCache(0, 0);
    }

    /// <summary>P2(MdiMoveLight.RestorePerFrame): 이동이 모두 끝났을 때 건 캐시를 바로 떼지 않고 into 로 넘긴다(떼기는 MdiStaticLight 가 프레임마다).
    /// 그 밖은 ReleaseAll 과 같다 — K7 미리 계산 목록을 버리고 카운터를 0 으로 둔다(넘긴 창은 이 클래스 관리 밖이다).</summary>
    internal static void HandOffAll(List<MdiDocumentWindow> into)
    {
        into.AddRange(s_cached);
        s_cached.Clear();
        (s_earlyFor, s_early) = (null, null);
        Pd.Poc.Shell.Docking.MdiMoveLight.NoteStaticCache(0, 0);
    }

    /// <summary>같은 MDI 영역의 다른 문서 창. moving 의 조상을 위로 올라가며 다른 MdiDocumentWindow 를 처음 품은 조상을 그 영역으로 본다(MdiStaticLight 도 같은 판정을 쓴다).
    /// 찾기는 MDI 배치 패널(MdiLayoutPanel)에서 멈추고, 그 패널에 moving 하나뿐이면 훑지 않는다(01a A29 — 같은 MDI 영역 정의 그대로, 2026-11-15 기본 적용).
    /// K7(옵션 MdiMoveLight.StaticLightEarly, 01a): 누를 때 미리 계산한 목록이 있으면 그것을 쓴다(붙지 않은 창은 거름).</summary>
    internal static List<MdiDocumentWindow> Siblings(MdiDocumentWindow moving)
    {
        if (Pd.Poc.Shell.Docking.MdiMoveLight.StaticLightEarly && ReferenceEquals(s_earlyFor, moving) && s_early is { } early)
        {
#if PD_DIAGNOSTICS
            Pd.Poc.Shell.Docking.MdiMoveLight.EarlyUsed++;
#endif
            return early.FindAll(w => TopLevel.GetTopLevel(w) is not null);
        }

        return Find(moving, bounded: true);
    }

    private static List<MdiDocumentWindow> Find(MdiDocumentWindow moving, bool bounded)
    {
        var result = new List<MdiDocumentWindow>();
        if (bounded && moving.FindAncestorOfType<MdiLayoutPanel>() is { Children.Count: <= 1 })
        {
            return result; // A29: 배치 패널 자식이 moving 을 담은 하나뿐 = 같은 영역 정지 창 0개(아래 훑기도 빈 목록) — 창 내용 트리를 단계마다 훑는 일을 건너뛴다
        }

        for (var anc = moving.GetVisualParent(); anc is not null && anc is not TopLevel; anc = anc.GetVisualParent())
        {
            foreach (var v in anc.GetVisualDescendants())
            {
                if (v is MdiDocumentWindow w && !ReferenceEquals(w, moving) && !IsInside(w, moving))
                {
                    result.Add(w);
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            if (bounded && anc is MdiLayoutPanel)
            {
#if PD_DIAGNOSTICS
                Pd.Poc.Shell.Docking.MdiMoveLight.SiblingsBounded++;
#endif
                return result; // 같은 MDI 영역 = 배치 패널 안. 그 위 조상은 다른 영역이다
            }
        }

        return result;
    }

    // K7: 누름 때(끌기 임계 전) 미리 계산한 정지 창 목록. 화면은 바꾸지 않는다 — 클래스·캐시는 지금처럼 첫 위치 변화 때 건다
    private static MdiDocumentWindow? s_earlyFor;
    private static List<MdiDocumentWindow>? s_early;

#pragma warning disable CA2255 // 등록 파일(테마)이 이 클래스 밖이라 어셈블리 적재 때 처리기를 붙인다(SdiTitleBar.TabJoin 과 같은 방식)
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void InstallEarly() =>
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressedEarly, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

    private static void OnPressedEarly(MdiDocumentWindow w, Avalonia.Input.PointerPressedEventArgs e)
    {
        (s_earlyFor, s_early) = (null, null);
        if (!Pd.Poc.Shell.Docking.MdiMoveLight.StaticLightEarly || !(Pd.Poc.Shell.Docking.MdiMoveLight.Siblings || Pd.Poc.Shell.Docking.MdiMoveLight.StaticCache)
            || w.DataContext is not IMdiDocument { MdiState: MdiWindowState.Normal } || !e.GetCurrentPoint(w).Properties.IsLeftButtonPressed
            || !IsTitlePress(e.Source as Visual, w)) // 제목줄 누름만(내용 클릭마다 계산하지 않는다)
        {
            return;
        }

        (s_earlyFor, s_early) = (w, Find(w, bounded: true));
#if PD_DIAGNOSTICS
        Pd.Poc.Shell.Docking.MdiMoveLight.EarlyComputed++;
#endif
    }

    /// <summary>누른 곳이 창 제목줄(PART_Header)인가 — 단추·Dock 끌기 손잡이 제외(Shell PanelJoinTargets.IsTitlePress 와 같은 규칙, 그쪽이 internal 이라 SdiTitleBar.TabJoin 처럼 같은 판정을 둔다).</summary>
    private static bool IsTitlePress(Visual? source, MdiDocumentWindow window)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, window); v = v.GetVisualParent())
        {
            if (v is Avalonia.Controls.Button || v is Control { Name: Pd.Ui.Controls.TemplateParts.DragHandle })
            {
                return false;
            }

            if (v is Control { Name: Pd.Ui.Controls.TemplateParts.Header })
            {
                return true;
            }
        }

        return false;
    }

    // 중첩(창 안의 창)은 영역 판정에서 뺀다 — 바깥 창이 움직이면 안쪽도 함께 움직인다
    private static bool IsInside(Visual inner, Visual outer)
    {
        for (var p = inner.GetVisualParent(); p is not null; p = p.GetVisualParent())
        {
            if (ReferenceEquals(p, outer)) { return true; }
        }

        return false;
    }
}
