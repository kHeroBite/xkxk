// 영역 안 패널 1개의 조회 결과(UI-20 원격 덤프·판정용) — 옛 PocDockFactory.SplitLayout.cs 안의 공개 레코드를 그대로 옮김
using System.Collections.Generic;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>영역 안 패널 1개의 조회 결과(원격 덤프·판정용).</summary>
public sealed record SplitPaneInfo(string PaneId, string? TabsId, PanelArea Area, int Index, double Proportion, bool IsHome,
    IReadOnlyList<string> Tabs, string? Active, IDock Pane);
