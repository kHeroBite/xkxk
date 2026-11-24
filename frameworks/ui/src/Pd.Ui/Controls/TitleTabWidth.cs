// 제목 탭 폭 순수 계산 — 자연 폭을 최소~최대로 자르고, 가용 폭을 넘으면 모든 탭에 같은 상한을 적용한다(UI 비의존)
using System;
using System.Collections.Generic;

namespace Pd.Ui.Controls;

/// <summary>
/// 규칙 정본은 plan/09_화면설계/01_화면설계_공통원칙.md §2-1 이다. 폭은 clamp(자연 폭, min, max) 에서 시작하고,
/// 합계가 가용 폭을 넘으면 모든 탭에 상한 max(min, 가용 폭 / 탭 수) 를 적용한다(짧은 제목은 자연 폭 유지).
/// min 에서도 넘치면 overflow 가 true 이고 스크롤은 호출하는 쪽이 맡는다.
/// </summary>
public static class TitleTabWidth
{
    public const double DefaultMin = 72;
    public const double DefaultMax = 240;

    /// <param name="natural">탭별 자연 폭(DIP). NaN 은 최소 폭으로 본다.</param>
    /// <param name="available">탭이 쓸 수 있는 가용 폭(DIP). 무한대면 제한이 없고, 음수·NaN 은 0 으로 본다.</param>
    /// <param name="min">탭 최소 폭(DIP).</param>
    /// <param name="max">탭 최대 폭(DIP). min 보다 작으면 min 을 쓴다.</param>
    /// <returns>탭별 폭(입력과 같은 순서)과 최소 폭으로도 가용 폭을 넘는지 여부.</returns>
    public static (double[] Widths, bool Overflow) Compute(IReadOnlyList<double> natural, double available, double min = DefaultMin, double max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(natural);
        max = Math.Max(min, max);
        var widths = new double[natural.Count];
        var total = 0d;
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = double.IsNaN(natural[i]) ? min : Math.Clamp(natural[i], min, max);
            total += widths[i];
        }

        if (widths.Length == 0 || total <= available)
        {
            return (widths, false);
        }

        var room = double.IsNaN(available) ? 0 : Math.Max(0, available);
        var cap = Math.Max(min, room / widths.Length);
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Min(widths[i], cap);
        }

        return (widths, min * widths.Length > room);
    }
}
