// 자석(창 흡착) 계산 — 이동·크기 조절 중인 사각형의 가장자리를 범위 안의 기준선(영역 경계·다른 창 가장자리)에 붙인다(순수 계산, UI 의존 없음)
using System;
using System.Collections.Generic;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 기준선 규칙(UI-12 확정 사양): 영역 경계 4변 + 다른 창의 네 가장자리(간격 0). 다른 창의 세로 가장자리는 두 창이 세로로
/// 겹치거나 범위 안으로 가까울 때만 기준이 된다(가로도 같음) — 멀리 떨어진 창의 연장선에 붙지 않게 한다.
/// 할당 없이 목록을 한 번씩 훑으므로 창 20개 기준 프레임당 수 마이크로초다.
/// </summary>
public static class MagnetSnap
{
    /// <summary>이동: 왼쪽·오른쪽 가장자리 중 가장 가까운 기준선 하나로 가로 보정, 위·아래도 같은 방식으로 세로 보정. 크기는 그대로.</summary>
    public static Rect SnapMove(Rect r, double range, Size area, IReadOnlyList<Rect> others)
    {
        if (range <= 0)
        {
            return r;
        }

        var dx = BestOffset(r.Left, r.Right, r.Top, r.Bottom, range, area.Width, others, vertical: true);
        var dy = BestOffset(r.Top, r.Bottom, r.Left, r.Right, range, area.Height, others, vertical: false);
        return new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
    }

    /// <summary>크기 조절: 끌고 있는 가장자리만 기준선에 붙인다(반대쪽 가장자리는 고정 — 크기가 바뀐다).</summary>
    public static Rect SnapResize(Rect r, double range, Size area, IReadOnlyList<Rect> others, bool left, bool top, bool right, bool bottom)
    {
        if (range <= 0)
        {
            return r;
        }

        double l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
        if (left) { l = Nearest(l, t, b, range, area.Width, others, vertical: true); }
        if (right) { rr = Nearest(rr, t, b, range, area.Width, others, vertical: true); }
        if (top) { t = Nearest(t, l, rr, range, area.Height, others, vertical: false); }
        if (bottom) { b = Nearest(b, l, rr, range, area.Height, others, vertical: false); }
        return new Rect(l, t, Math.Max(0, rr - l), Math.Max(0, b - t));
    }

    /// <summary>
    /// 두 가장자리(lo·hi) 중 하나를 기준선에 맞추는 데 필요한 최소 이동량(범위 밖이면 0).
    /// vertical=true 면 세로 기준선(x 값), spanLo..spanHi 는 이 창의 세로 범위(겹침 판정용).
    /// </summary>
    private static double BestOffset(double lo, double hi, double spanLo, double spanHi, double range, double areaLimit,
        IReadOnlyList<Rect> others, bool vertical)
    {
        var best = range + 1;
        Consider(ref best, 0 - lo);
        Consider(ref best, areaLimit - hi);
        for (var i = 0; i < others.Count; i++)
        {
            var o = others[i];
            if (!Overlaps(o, spanLo, spanHi, range, vertical))
            {
                continue;
            }

            var a = vertical ? o.Left : o.Top;
            var z = vertical ? o.Right : o.Bottom;
            Consider(ref best, a - lo);
            Consider(ref best, z - lo);
            Consider(ref best, a - hi);
            Consider(ref best, z - hi);
        }

        return Math.Abs(best) <= range ? best : 0;
    }

    /// <summary>가장자리 하나(edge)를 가장 가까운 기준선으로 옮긴 값. 범위 밖이면 그대로.</summary>
    private static double Nearest(double edge, double spanLo, double spanHi, double range, double areaLimit,
        IReadOnlyList<Rect> others, bool vertical)
    {
        var best = range + 1;
        Consider(ref best, 0 - edge);
        Consider(ref best, areaLimit - edge);
        for (var i = 0; i < others.Count; i++)
        {
            var o = others[i];
            if (!Overlaps(o, spanLo, spanHi, range, vertical))
            {
                continue;
            }

            Consider(ref best, (vertical ? o.Left : o.Top) - edge);
            Consider(ref best, (vertical ? o.Right : o.Bottom) - edge);
        }

        return Math.Abs(best) <= range ? edge + best : edge;
    }

    private static bool Overlaps(Rect o, double spanLo, double spanHi, double range, bool vertical) => vertical
        ? spanLo < o.Bottom + range && spanHi > o.Top - range
        : spanLo < o.Right + range && spanHi > o.Left - range;

    private static void Consider(ref double best, double offset)
    {
        if (Math.Abs(offset) < Math.Abs(best))
        {
            best = offset;
        }
    }
}
