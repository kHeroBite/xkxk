// 패널 영역 목록과 저장·원격 이름 — 저장본과 원격 명령은 옛 이름(Left·Bottom·Right)을 그대로 쓴다. 영역 이름은 위치를 뜻하지 않는다
using System;
using System.Collections.Generic;

namespace Pd.Poc.Shell.Docking;

/// <summary>영역 순회 순서와 바깥에 보이는 이름. 옛 저장본(Version 3)의 Area 값을 그대로 읽고, 새 저장본도 같은 값을 쓴다.</summary>
public static class PanelAreaNames
{
    /// <summary>영역 순회 순서.</summary>
    public static IReadOnlyList<PanelArea> All { get; } = new[] { PanelArea.Explorer, PanelArea.Log, PanelArea.Notification, PanelArea.Extra };

    /// <summary>저장·원격 이름.</summary>
    public static string Stored(PanelArea area) => area switch
    {
        PanelArea.Explorer => "Left",
        PanelArea.Log => "Bottom",
        PanelArea.Notification => "Right",
        _ => "Extra",
    };

    /// <summary>저장·원격 이름에서 영역을 찾는다.</summary>
    public static bool TryParse(string? stored, out PanelArea area)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Stored(candidate), stored, StringComparison.OrdinalIgnoreCase))
            {
                area = candidate;
                return true;
            }
        }

        area = default;
        return false;
    }
}
