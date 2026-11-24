// 도킹 팩토리 진단 — 집중 숨김·핀·알림 이동 분할 기록과 알림 패널 진단 스위치(--notify-panel off|empty). 옛 PocDockFactory.Diag(P4). 제품 빌드에는 호출·문자열 0건
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
#endif
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 옛 partial void 훅(OverrideRootChild·DiagSplitRecord·DiagPinRestore)을 [Conditional("PD_DIAGNOSTICS")] 메서드로 바꿨다(W0 P4 방식).
/// 제품 빌드에서는 호출부와 인자 평가가 함께 빠진다. 진단 상태(분할 기록 8건)는 이 인스턴스가 가진다(팩토리 1개에 1개).
/// 팩토리가 필드 초기화자 `new()` 로 만든다(K2) — 생성자는 인자가 없고, 분할 요약에 쓰는 메인 영역(옛 _mainPane)은 호출 인자로 받는다.
/// </summary>
public sealed class DockFactoryDiag
{
#if PD_DIAGNOSTICS
    private readonly List<string> _diagSplitLog = new();
#endif

    /// <summary>
    /// CreateLayout 의 Root 자식 교체 지점. 구현은 진단 빌드(PD_DIAGNOSTICS)에만 있다.
    /// --notify-panel off 면 바깥 분할에서 MainPane 을 떼어 Root 의 유일한 자식으로 둔다.
    /// </summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void OverrideRootChild(IDock outer, IDockable main, ref IDockable rootChild)
    {
#if PD_DIAGNOSTICS
        if (!NotificationPanelDisabled)
        {
            return;
        }

        // 바깥 분할에서 MainPane 을 떼어 Root 의 유일한 자식으로 둔다. 알림 패널·바깥 분할선은 트리에 들어가지 않는다.
        outer.VisibleDockables?.Remove(main);
        main.Owner = null;
        main.Proportion = double.NaN;
        rootChild = main;
#endif
    }

    /// <summary>집중 숨김 접기·되살리기 때 기록한 비율을 진단 빌드에서만 남긴다(Android #125 세로 밀림 조사). 제품 빌드에서는 호출·인자 평가가 빠진다.</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal void DiagSplitRecord(string what, IDock pane, PanelHideService.CollapsedPanel record, bool overridden, IDockable? mainPane)
    {
#if PD_DIAGNOSTICS
        var sib = string.Join(",", record.Siblings.Select(s => $"{s.Dockable.Id}={P(s.Proportion)}"));
        var line = $"{what} {pane.Id} saved={P(record.Proportion)} now={P(pane.Proportion)} overridden={overridden} sib=[{sib}] owner={record.Owner.Id}";
        _diagSplitLog.Add(line);
        if (_diagSplitLog.Count > 8)
        {
            _diagSplitLog.RemoveAt(0);
        }

        Console.WriteLine("[split] " + line + " | " + DiagSplitState(mainPane));
#endif
    }

    /// <summary>핀 해제 뒤 비율 복원(PW2, PocDockFactory.PinDockableCore 재정의)을 splitLog·stdout 에 남긴다. 제품 빌드에서는 호출·인자 평가가 빠진다.</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal void DiagPinRestore(PinShare s, IDockable? mainPane)
    {
#if PD_DIAGNOSTICS
        var sib = string.Join(",", s.Siblings.Select(x => $"{x.Dockable.Id}={P(x.Proportion)}"));
        DiagSplitNote($"pinRestore {s.Pane.Id} saved={P(s.Proportion)} now={P(s.Pane.Proportion)} sib=[{sib}]", mainPane);
#endif
    }

#if PD_DIAGNOSTICS
    /// <summary>MainPane·CenterPane 자식 비율(NaN 그대로 표기) 요약. 예: "MainPane[LeftPane=0.18,CenterPane=NaN];CenterPane[Documents=0.8,BottomPane=0.2]".</summary>
    internal static string DiagSplitState(IDockable? mainPane)
    {
        string Of(IDockable? d) => d is IDock { VisibleDockables: { } list }
            ? $"{d.Id}[{string.Join(",", list.Where(x => x is not Dock.Model.Controls.IProportionalDockSplitter).Select(x => $"{x.Id}={P(x.Proportion)}"))}]"
            : "-";
        var center = (mainPane as IDock)?.VisibleDockables?.FirstOrDefault(d => d.Id == "CenterPane");
        return $"{Of(mainPane)};{Of(center)}";
    }

    /// <summary>팩토리 트리의 MainPane 으로 만든 분할 요약(원격 panel.joinProbe 용, 옛 PocDockFactory.DiagSplitState 파사드 대체).</summary>
    internal static string DiagSplitState(PocDockFactory? factory) =>
        DiagSplitState(factory?.Root is { } root ? factory.FindDockable(root, d => d.Id == "MainPane") : null);

    /// <summary>splitLog 에 한 줄 더하고(최근 8건 유지) stdout 에 현재 분할 상태와 함께 찍는다.</summary>
    private void DiagSplitNote(string line, IDockable? mainPane)
    {
        _diagSplitLog.Add(line);
        if (_diagSplitLog.Count > 8)
        {
            _diagSplitLog.RemoveAt(0);
        }

        Console.WriteLine("[split] " + line + " | " + DiagSplitState(mainPane));
    }

    /// <summary>접기·되살리기 기록 최근 8건(오래된 것부터).</summary>
    internal string DiagSplitLog => string.Join(" || ", _diagSplitLog);

    private static string P(double p) => double.IsNaN(p) ? "NaN" : p.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>--notify-panel off(0·false 포함): 알림 패널·바깥 분할(OuterPane)을 트리에 넣지 않는다(조건 C).</summary>
    internal static bool NotificationPanelDisabled => ArgValue("notify-panel") is "off" or "0" or "false";

    /// <summary>--notify-panel empty: 알림 패널 틀(ToolDock·분할)은 두고 내용 화면만 비운다(조건 E — 내용 비용과 Dock 틀 비용 분리).</summary>
    public static bool NotificationContentEmpty => ArgValue("notify-panel") == "empty";

    /// <summary>적용 중인 알림 패널 진단 스위치 요약(원격·perf 기록용). 예: "notify-panel=empty".</summary>
    public static string NotificationDiagSwitches =>
        $"notify-panel={ArgValue("notify-panel") ?? "on"}";

    /// <summary>"--name=value" 또는 "--name value" 의 값(소문자). 없으면 null. 정적 초기화에 기대지 않고 읽을 때마다 판정한다.</summary>
    private static string? ArgValue(string name)
    {
        IReadOnlyList<string> args = App.StartupArgs;
        var key = "--" + name;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            if (a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return a[(key.Length + 1)..].Trim().ToLowerInvariant();
            }

            if (a.Equals(key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1].Trim().ToLowerInvariant();
            }
        }

        return null;
    }
#endif
}
