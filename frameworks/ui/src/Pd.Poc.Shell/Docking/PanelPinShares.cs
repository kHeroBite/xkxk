// 측면 패널 핀 토글 전후 비율 보존(PW2) — 핀 켜기 직전 비율을 기억했다가 핀이 풀리면 되돌린다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>핀 켜기 직전의 바깥 패널·비율·다른 칸 비율(PW2).</summary>
internal sealed record PinShare(IDock Pane, double Proportion, List<(IDockable Dockable, double Proportion)> Siblings);

/// <summary>
/// 측면 패널 탭 영역의 핀 토글 전후 비율 보존(PW2). Dock 의 핀 켜기는 빈 바깥 패널 비율을 0 으로 만들고,
/// 핀을 풀어도 되돌리지 않는다(웹 i2000rc followup_w2 실측 0.18→0, Fold5 폭 109→0).
/// 키는 안쪽 탭 영역이다. 레이아웃을 새로 만들면 옛 키는 다시 매칭되지 않는다(동작 영향 없음).
/// </summary>
internal sealed class PanelPinShares
{
    private readonly Dictionary<IDock, PinShare> _shares = new();

    /// <summary>핀 켜기 직전에 기억한 바깥 패널 비율. 기억이 없으면 null.</summary>
    public double? SavedProportion(IDock tabs) => _shares.TryGetValue(tabs, out var s) ? s.Proportion : null;

    /// <summary>핀 켜기 직전에 부른다. 바깥 패널이 분할 안에 있지 않으면 기억하지 않는다.</summary>
    public void Remember(IDock tabs)
    {
        if (tabs.Owner is not IDock pane || pane.Owner is not IDock owner || owner.VisibleDockables is not { } list)
        {
            return;
        }

        var siblings = list.Where(d => !ReferenceEquals(d, pane) && d is not ISplitter)
            .Select(d => (Dockable: d, Proportion: d.Proportion)).ToList();
        _shares[tabs] = new PinShare(pane, pane.Proportion, siblings);
    }

    /// <summary>핀이 걸리지 않았을 때 기억을 버린다.</summary>
    public void Forget(IDock tabs) => _shares.Remove(tabs);

    /// <summary>
    /// 핀이 풀린 뒤 부른다. 탭 영역이 원래 바깥 패널로 돌아왔을 때만 되돌리고, 되돌린 기록을 돌려준다(아니면 null).
    /// 패널 비율은 저장값이 (0,1) 안일 때만 되돌린다. 다른 칸은 자동 몫(NaN)도 그대로 되돌린다(RestorePanel 과 같은 규칙 — CenterPane=1 잔존 방지).
    /// </summary>
    public PinShare? Restore(IDock tabs)
    {
        if (!_shares.Remove(tabs, out var s) || !ReferenceEquals(tabs.Owner, s.Pane)
            || s.Pane.Owner is not IDock owner || owner.VisibleDockables is not { } list)
        {
            return null;
        }

        if (s.Proportion is > 0 and < 1)
        {
            PocDockFactory.SetShare(s.Pane, s.Proportion);
        }

        foreach (var (sibling, saved) in s.Siblings)
        {
            if (list.Contains(sibling))
            {
                PocDockFactory.SetShare(sibling, saved);
            }
        }

        return s;
    }
}
