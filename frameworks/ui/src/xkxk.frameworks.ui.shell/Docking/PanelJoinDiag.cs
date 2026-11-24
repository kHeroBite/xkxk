// 패널 탭 합류 진단(진단 빌드 전용) — hover 이력 기록과 원격 panel.joinProbe 용 판정 과정 한 줄 문자열 생성
#if PD_DIAGNOSTICS
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>PanelJoinDrag 의 진단 짝. 끌기 상태는 PanelJoinDrag.DiagState 로 읽기만 한다.</summary>
internal static class PanelJoinDiag
{
    private const int HoverLogMax = 12;
    private static readonly System.Collections.Generic.List<string> s_hoverLog = new();
    private static string? s_openId;
    private static long s_openSince;
    private static long s_dragStartAt;

    /// <summary>hover 이력: 패널을 떠날 때 'Id@진입-이탈(머문ms)' 한 건을 남긴다. 시각은 끌기 시작(누름) 기준 ms(누름 전이면 Environment.TickCount64 그대로).</summary>
    public static void HoverChanged(Control? from, Control? to, long now)
    {
        if (s_openId is { } id)
        {
            Push($"{id}@{Rel(s_openSince)}-{Rel(now)}({now - s_openSince})");
        }

        s_openId = (to?.DataContext as IDock)?.Id;
        s_openSince = now;
    }

    /// <summary>MDI 제목줄 끌기를 새로 누를 때(hover 이력 구간 표시).</summary>
    public static void DragStart(string documentId)
    {
        s_dragStartAt = Environment.TickCount64;
        s_openId = null;
        Push($"start:{documentId}");
    }

    private static long Rel(long t) => s_dragStartAt > 0 ? t - s_dragStartAt : t;

    private static void Push(string item)
    {
        s_hoverLog.Add(item);
        if (s_hoverLog.Count > HoverLogMax)
        {
            s_hoverLog.RemoveAt(0);
        }
    }

    /// <summary>열린(아직 떠나지 않은) hover 구간까지 포함한 이력 문자열.</summary>
    private static string HoverLogText()
    {
        var items = s_hoverLog.ToList();
        if (s_openId is { } id)
        {
            items.Add($"{id}@{Rel(s_openSince)}-now({Environment.TickCount64 - s_openSince})");
        }

        return string.Join(';', items);
    }

    /// <summary>
    /// 점(TopLevel 클라이언트 DIP) 아래 합류 대상 판정 과정을 한 줄로 돌려준다(PanelJoinDrag.Probe 본문).
    /// 형식: "installed=…|hidden=…|pressed=…|moved=…|hover=…|captureLost=…|candidates=Id:x,y,w,h:visible:j=0|1;…|target=…|last=…".
    /// last 는 마지막 놓기 판정(window/toplevel 경로·target·hover·dwellMs·accepted)이다.
    /// </summary>
    public static string Probe(TopLevel top, Point point)
    {
        var st = PanelJoinDrag.DiagState;
        var parts = top.GetVisualDescendants().OfType<ToolDockControl>()
            .Select(c =>
            {
                var id = (c.DataContext as IDock)?.Id ?? c.DataContext?.GetType().Name ?? "null";
                var o = c.TranslatePoint(default, top) ?? default;
                // 마지막 항목 j=1 은 그 컨트롤 Classes 에 pdJoinTarget 이 실제로 붙어 있다는 뜻(강조 부착 확인 — 원격 tree.get 은 Classes 를 내지 않는다)
                return $"{id}:{o.X:0},{o.Y:0},{c.Bounds.Width:0},{c.Bounds.Height:0}:{c.IsEffectivelyVisible}:j={(c.Classes.Contains(PanelJoinDrag.TargetClass) ? 1 : 0)}";
            });
        var target = (PanelJoinTargets.FindAt(st.Factory, top, point)?.DataContext as IDock)?.Id ?? "none";
        // 문서 영역 후보(Android #125 세로 밀림 조사): 보이는 DocumentDockControl 전부의 dock Id·사각형·소유 dock Id·방향
        var docs = top.GetVisualDescendants().OfType<DocumentDockControl>().Where(c => c.IsEffectivelyVisible)
            .Select(c =>
            {
                var d = c.DataContext as IDock;
                var o = c.TranslatePoint(default, top) ?? default;
                var owner = d?.Owner as IDock;
                var orient = (owner as Dock.Model.Controls.IProportionalDock)?.Orientation.ToString() ?? "-";
                return $"{d?.Id ?? "null"}:{o.X:0},{o.Y:0},{c.Bounds.Width:0},{c.Bounds.Height:0}:owner={owner?.Id ?? "null"}/{orient}";
            });
        return $"installed={st.Installed}|hidden={st.Factory?.SidePanelsHidden}|pressed={st.Pressed?.Id ?? "none"}|moved={st.Moved}"
            + $"|hover={(st.Hover?.DataContext as IDock)?.Id ?? "none"}|captureLost={st.CaptureLost}|candidates={string.Join(';', parts)}|target={target}"
            + $"|hoverDwellMs={Environment.TickCount64 - st.HoverSince}|last={st.LastOutcome}|hoverLog={HoverLogText()}"
            // 알림 패널 진단 스위치 적용 확인: 인자 판독 결과와 실제로 만들어진 알림 화면의 영역 보고 방식(화면이 없으면 none)
            + $"|notify={DockFactoryDiag.NotificationDiagSwitches}|notifyView={Diagnostics.NotificationPanelProbe.LayoutMode ?? "none"}|notifyCreated={Diagnostics.NotificationPanelProbe.CreatedCount}|notifyAttached={Diagnostics.NotificationPanelProbe.AttachedCount}"
            + $"|docDocks={string.Join(';', docs)}"
            // 분할 비율(NaN 그대로)과 집중 숨김 접기·되살리기 기록 최근 8건(Android #125 — PocDockFactory.Diag)
            + $"|split={DockFactoryDiag.DiagSplitState(st.Factory)}|splitLog={st.Factory?.Diag.DiagSplitLog}";
    }
}
#endif
