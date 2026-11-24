// MDI 창 단위(탭 묶음이면 묶음) 전체 이동·위치 고정 보존·레이아웃 초기화 — WindowStateOps 의 분리 부분(300줄 상한)
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Docking;

internal sealed partial class WindowStateOps
{
    /// <summary>문서가 화면에 보이는 MDI 창(탭 묶음 안이면 묶음).</summary>
    private static IMdiDocument Win(PocDocument doc) => MdiTabGroup.WindowOf(doc)!;

    /// <summary>위치 고정 창인가. 묶음은 안 문서 중 하나라도 잠기면 잠긴 창이다(MdiZOrder·MdiTileMinSize 와 같은 기준).</summary>
    private static bool Locked(IMdiDocument win) =>
        win is MdiTabGroupDock g ? g.Documents.Any(d => ChromeOf(d).IsLocked) : win is PocDocument doc && ChromeOf(doc).IsLocked;

    /// <summary>MDI 칸의 창 목록(묶음은 1개로 센다, 열린 순서).</summary>
    private List<IMdiDocument> MdiWindows() => MdiDocs().Select(Win).Distinct().ToList();

    /// <summary>MDI 창 전체 이동. 경계값만 바꾸고 배치 재계산은 Dock 레이아웃 패스에 맡긴다(프레임 단위 반복 호출 전제).</summary>
    public void MoveAll(double dx, double dy)
    {
        // 프레임마다 반복 호출되므로 비용 최소화(PERF-02): 플로팅 창이 없으면 창 트리 재귀 탐색을 생략하고,
        // 보통 상태 창만 경계값을 바꿔(최소/최대 창은 위치 무의미) 불필요한 배치 무효화를 줄인다.
        // 여러 창의 배치 무효화는 Avalonia 레이아웃 패스에서 한 번에 합쳐진다. 묶음이 없으면 집합도 만들지 않는다.
#if PD_DIAGNOSTICS
        using var probe = MdiDragProbe.Place(); // 끌기 단계 '배치' 할당(--drag-probe 일 때만, 끄면 bool 검사 하나)
#endif
        var hasWindows = Root.Windows?.Any() == true;
        HashSet<MdiTabGroupDock>? seen = null;
        for (var i = 0; i < OpenDocuments.Count; i++)
        {
            var doc = OpenDocuments[i];
            if ((hasWindows && FindWindow(doc) is not null) || PocDockFactory.PanelOf(doc) is not null) // 측면 패널 탭 안 문서는 MDI 위치가 없다
            {
                continue; // SDI·측면 패널 문서는 옮기지 않음
            }

            IMdiDocument win = doc;
            if (MdiTabGroup.GroupOf(doc) is { } g)
            {
                if (!(seen ??= new HashSet<MdiTabGroupDock>()).Add(g))
                {
                    continue; // 같은 묶음은 한 번만 옮긴다
                }

                win = g;
            }

            if (win.MdiState != MdiWindowState.Normal || Locked(win))
            {
                continue; // 최소/최대·위치 고정 창은 옮기지 않음
            }

            var b = win.MdiBounds;
            win.MdiBounds = new DockRect(b.X + dx, b.Y + dy, b.Width, b.Height);
        }
    }

    /// <summary>위치 고정 창은 배치 명령에서 제외: 배치 전 창(묶음 포함)의 위치·상태를 기억했다가 배치 뒤 되돌린다.</summary>
    internal void PreserveLocked(Action arrange)
    {
        var locked = MdiWindows().Where(Locked).Select(w => (Win: w, w.MdiBounds, w.MdiState)).ToList();
        arrange();
        foreach (var (win, bounds, state) in locked)
        {
            win.MdiBounds = bounds;
            win.MdiState = state;
        }
    }

    public void ResetLayout()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.resetLayout");
        RestoreAll();
        var windows = MdiWindows();
        var placed = windows.Where(Locked).Select(w => w.MdiBounds).ToList(); // 고정 창 자리는 피한다
        foreach (var win in windows.Where(w => !Locked(w)))
        {
            win.MdiBounds = PocDockFactory.NextMdiBounds(placed); // 묶음 1개 = 자리 1개
            placed.Add(win.MdiBounds);
        }

        _log(Loc.Tr("log.window.layoutReset"));
    }
}
