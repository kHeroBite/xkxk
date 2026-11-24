// PocDockFactory 와 패널·분할 서비스들이 함께 쓰는 도킹 트리 상태(영역별 기본 패널·탭 영역·가장자리, 분할 패널·영역·접힘·숨긴 도구·핀 몫)
using System.Collections.Generic;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// DockLayoutBuilder 가 CreateLayout 때 트리 참조를 채우고, 서비스는 생성자로 이것을 받는다. 한 서비스만 쓰는 상태는 그 서비스가 가진다.
/// 영역은 모두 같은 규칙을 따른다. 영역별 값은 사전에 담는다.
/// </summary>
internal sealed class DockLayoutState
{
    /// <summary>영역의 기본 바깥 패널.</summary>
    internal Dictionary<PanelArea, IDock> HomePanes { get; } = new();

    /// <summary>영역의 기본 안쪽 탭 영역.</summary>
    internal Dictionary<PanelArea, PanelDocumentDock> HomeTabs { get; } = new();

    /// <summary>영역이 붙은 바깥 가장자리.</summary>
    internal Dictionary<PanelArea, PanelEdge> Edges { get; } = new();

    /// <summary>메인 영역(옛 _mainPane). 진단 요약과 알림 단 유무 판정에 쓴다.</summary>
    internal IDockable? MainPane { get; set; }

    /// <summary>바깥 분할. 진단 모드(--notify-panel off)에서는 트리 밖이다.</summary>
    internal ProportionalDock? Outer { get; set; }

    /// <summary>문서 영역을 담는 칸. 문서 분할은 이 안에서만 일어나 패널 단과 섞이지 않는다.</summary>
    internal ProportionalDock? DocumentsHost { get; set; }

    /// <summary>분할로 만든 패널(옛 _splitPanes, UI-20). 키 = 바깥 패널 Id.</summary>
    internal Dictionary<string, SplitAreaService.SplitPane> SplitPanes { get; } = new();

    /// <summary>분할된 영역의 감싸기 ProportionalDock(옛 _areas).</summary>
    internal Dictionary<PanelArea, ProportionalDock> Areas { get; } = new();

    /// <summary>접힌 바깥 패널의 복원 정보(옛 _collapsed).</summary>
    internal Dictionary<IDock, PanelHideService.CollapsedPanel> Collapsed { get; } = new();

    /// <summary>숨긴 도구의 복원 정보(옛 _hiddenTools). 키 = 도구 Id.</summary>
    internal Dictionary<string, PanelHideService.HiddenTool> HiddenTools { get; } = new();

    /// <summary>핀 켜기 직전 비율 기억(옛 _pinShares, PW2).</summary>
    internal PanelPinShares PinShares { get; } = new();

    /// <summary>머리줄 ✕ 로 닫힌 기본 패널의 영역(PanelOpenStates 가 쓰고 PanelHideService 가 접기 판정에 읽는다).</summary>
    internal HashSet<PanelArea> ClosedAreas { get; } = new();
}
