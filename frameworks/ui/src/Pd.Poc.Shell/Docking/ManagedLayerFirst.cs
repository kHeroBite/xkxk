// Dock 템플릿의 ManagedWindowLayer 를 Grid 첫 자식으로 옮겨 PointerMoved 터널의 TryGetLayer 전위 순회를 없애는 제품 기본 보정(그리기·히트 순서는 ZIndex 로 유지)
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Avalonia.Controls;
using Pd.Ui.Controls;
#if PD_DIAGNOSTICS
using Pd.Poc.Shell.Diagnostics;
#endif

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Dock.Avalonia 12.1.0.12 Fluent DockControl 템플릿은 Grid[CommandBarHost, PART_ContentControl, PART_ManagedWindowLayer, PART_SelectorOverlay] 순서다(디컴파일).
/// DockableControlTracker 의 PointerMoved Tunnel 처리기가 관리형 창 모드에서 ManagedWindowLayer.TryGetLayer 를 부르고, 이는 DockControl 후손을 전위 순회해 레이어를 찾는다.
/// 레이어를 Grid 의 첫 자식으로 옮기면 순회가 레이어에서 바로 끝난다(웹 끌기 EventLatency -80%, 긴 작업 62→0 — poc/ui/logs/webperf/desktop/summary_otest-w-7.md).
/// 순서 유지: 옮기기 전 각 자식의 ZIndex 를 원래 자식 순번으로 매긴다. Avalonia 는 그리기·히트 시험을 ZIndex 오름차순(같으면 자식 순서)으로 하므로
/// 옮긴 뒤에도 콘텐츠 &lt; 레이어 &lt; 선택 오버레이 순서가 그대로다. 이미 ZIndex 가 0 이 아닌 자식이 있으면 순서를 보장할 수 없어 옮기지 않는다.
/// 데스크톱·Android·웹 공통 기본 동작이다. 관리형 창을 쓰지 않는 데스크톱에서도 같은 순서가 유지되므로 화면은 바뀌지 않는다.
/// </summary>
public static class ManagedLayerFirst
{
    /// <summary>MainView 생성자에서 1회 부른다. 템플릿이 적용될 때마다 레이어를 첫 자식으로 옮긴다.</summary>
    public static void Attach(DockControl dock)
    {
        dock.TemplateApplied += (_, e) => Apply(e);
    }

    private static void Apply(TemplateAppliedEventArgs e)
    {
        var layer = e.NameScope.Find<ManagedWindowLayer>(TemplateParts.ManagedWindowLayer);
        if (layer?.Parent is not Panel panel)
        {
            Report(0);
            return;
        }

        var index = panel.Children.IndexOf(layer);
        if (index < 0)
        {
            Report(0);
            return;
        }

        if (index == 0)
        {
            Report(1);
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child.ZIndex != 0)
            {
                Report(0);
                return;
            }
        }

        // 원래 자식 순번을 ZIndex 로 고정한 뒤 레이어만 맨 앞으로 옮긴다
        for (var i = 0; i < panel.Children.Count; i++)
        {
            panel.Children[i].ZIndex = i;
        }

        panel.Children.Move(index, 0);
        Report(1);
    }

    private static void Report(int applied)
    {
#if PD_DIAGNOSTICS
        // 진단 판만: 측정 스크립트 probe_all 이 grid_probe_ 접두사만 담는다(1=옮김·이미 첫 자식, 0=구조가 달라 못 옮김)
        PerfProbe.Current.SetMetric("grid_probe_layer_first_applied", applied);
#endif
    }
}
