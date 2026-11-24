// 창 자동 복구 계산(요건 5) — 제목줄이 닿지 않는 창을 찾아 기본 위치를 정하는 순수 함수와 연속 이벤트를 1회로 합치는 디바운스(화면·창 조작 없음)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Busy;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>복구 판정 대상 창 하나. Bounds 는 해당 호스트 좌표(메인·SDI = 물리 px, MDI = 배치 패널 DIP).</summary>
public readonly record struct RecoveryWindow(Rect Bounds, bool IsLocked = false, bool IsMinimized = false, bool IsMaximized = false, MdiGrabZone? Grab = null);

/// <summary>메인·SDI 복구 계획. Sdi 의 i번째는 입력 i번째 창의 새 좌상단이고, 옮기지 않는 창은 null.</summary>
public sealed record RecoveryPlan(bool MainHidden, IReadOnlyList<Point?> Sdi);

/// <summary>
/// 메인·SDI 의 '안 보임' 기준은 MDI-12 와 같다: 창 위쪽 띠(높이 32)가 어떤 영역과도 가로 120·세로 16 이상 겹치지 않으면 끌 손잡이가 없는 창이다.
/// MDI 문서 창은 끌기 한계와 같은 구간을 재는 MdiDragLimit.IsGrabbable 로 판정한다.
/// 복구 위치는 가까운 곳으로 당기는 clamp 가 아니라 기본 위치다. 메인은 ResetMain, SDI 는 SdiResetLayout, MDI 는 NextMdiBounds 규칙을 쓴다.
/// 위치 고정(IsLocked) 창도 안 보이면 옮긴다(접근 불가가 더 나쁘다). 고정 상태 자체는 호출자가 그대로 둔다.
/// </summary>
public static class PlacementRecovery
{
    /// <summary>제목줄로 보는 창 위쪽 띠 높이.</summary>
    public const double TitleStrip = 32;

    /// <summary>끌 수 있다고 보는 최소 겹침 폭.</summary>
    public const double MinVisibleWidth = 120;

    /// <summary>끌 수 있다고 보는 최소 겹침 높이.</summary>
    public const double MinVisibleHeight = 16;

    /// <summary>진단 전용 스위치: 비전경 시험(--bg-pos)에서도 복구·재결합 정책을 켠다. 제품 빌드에서는 아무도 켜지 않는다.</summary>
    public static bool ForceInBackground { get; set; }

    /// <summary>복구를 끌 조건. 비전경 시험은 창을 일부러 화면 밖에 두므로 켜면 오탐이다.</summary>
    public static bool IsSuppressed(bool backgroundMode) => backgroundMode && !ForceInBackground;

    /// <summary>창 위쪽 띠가 areas 중 하나와 최소 크기 이상 겹치는가. areas 가 비면 판단할 근거가 없어 true.</summary>
    public static bool IsTitleReachable(Rect bounds, IReadOnlyList<Rect> areas)
    {
        if (areas.Count == 0)
        {
            return true;
        }

        var strip = new Rect(bounds.X, bounds.Y, bounds.Width, Math.Min(TitleStrip, bounds.Height));
        var needW = Math.Min(MinVisibleWidth, strip.Width);
        var needH = Math.Min(MinVisibleHeight, strip.Height);
        foreach (var area in areas)
        {
            var hit = area.Intersect(strip);
            if (hit.Width > 0 && hit.Height > 0 && hit.Width >= needW && hit.Height >= needH)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 메인·SDI 복구 계획을 계산한다. workAreas 는 주 모니터가 먼저다. 비어 있으면(모니터 정보 없음·전환 중) 아무것도 옮기지 않는다.
    /// 최대화 창은 메인·SDI 모두 대상에서 빠진다(OS 가 소속 모니터에 두고, 위쪽 작업 표시줄이면 제목 띠가 작업 영역 밖이어도 보이는 창이다). 최소화 창도 제외한다.
    /// </summary>
    public static RecoveryPlan Evaluate(IReadOnlyList<Rect> workAreas, RecoveryWindow? main, IReadOnlyList<RecoveryWindow> sdi, Point sdiDefault)
    {
        var none = new Point?[sdi.Count];
        if (workAreas.Count == 0)
        {
            return new RecoveryPlan(false, none);
        }

        var mainHidden = main is { IsMinimized: false, IsMaximized: false } m && !IsTitleReachable(m.Bounds, workAreas);
        var hidden = sdi.Select(w => !w.IsMinimized && !w.IsMaximized && !IsTitleReachable(w.Bounds, workAreas)).ToArray();
        if (!hidden.Any(h => h))
        {
            return new RecoveryPlan(mainHidden, none);
        }

        // 보이는 창은 Locked 로 넘겨 자리를 지키며 겹침 기준으로만 쓰고, 안 보이는 창은 IsLocked 와 상관없이 옮긴다.
        var inputs = sdi.Select((w, i) => new SdiResetWindow(w.Bounds.TopLeft, w.Bounds.Size, !hidden[i])).ToArray();
        return new RecoveryPlan(mainHidden, SdiResetLayout.Compute(workAreas, sdiDefault, inputs));
    }

    /// <summary>
    /// MDI 문서 창 복구 위치. 결과의 i번째가 null 이면 그대로 둔다. arranged 는 판정할 사각형과 끌 수 있는 구간, bounds 는 저장값(계단 충돌 기준)이다.
    /// 판정은 끌기 한계와 같은 구간을 재는 MdiDragLimit.IsGrabbable 이다. 일부러 밀어 둔 창은 잡을 곳이 남아 있는 한 옮기지 않는다.
    /// next 는 PocDockFactory.NextMdiBounds 규칙을 받는다. 크기는 유지하고, 계단 자리도 안 보이면 영역 왼쪽 위로 둔다.
    /// </summary>
    public static IReadOnlyList<DockRect?> EvaluateMdi(Size area, IReadOnlyList<RecoveryWindow> arranged, IReadOnlyList<DockRect> bounds, Func<IEnumerable<DockRect>, DockRect> next)
    {
        var result = new DockRect?[arranged.Count];
        if (area.Width <= 0 || area.Height <= 0)
        {
            return result;
        }

        var taken = new List<DockRect>(bounds);
        for (var i = 0; i < arranged.Count; i++)
        {
            var w = arranged[i];
            var grab = w.Grab ?? MdiGrabZone.Whole;
            if (w.IsMinimized || w.IsMaximized || w.Bounds.Width <= 0 || MdiDragLimit.IsGrabbable(w.Bounds, area, grab))
            {
                continue;
            }

            taken.Remove(bounds[i]); // 자기 옛 자리는 비어 있는 자리로 본다
            var placed = MdiDefaultSlot(area, grab, bounds[i], taken, next);
            taken.Add(placed);
            result[i] = placed;
        }

        return result;
    }

    /// <summary>MDI 문서 창 하나의 기본 위치(복구·창 메뉴 공용). next 규칙의 첫 빈 칸이고, 그 칸이 잡을 수 없으면 영역 왼쪽 위다. 크기는 size 그대로다.</summary>
    public static DockRect MdiDefaultSlot(Size area, MdiGrabZone grab, DockRect size, IEnumerable<DockRect> taken, Func<IEnumerable<DockRect>, DockRect> next)
    {
        var slot = next(taken);
        return MdiDragLimit.IsGrabbable(new Rect(slot.X, slot.Y, size.Width, size.Height), area, grab)
            ? new DockRect(slot.X, slot.Y, size.Width, size.Height)
            : new DockRect(0, 0, size.Width, size.Height);
    }
}

/// <summary>뒤따름 디바운스: 마지막 호출 뒤 delayMs 동안 조용하면 run 을 1회 부른다. 대기가 없을 때는 타이머가 없다. 시험은 가짜 시계를 넣는다.</summary>
public sealed class RecoveryDebounce
{
    private readonly int _delayMs;
    private readonly IBusyClock _clock;
    private readonly Action _run;
    private IDisposable? _pending;

    public RecoveryDebounce(int delayMs, IBusyClock clock, Action run)
    {
        _delayMs = delayMs;
        _clock = clock;
        _run = run;
    }

    /// <summary>이벤트 1건을 알린다. 대기 중인 평가를 뒤로 민다.</summary>
    public void Trigger()
    {
        _pending?.Dispose();
        _pending = _clock.Schedule(_delayMs, () =>
        {
            _pending = null;
            _run();
        });
    }
}
