// ⑦ 독립(SDI) 창 위치 초기화 공식 — 데스크톱 OS 창·웹 관리형 창이 함께 쓰는 순수 계산(입력 사각형·위치 → 새 위치, 화면·창 조작 없음)
using System;
using System.Collections.Generic;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>위치를 다시 정할 SDI 창 하나(호스트 좌표). Locked 면 옮기지 않고 다른 창의 겹침 기준으로만 쓴다.</summary>
public readonly record struct SdiResetWindow(Point Position, Size Size, bool Locked);

/// <summary>
/// 판정보고서 §3.5 ⑦ 기준(Mars 0000_메인.cs:6302~6324 동작):
/// ① 기본 위치에서 시작 → ② 이미 놓인 창(또는 위치 고정 창)과 좌상단이 같으면 (+30,+30) 을 반복(Mars util.Form.cs:1040 gap=30)
/// → ③ 어느 작업 영역과도 100×100 이상 겹치지 않으면 첫 작업 영역 가운데로 보정(Mars Fn창위치유효성검증_SDI, util.Form.cs:1152)
/// → ④ 크기는 바꾸지 않는다 → ⑤ 위치 고정 창은 제외.
/// Mars 와 다른 점: 보정한 위치도 다시 ②를 적용한다(두 창이 같은 가운데에 겹쳐 놓이지 않게 — 수용 기준 '서로 겹치지 않음').
/// </summary>
public static class SdiResetLayout
{
    /// <summary>겹칠 때 비켜 놓는 간격(Mars gap=30).</summary>
    public const double Gap = 30;

    /// <summary>화면 안으로 보는 최소 보이는 폭·높이(Mars 100px).</summary>
    public const double MinVisible = 100;

    /// <summary>같은 좌상단으로 보는 허용 오차(배율 반올림 차이 흡수).</summary>
    private const double SameTolerance = 0.5;

    /// <summary>비켜 놓기 반복 상한(작업 영역보다 많은 창이 같은 칸을 차지해도 끝나도록).</summary>
    private const int MaxShifts = 1000;

    /// <summary>
    /// 각 창의 새 위치를 계산한다. 결과의 i번째는 windows[i] 의 새 좌상단이고, 위치 고정 창은 null(옮기지 않음).
    /// workAreas 는 비어 있으면 화면 밖 보정을 하지 않는다. 첫 항목이 보정 기준(주 모니터·창 레이어)이다.
    /// </summary>
    public static IReadOnlyList<Point?> Compute(IReadOnlyList<Rect> workAreas, Point defaultPosition, IReadOnlyList<SdiResetWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        ArgumentNullException.ThrowIfNull(windows);

        var occupied = new List<Point>();
        foreach (var w in windows)
        {
            if (w.Locked)
            {
                occupied.Add(w.Position);
            }
        }

        var result = new Point?[windows.Count];
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            if (w.Locked)
            {
                continue;
            }

            var p = AvoidSame(defaultPosition, occupied);
            if (workAreas.Count > 0 && !IsVisible(new Rect(p, w.Size), workAreas))
            {
                p = AvoidSame(CenterIn(workAreas[0], w.Size), occupied);
            }

            occupied.Add(p);
            result[i] = p;
        }

        return result;
    }

    /// <summary>어느 작업 영역과든 MinVisible×MinVisible 이상 겹치면 화면 안이다(창이 그보다 작으면 창 크기만큼).</summary>
    public static bool IsVisible(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        var needW = Math.Min(MinVisible, bounds.Width);
        var needH = Math.Min(MinVisible, bounds.Height);
        foreach (var area in workAreas)
        {
            var hit = area.Intersect(bounds);
            if (hit.Width >= needW && hit.Height >= needH && hit.Width > 0 && hit.Height > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 웹 관리형 창 전용 레이어 맞춤(⑦-6, lead 결정 10-29). Dock ClassicMdiLayoutManager.ClampBounds 는 레이어보다 큰 창을 레이어 크기로 줄이고
    /// 0 으로 당겨 큰 창끼리 정확히 겹친다. 그래서 표시 전에 축마다 직접 맞추고, 이 값을 모델(MdiBounds)에도 써서 표시와 일치시킨다.
    /// 축 규칙(x·w 기준, y·h 동일): w ≤ W−xf → x=xf / W−xf &lt; w ≤ W → x=W−w / w &gt; W → w'=W−xf, x=xf(계단 간격을 남겨 좌상단이 서로 다르게).
    /// position 은 레이어 좌표(원점 0,0)이고 layer 가 비어 있으면 그대로 돌려준다. 데스크톱 OS 창 경로는 쓰지 않는다.
    /// </summary>
    public static Rect FitInLayer(Point position, Size size, Size layer)
    {
        var (x, w) = FitAxis(position.X, size.Width, layer.Width);
        var (y, h) = FitAxis(position.Y, size.Height, layer.Height);
        return new Rect(x, y, w, h);
    }

    private static (double Start, double Length) FitAxis(double start, double length, double extent)
    {
        if (extent <= 0)
        {
            return (start, length);
        }

        if (length > extent)
        {
            // 시작점이 레이어 밖이면 최소 보이는 폭을 남기는 범위로 당긴다(줄인 크기가 0 이하가 되지 않게)
            var s = Math.Clamp(start, 0, Math.Max(0, extent - MinVisible));
            return (s, extent - s);
        }

        if (length > extent - start)
        {
            return (extent - length, length);
        }

        return (Math.Max(0, start), length); // 음수 시작은 Dock 표시와 같게 0
    }

    private static Point AvoidSame(Point start, List<Point> occupied)
    {
        var p = start;
        for (var n = 0; n < MaxShifts && occupied.Exists(o => Same(o, p)); n++)
        {
            p = new Point(p.X + Gap, p.Y + Gap);
        }

        return p;
    }

    private static Point CenterIn(Rect area, Size size) =>
        new(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2);

    private static bool Same(Point a, Point b) =>
        Math.Abs(a.X - b.X) <= SameTolerance && Math.Abs(a.Y - b.Y) <= SameTolerance;
}
