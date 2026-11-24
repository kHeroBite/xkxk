// 도구 패널 1개의 탭 구성 저장 모양(UI-14·UI-20) — 옛 PocDockFactory.Panels.cs 안의 공개 클래스를 그대로 옮김(JSON 키 불변)
using System.Collections.Generic;

namespace Pd.Client.Data.Layout;

/// <summary>도구 패널 1개의 탭 구성(탭 순서 = 도구 Id 순서).</summary>
public sealed class PanelTabsSnapshot
{
    public string PanelId { get; set; } = "";

    public List<string> ToolIds { get; set; } = new();

    public string? ActiveToolId { get; set; }

    /// <summary>분할된 영역 이름 Left|Bottom|Right(UI-20). null 이면 분할되지 않은 패널(옛 저장본 포함).</summary>
    public string? Area { get; set; }

    /// <summary>영역 안 화면 순서(0부터, UI-20).</summary>
    public int? Index { get; set; }

    /// <summary>영역 안 비율(UI-20). null 이면 현재 유지.</summary>
    public double? Proportion { get; set; }
}
