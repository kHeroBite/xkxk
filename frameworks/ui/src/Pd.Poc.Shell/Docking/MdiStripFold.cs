// MDI 창의 기능 버튼 줄(PdCaptionStripHost)을 접거나 펴는 뷰 쪽 적용기 — 접기 판정은 MdiDragLimit 순수 계산에 맡기고 여기서는 IsVisible 만 바꾼다
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 왼쪽으로 밀린 창에서 기능 버튼 줄이 잡을 곳을 가리면 그 줄을 접는다(MdiDragLimit.ShouldFoldStrip). 끌어내면 다시 표시한다.
/// 창을 SDI 로 분리하면 Restore 로 원래대로 되돌린다. 접은 줄만 되돌리므로 원래 숨겨져 있던 줄(도구 창)은 건드리지 않는다.
/// </summary>
internal static class MdiStripFold
{
    /// <summary>템플릿(MdiDocumentWindowTheme.axaml)의 기능 버튼 줄 이름.</summary>
    internal const string StripHostName = "PdCaptionStripHost";

    /// <summary>구간이 음수일 때 같은 창 폭에서 부모 재배치를 요청하는 최대 횟수(D-24).</summary>
    private const int MaxRetry = 3;

    private static readonly ConditionalWeakTable<Control, FoldState> States = new();

    private sealed class FoldState
    {
        internal Control? Host;
        internal bool Folded;
        internal bool Retry; // 구간이 낡았을 때 보낸 재배치 요청이 대기 중인가
        internal int Templates; // Host 를 찾은 때의 템플릿 적용 횟수(MdiGrabZoneCache.TemplatesOf)
        internal int MissVersion = -1; // Host 를 못 찾은 구조 버전(MdiGrabZoneCache.StructureOf). -1 = 없음
        internal int MissSkips; // 같은 구조 버전에서 건너뛴 횟수
        internal double RetryWidth = -1; // 재시도를 건 창 폭(같은 폭에서는 MaxRetry 번까지만)
        internal int RetryCount;
        internal bool Settled; // K2: 직전 판정이 접힘을 바꾸지 않고 끝났다(LastRect·LastArea 와 같은 입력이면 다시 판정하지 않는다)
        internal Rect LastRect;
        internal Size LastArea;
    }

    /// <summary>이 창의 기능 버튼 줄이 지금 이 클래스에 의해 접혀 있는가.</summary>
    internal static bool IsFolded(Control window) => States.TryGetValue(window, out var s) && s.Folded;

    /// <summary>창 위치(rect)와 캔버스 크기(area)에 맞게 기능 버튼 줄을 접거나 편다. 구간은 MdiGrabZoneCache(MagnetMdiLayoutManager 와 같은 캐시)에서 얻는다.</summary>
    internal static void Apply(Control window, Rect rect, Size area)
    {
        var state = States.GetOrCreateValue(window);
        var host = state.Host;
        var templates = MdiGrabZoneCache.TemplatesOf(window);
        if (state.Settled && host is not null && state.Templates == templates && state.LastRect == rect && state.LastArea == area
            && MdiGrabZoneCache.IsFresh(window))
        {
            MdiGrabZoneDiag.Note(MdiGrabZoneDiag.FoldSkip);
            return; // K2(01a 2026-11-14): 같은 입력·같은 구간이면 결과도 같다 — 끌기 중 정지 창과, GetDragBounds 뒤 Arrange 의 같은 사각형 재판정을 건너뛴다
        }

        MdiGrabZoneDiag.Note(MdiGrabZoneDiag.FoldRun);
        state.Settled = false;
        var structure = MdiGrabZoneCache.StructureOf(window);
        if (host is null && state.MissVersion == structure && ++state.MissSkips < MdiGrabZoneCache.MissRetryEvery)
        {
            return; // 이 구조에서 이미 기능 버튼 줄이 없음을 확인했다 — 창 전체 시각 트리 순회를 반복하지 않는다(D-24)
        }

        if (host is null || state.Templates != templates)
        {
            // 템플릿이 다시 적용되면 옛 줄은 떨어져 나가고 새 줄은 보이는 상태로 만들어지므로 다시 찾고 접힘도 초기화한다.
            state.Templates = templates;
            state.Folded = false;
            host = state.Host = FindHost(window);
            state.MissSkips = 0;
            if (host is null)
            {
                state.MissVersion = structure;
                return;
            }

            state.MissVersion = -1;

            MdiGrabZoneCache.WatchStrip(window, host); // 줄 폭·머리줄 높이가 바뀌면 캐시 세대를 올린다
        }

        var zone = MdiGrabZoneCache.Get(window);
        if (zone.RightInset < 0)
        {
            // 최대화에서 복구한 직후처럼 안쪽 제목 칸이 아직 새 창 폭으로 다시 배치되기 전이면 구간이 창 밖으로 읽힌다(RightInset<0).
            // 이 값으로 판정하면 접어야 할 창을 못 접고 이후 Arrange 도 없으므로, 판정은 건너뛰고 배치가 끝난 뒤 부모에 한 번 더 Arrange 를 요청한다.
            var width = window.Bounds.Width;
            if (state.RetryWidth != width)
            {
                state.RetryWidth = width;
                state.RetryCount = 0; // 창 폭이 바뀌면 다시 허용
            }

            if (!state.Retry && state.RetryCount < MaxRetry)
            {
                state.Retry = true;
                state.RetryCount++; // 같은 폭에서 구간이 계속 음수면 재배치 요청을 MaxRetry 번으로 끝낸다(무한 재배치 방지, D-24)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { state.Retry = false; (window.GetVisualParent() as Avalonia.Layout.Layoutable)?.InvalidateArrange(); }, Avalonia.Threading.DispatcherPriority.Loaded);
            }

            return;
        }

        state.RetryWidth = -1; // 정상 구간을 읽었으면 다음 음수 때 다시 허용한다
        state.RetryCount = 0;
        var fold = MdiDragLimit.ShouldFoldStrip(rect, area, zone, state.Folded);
        if (fold == state.Folded)
        {
            state.Settled = true; // 바뀔 것이 없다 — 다음에 같은 입력이면 건너뛴다(접힘이 바뀐 직후는 Settled=false 라 다시 판정한다)
            state.LastRect = rect;
            state.LastArea = area;
            return;
        }

        if (fold && !host.IsVisible)
        {
            return; // 원래 숨겨진 줄(도구 창)은 건드리지 않는다
        }

        // 순서: ① 접기 → ② 캐시 무효화 → ③ 재읽기. 접힘이 바뀌면 제목 칸 둘째 열(줄 폭)이 달라지므로 보관값을 버린다.
        // ③ 은 여기서 바로 하지 않는다 — 지금은 안쪽 배치가 토글 전 그대로라, 펴기 직후 읽으면 둘째 열 0 을 기억해 버린다(MdiGrabZoneReader.StripWidth).
        // IsVisible 변경이 부모 재배치를 부르므로 다음 Arrange 의 판정(Get)이 배치가 끝난 값으로 다시 읽는다.
        state.Folded = fold;
        host.SetCurrentValue(Visual.IsVisibleProperty, !fold);
        MdiGrabZoneCache.Invalidate(window);
    }

    /// <summary>한 줄 호출용: 접기 대상(eligible, 예: 일반 MDI 창)이면 Apply, 아니면(최대화·분리·복구 등) Restore.</summary>
    internal static void Sync(Control window, Rect rect, Size area, bool eligible)
    {
        if (eligible)
        {
            Apply(window, rect, area);
        }
        else
        {
            Restore(window);
        }
    }

    /// <summary>접은 줄을 원래대로 되돌린다(SDI 분리·창 닫기 직전). 구간 캐시도 버린다(분리 뒤 창 안쪽 배치가 달라진다).</summary>
    internal static void Restore(Control window)
    {
        MdiGrabZoneCache.Invalidate(window);
        if (States.TryGetValue(window, out var s))
        {
            s.Settled = false;
        }

        if (s is { Folded: true })
        {
            s.Folded = false;
            s.Host?.SetCurrentValue(Visual.IsVisibleProperty, true);
        }
    }

    private static Control? FindHost(Control window)
    {
        foreach (var v in window.GetVisualDescendants())
        {
            if (v is Control { Name: StripHostName } c)
            {
                return c;
            }
        }

        return null;
    }
}
