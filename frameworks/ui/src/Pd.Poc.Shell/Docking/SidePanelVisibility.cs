// 도구 패널 표시(옛 WindowCommands.Panels) — '집중' 템플릿의 측면 패널 숨김/복원과 제품 빌드의 로그 도구 숨김(odev-1 PocDockFactory.Hide API 사용)
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Docking;

/// <summary>팩토리는 s.Factory, 로그는 s.Log 를 쓴다. 상수 LogToolId·ExplorerToolId 는 파사드가 같은 이름으로 다시 내보낸다.</summary>
[WindowCommandsPart]
internal sealed class SidePanelVisibility
{
    private readonly WindowSession _s;

    internal SidePanelVisibility(WindowSession s) => _s = s;

    /// <summary>제품 빌드에서 숨기는 진단 도구(로그 패널, team-lead 승인). 진단 빌드는 보인다(Q15 로그 탭 시험).</summary>
    public const string LogToolId = "Log";

    /// <summary>측면 패널이 숨겨져 있는가('집중' 템플릿).</summary>
    public bool SidePanelsHidden => _s.Factory.SidePanelsHidden;

    /// <summary>
    /// 측면 패널 숨김/복원. 원래 위치·분할선·비율·안쪽 탭(끌어 넣은 문서 탭 포함)을 그대로 되돌리는 처리는 odev-1 SetSidePanelsHidden 이 한다
    /// (Dock HideDockable/RestoreDockable 은 원래 인덱스·분할선을 되살리지 않아 쓰지 않는다 — odev-1 디컴파일 실측). 모든 패널이 대상이다.
    /// 이미 그 상태면 아무것도 하지 않는다.
    /// </summary>
    public void SetToolPanelsHidden(bool hidden)
    {
        if (_s.Factory.SidePanelsHidden == hidden)
        {
            return;
        }

        using (PerfProbe.Current.TimeOperation(hidden ? "ui.panels.hide" : "ui.panels.restore"))
        {
            _s.Factory.SetSidePanelsHidden(hidden);
            ApplyProductToolVisibility(); // 되살린 패널에서도 제품 빌드의 로그는 숨긴 채로
        }

        _s.Log(Loc.Tr(hidden ? "log.window.panelsHidden" : "log.window.panelsRestored"));
    }

    /// <summary>
    /// 제품 빌드(진단 기능 꺼짐)에서는 로그 도구를 숨긴다. 기동·레이아웃 복원·템플릿 적용 뒤마다 부른다(이미 숨겨져 있으면 아무것도 하지 않음).
    /// 로그 탭이 빠져 하단 패널이 비면 패널도 접히고, 사용자가 끌어 넣은 문서 탭이 있으면 패널은 남는다(odev-1 SetToolHidden).
    /// </summary>
    public void ApplyProductToolVisibility()
    {
        if (PerfProbe.IsEnabled || _s.Factory.IsToolHidden(LogToolId))
        {
            return;
        }

        // 순서 보장: 레이아웃(CreateLayout·InitLayout) 전에는 숨길 대상이 없고, CreateLayout 이 숨김 기록을 비운다(odev-1).
        // MainViewModel 생성자는 레이아웃 생성 뒤에 부르지만, 다른 경로에서 일찍 불려도 조용히 지나치지 않게 막고 경고를 남긴다.
        if (_s.Factory.Root is null)
        {
            System.Diagnostics.Debug.WriteLine("[경고][shell] 로그 도구 숨김 건너뜀: 레이아웃 생성 전 호출"); // i18n-ok 개발자 진단 출력
            return;
        }

        if (!_s.Factory.SetToolHidden(LogToolId, true))
        {
            System.Diagnostics.Debug.WriteLine("[경고][shell] 로그 도구 숨김 실패: 레이아웃에서 'Log' 를 찾지 못함"); // i18n-ok 개발자 진단 출력
        }
    }

    /// <summary>탐색기 도구 Id(보기 메뉴 '탐색기').</summary>
    public const string ExplorerToolId = "Explorer";

    /// <summary>
    /// 보기 메뉴 '탐색기'·'로그': 도구가 보이는 어딘가에 있으면 그 탭을 활성화하고, 숨겨져 있거나 홈 패널이 접혀 있으면
    /// 홈 패널(접힌 자리·분할선·비율 복원)로 되돌린다(odev-1 RevealTool). '집중' 템플릿으로 측면 패널이 숨겨져 있으면 먼저 되살린다.
    /// 제품 빌드의 로그는 숨김 결정(team-lead)이라 대상이 아니다. 성공하면 true.
    /// </summary>
    public bool ShowTool(string toolId)
    {
        if (toolId == LogToolId && !PerfProbe.IsEnabled)
        {
            return false;
        }

        bool shown;
        using (PerfProbe.Current.TimeOperation("ui.panels.showTool"))
        {
            if (_s.Factory.SidePanelsHidden)
            {
                SetToolPanelsHidden(false);
            }

            shown = _s.Factory.RevealTool(toolId);
        }

        var name = Loc.Tr(toolId switch
        {
            LogToolId => "panel.log",
            ViewModels.NotificationPanelViewModel.ToolId => "notify.title",
            _ => "panel.explorer",
        });
        _s.Log(Loc.Tr(shown ? "log.window.toolShown" : "log.window.toolShowFailed", name));
        return shown;
    }

    /// <summary>저장본의 측면 패널 숨김 적용(null = 옛 저장본, 현재 유지). 복원의 마지막 단계에서 부른다(odev-1 순서 계약).</summary>
    internal void ApplySidePanels(bool? hidden)
    {
        if (hidden is { } h)
        {
            SetToolPanelsHidden(h);
        }

        ApplyProductToolVisibility();
    }
}
