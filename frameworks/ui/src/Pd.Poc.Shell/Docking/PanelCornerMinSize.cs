// 패널 끝 확장 최소 길이(요건 3) — 모서리를 가져간 패널 때문에 줄어드는 패널의 길이가 하한 아래로 가지 않게 바깥 패널 두께를 깎는 순수 계산
using System.Collections.Generic;
using System.Linq;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Dock 의 분할 패널에는 하한이 없다(실측: 창 높이 420·하단 몫 0.9 에서 좌 패널 28dip). 그래서 트리를 옮기기 전에 비율로 막는다.
/// 패널 길이는 교차 축에서 바깥 패널들이 남긴 길이다. 부족하면 그 바깥 패널들의 두께를 같은 비율로 줄인다.
/// </summary>
internal static class PanelCornerMinSize
{
    /// <summary>패널 길이 하한(dip). 머리줄과 내용 한 줄이 들어가는 크기다.</summary>
    public const double MinLength = 48;

    /// <summary>분할선 두께(dip, 실측). 단마다 하나씩 길이에서 빠진다.</summary>
    private const double SplitterThickness = 4;

    /// <summary>
    /// order 는 바깥부터의 패널(Horizontal = 좌우 변, Thickness = 창 전체에 대한 두께 몫)이다. 고친 두께 몫을 같은 순서로 돌려준다. 고칠 일이 없으면 값이 그대로다.
    /// 창이 하한의 두 배보다 작으면 두 패널을 함께 지킬 수 없어 고치지 않는다(배치 전 크기 0 포함).
    /// </summary>
    internal static IReadOnlyList<double> Limit(IReadOnlyList<(bool Horizontal, double Thickness)> order, double width, double height)
    {
        var result = order.Select(p => p.Thickness).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var cross = order[i].Horizontal ? height : width;
            if (cross <= MinLength * 2)
            {
                continue;
            }

            var outers = Enumerable.Range(0, i).Where(j => order[j].Horizontal != order[i].Horizontal).ToList();
            var used = outers.Sum(j => result[j]);
            var allowed = 1 - (MinLength + (outers.Count * SplitterThickness)) / cross;
            if (used > allowed && used > 0)
            {
                foreach (var j in outers)
                {
                    result[j] *= allowed / used;
                }
            }
        }

        return result;
    }
}
