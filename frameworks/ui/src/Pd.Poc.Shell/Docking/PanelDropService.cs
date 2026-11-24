// 패널 제목 줄 끌어 놓기 — Dock 의 판정(IDockDropPolicy)과 실행 시작(IDockOperationObserver)에서 패널째 놓기를 받아 PanelMoveService 로 보낸다. 트리에서 패널이 빠지는 Dock 기본 실행은 막는다
using Pd.Client.Data.Logging;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Dock 기본(ValidateDock → DockDockableIntoDockVisible)은 패널째 놓으면 안쪽 탭 영역을 대상 dock 으로 옮겨 패널이 트리에서 빠진다(D-PANELLOST, 디컴파일).
/// 그래서 실행 직전(BeginDockOperation)에 이쪽 이동을 예약하고, 같은 호출 안의 CanDrop 은 거부해 Dock 기본 실행을 건너뛴다. Drop 은 실행 결과와 무관하게 성공을 돌려줘 띄우기로 새지 않는다.
/// 놓는 곳: 패널 가운데 = 탭 합류, 패널 가장자리 = 그 옆에 분할(영역 방향만), 문서 영역·전역 안내 가장자리 = 그 가장자리로 이동.
/// 문서만 있는 패널을 문서 영역 가운데에 놓으면 예전처럼 활성 문서를 분리한다(PanelTabsService.BeforeMove).
/// </summary>
internal sealed class PanelDropService
{
    private sealed class Scope : IDockOperationScope
    {
        private readonly PanelDropService _owner;

        internal Scope(PanelDropService owner) => _owner = owner;

        public void Complete()
        {
        }

        public void Dispose() => _owner._executing = false;
    }

    private readonly PocDockFactory _f;
    private bool _executing;

    internal PanelDropService(PocDockFactory f) => _f = f;

    /// <summary>패널째 끌기인가(바깥 패널 또는 그 탭 영역).</summary>
    internal bool IsPanelDrag(IDockable source) => (source is PanelDocumentDock || (source is Dock.Model.Controls.IToolDock && _f.Tabs.TabsOf(source.Id ?? "") is not null)) && _f.Split.ResolvePane(source) is not null;

    /// <summary>패널째 놓기 판정. 실행 단계에서는 이미 예약했으므로 거부한다.</summary>
    internal bool CanDrop(IDockable source, IDockable target, DockOperation operation)
    {
        if (_executing || _f.Split.ResolvePane(source) is not { } from)
        {
            return false;
        }

        if (_f.Split.ResolvePane(target) is { } to)
        {
            if (ReferenceEquals(to.Pane, from.Pane))
            {
                return false; // 같은 위치 거절
            }

            return operation == DockOperation.Fill || (EdgeFor(operation) is not null && _f.Split.IsSplitAxis(to.Area, operation) && _f.Split.CanSplitNow(to.Area));
        }

        if (!InMainTree(target))
        {
            return false; // 독립 창으로는 패널을 옮기지 않는다
        }

        if (EdgeFor(operation) is { } edge)
        {
            return _f.Moves.EdgeOf(from.Area) != edge && _f.Moves.CanMoveToEdge(from.Pane, edge, wholePanel: true); // 같은 위치 거절
        }

        // 안내는 가운데(Fill) 판정이 참일 때만 뜬다(디컴파일 IsLocalTargetAvailable). 그래서 받아 두고, 실행은 Begin 이 정한다: 도구가 있는 패널은 제자리, 문서만 있는 패널은 활성 문서 분리다
        return operation == DockOperation.Fill;
    }

    /// <summary>실행 직전. 패널째 놓기면 이동을 예약하고 Dock 기본 실행을 막는 범위를 돌려준다. 아니면 null(Dock 기본).</summary>
    internal IDockOperationScope? Begin(IDockable source, IDockable target, DockOperation operation)
    {
        if (!IsPanelDrag(source))
        {
            return null;
        }

        if (operation == DockOperation.Window)
        {
            LogDrop(source, target, operation, "dock-default(window)");
            return null;
        }

        var centerOfNonPanel = operation == DockOperation.Fill && _f.Split.ResolvePane(target) is null;
        if (centerOfNonPanel && _f.Tabs.PanelFloatItem(source) is not null)
        {
            LogDrop(source, target, operation, "dock-default(float-active-doc)");
            return null; // 문서만 있는 패널: Dock 기본 경로가 BeforeMove 에서 활성 문서를 분리한다
        }

        if (!centerOfNonPanel)
        {
            // Dock 의 놓기 처리(안내 지우기·끌기 끝내기)가 끝난 뒤 트리를 바꾼다
            Dispatcher.UIThread.Post(() => Execute(source, target, operation));
        }

        LogDrop(source, target, operation, centerOfNonPanel ? "stay(tool-panel)" : "panel-move");
        _executing = true; // 문서 영역 가운데에 놓인 도구 패널은 제자리다(문서 영역은 패널을 담지 않는다)
        return new Scope(this);
    }

    /// <summary>패널째 놓기 판정 1줄 진단(B5). 놓는 순간 1회만 남는다(CanDrop 은 끄는 동안 반복 호출돼 로그하지 않는다).</summary>
    private void LogDrop(IDockable source, IDockable target, DockOperation operation, string result) =>
        PdLog.Info("panel-drop", $"source={source.Id} target={target.Id} op={operation} singleDoc={_f.Tabs.PanelFloatItem(source)?.Id ?? "-"} feature={_f.PanelFeature.Enabled} result={result}");

    private void Execute(IDockable source, IDockable target, DockOperation operation)
    {
        if (_f.Split.ResolvePane(source) is not { } from || _f.Tabs.TabsOf(from.Pane.Id ?? "") is not { VisibleDockables: { } list } tabs)
        {
            return;
        }

        var items = list.ToList();
        var active = tabs.ActiveDockable ?? items.FirstOrDefault();
        if (active is null)
        {
            return;
        }

        if (_f.Split.ResolvePane(target) is { } to)
        {
            var dest = operation == DockOperation.Fill
                ? _f.Tabs.TabsOf(to.Pane.Id ?? "")
                : _f.Split.SplitPaneCore(to.Area, to.Pane, operation, null);
            if (dest is not null)
            {
                _f.Moves.MoveTabs(items, active, dest);
            }
        }
        else if (EdgeFor(operation) is { } edge)
        {
            _f.Moves.MoveToEdge(from.Pane, items, active, edge, wholePanel: true);
        }
    }

    private bool InMainTree(IDockable target)
    {
        for (var d = target; d is not null; d = d.Owner)
        {
            if (ReferenceEquals(d, _f.Root))
            {
                return true;
            }
        }

        return false;
    }

    private static PanelEdge? EdgeFor(DockOperation operation) => operation switch
    {
        DockOperation.Left => PanelEdge.Left,
        DockOperation.Right => PanelEdge.Right,
        DockOperation.Top => PanelEdge.Top,
        DockOperation.Bottom => PanelEdge.Bottom,
        _ => null,
    };
}
