// Snap Assist 상태 머신(정적 단일 세션) — 칸 배치 뒤 남은 칸마다 후보를 고르게 하고, 고르면 ApplyCell 로 놓고 다음 칸으로, 끝나면 고른 배치만 남긴다(11 문서 §3.9)
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// UI 스레드 전용. 화면은 SnapAssistHost.Factory 가 만든 ISnapAssistOverlay 로만 그린다(Shell → Views 참조 없음).
/// 진입: WindowMenuOps.ApplyCell(…, assist)·끌기 Apply 가 배치 성공 뒤 다음 프레임에 TryStart/TryStartFromZone 을 부른다(batch 2 — 이 클래스는 진입부를 모른다).
/// Pick 은 WindowMenuOps.ApplyCell(target, layoutId, cell) 를 assist 없이 부르므로 세션이 다시 시작되지 않는다.
/// 끝: 남은 칸 다 참(filled)·후보 없음(no-candidates)·Esc·바깥·포커스·영역 크기·UI 방식·배치한 창 닫힘·취소. 그때까지 고른 배치는 되돌리지 않는다.
/// 감시는 이벤트 구독뿐이다(세션 시작 때 구독, 끝 때 해제 — 타이머·스레드 없음).
/// </summary>
public static partial class SnapAssistSession
{
    private sealed class Run
    {
        public required SnapAssistScope Scope;
        public required object Anchor;
        public required WindowMenuTarget Placed;
        public required string LayoutId;
        public required ISnapAssistOverlay Overlay;
        public Rect Area;
        public List<SnapAssistZone> Zones = new();
        public HashSet<int> Skipped = new();
        public HashSet<string> Exclude = new();
        public int ActiveCell = -1;
        public SnapAssistCandidateList Current;
        public int Focus = -1;
        public bool Thumbnails;
        public Dictionary<string, string> ThumbStatus = new(); // 후보 키 → bitmap | fallback:<사유> (Views 가 ReportThumb 로 알린다)
        public Dictionary<string, string> ThumbDetail = new(); // 후보 키 → 찍은 대상·크기 진단 1줄
    }

    private static Run? s_run;
    private static bool s_applying;
    private static string s_lastEnd = string.Empty;

    /// <summary>세션이 떠 있는가.</summary>
    public static bool IsActive => s_run is not null;

    /// <summary>
    /// placed 를 layoutId·cell 에 놓은 직후 세션을 연다. anchor 는 SnapAssistHost.Factory 설명과 같다. 띄웠으면 true.
    /// 순서: 옵션(EffectiveSnapAssistEnabled, Edge·Corner 면 EffectiveSnapAssistOnEdge) → max 면 skip → 칸 종류 켜짐 → UI 방식(태블릿형·모바일형 skip)
    /// → 남은 칸 = 칸 수 − {배치 칸} → 칸마다 후보 → 후보 있는 첫 남은 칸이 활성(모두 0개면 no-candidates) → 오버레이 Show.
    /// 이미 세션이 있으면 그 세션을 cancel 로 끝내고 새로 연다.
    /// </summary>
    public static bool TryStart(WindowMenuTarget placed, string layoutId, int cell, SnapAssistSource source, SnapAssistScope scope, object anchor)
    {
        if (s_run is not null)
        {
            Cancel(SnapAssistEndReason.Cancel);
        }

        var reason = Start(placed, layoutId?.Trim() ?? string.Empty, cell, source, scope, anchor);
        LastSkipReason = reason ?? string.Empty;
        PdLog.Info("snap-assist", $"start src={source} scope={scope} layout={layoutId}#{cell} doc={placed.Document?.Id} result={(reason is null ? "shown" : "skip:" + reason)}");
        return reason is null;
    }

    /// <summary>
    /// 끌기 판정 결과로 연다. BarCell → 그 칸 그대로, Edge → half 의 좌·우 칸, Corner → quad 의 모서리 칸. TopEdge·None·TabJoin 은 띄우지 않는다(max).
    /// 세로 영역에서 칸 번호가 바뀌는 경우를 위해 판정 사각형(zone.Target)과 가장 가까운 칸을 고른다.
    /// </summary>
    public static bool TryStartFromZone(WindowMenuTarget placed, SnapZone zone, SnapAssistScope scope, object anchor)
    {
        switch (zone.Kind)
        {
            case SnapZoneKind.BarCell when zone.LayoutId is { } id:
                return TryStart(placed, id, zone.Cell, SnapAssistSource.BarCell, scope, anchor);
            case SnapZoneKind.Edge:
                return TryStart(placed, SnapLayoutCatalog.Half, NearestCell(SnapLayoutCatalog.Half, zone.Target, scope, anchor, zone.Left ? 0 : 1),
                    SnapAssistSource.Edge, scope, anchor);
            case SnapZoneKind.Corner:
                var fallback = zone.Corner switch
                {
                    SnapCorner.TopLeft => 0,
                    SnapCorner.TopRight => 1,
                    SnapCorner.BottomLeft => 2,
                    _ => 3,
                };
                return TryStart(placed, SnapLayoutCatalog.Quad, NearestCell(SnapLayoutCatalog.Quad, zone.Target, scope, anchor, fallback),
                    SnapAssistSource.Corner, scope, anchor);
            default:
                if (s_run is not null)
                {
                    Cancel(SnapAssistEndReason.Cancel);
                }

                LastSkipReason = "max";
                return false;
        }
    }


    /// <summary>세션 열기. 띄웠으면 null, 아니면 skip 사유.</summary>
    private static string? Start(WindowMenuTarget placed, string layoutId, int cell, SnapAssistSource source, SnapAssistScope scope, object anchor)
    {
        var settings = XkxkUi.Current.Settings;
        if (!settings.EffectiveSnapAssistEnabled)
        {
            return "disabled";
        }

        if ((source is SnapAssistSource.Edge or SnapAssistSource.Corner) && !settings.EffectiveSnapAssistOnEdge)
        {
            return "disabled";
        }

        if (string.Equals(layoutId, SnapLayoutCatalog.Max, StringComparison.OrdinalIgnoreCase))
        {
            return "max";
        }

        if (!settings.EffectiveSnapLayouts.Any(l => string.Equals(l.Id, layoutId, StringComparison.OrdinalIgnoreCase))
            || SnapLayoutCatalog.Find(layoutId) is not { } layout)
        {
            return "layout-off";
        }

        if (settings.EffectiveUiStyle is UiStyle.Tablet or UiStyle.Mobile || anchor is null)
        {
            return "scope-off";
        }

        var area = SnapAssistCandidates.AreaOf(scope, anchor);
        if (!(area.Width > 0 && area.Height > 0))
        {
            return "scope-off";
        }

        var ctx = settings.CreateSnapContext(area, WindowMinSize.Current);
        var zones = new List<SnapAssistZone>();
        for (var i = 0; i < layout.Cells.Count; i++)
        {
            var rect = ctx.CellTarget(layout.Id, i);
            if (rect.Width > 0 && rect.Height > 0)
            {
                zones.Add(new SnapAssistZone(i, rect, i == cell)); // 최소 크기 미달 칸은 남은 칸이 아니다
            }
        }

        if (!zones.Any(z => !z.Filled))
        {
            return "no-remaining";
        }

        var placedKey = SnapAssistCandidates.KeyOf(placed);
        var exclude = new HashSet<string> { placedKey };
        var first = -1;
        var firstList = default(SnapAssistCandidateList);
        foreach (var z in zones.Where(z => !z.Filled))
        {
            var list = SnapAssistCandidates.For(scope, anchor, placed, layout.Id, z.Cell, exclude);
            if (list.Candidates.Count > 0)
            {
                first = z.Cell;
                firstList = list;
                break;
            }
        }

        if (first < 0)
        {
            return "no-candidates";
        }

        if (SnapAssistHost.Factory?.Invoke(scope, anchor) is not { } overlay)
        {
            return "no-overlay";
        }

        var run = new Run
        {
            Scope = scope,
            Anchor = anchor,
            Placed = placed,
            LayoutId = layout.Id,
            Overlay = overlay,
            Area = area,
            Zones = zones,
            Exclude = exclude,
            Thumbnails = settings.EffectiveSnapAssistThumbnails,
        };
        Activate(run, first, firstList);
        s_run = run;
        Watch(run);
        try
        {
            overlay.Show(ViewOf(run));
        }
        catch (Exception ex)
        {
            PdLog.Info("snap-assist", $"show failed: {ex.Message}");
            s_run = null;
            Unwatch(run);
            return "no-overlay";
        }

        return null;
    }

    private static void Activate(Run r, int cell, SnapAssistCandidateList list)
    {
        r.ActiveCell = cell;
        r.Current = list;
        r.Focus = list.Candidates.Count > 0 ? 0 : -1; // 첫 표시·칸 바꿈 때 첫 후보에 초점
    }


    /// <summary>판정 사각형과 가장 가까운(좌상단·크기 차 합이 가장 작은) 칸 번호. 영역을 모르면 fallback.</summary>
    private static int NearestCell(string layoutId, Rect target, SnapAssistScope scope, object anchor, int fallback)
    {
        var area = SnapAssistCandidates.AreaOf(scope, anchor);
        if (!(area.Width > 0 && area.Height > 0) || SnapLayoutCatalog.Find(layoutId) is not { } layout || !(target.Width > 0))
        {
            return fallback;
        }

        var best = fallback;
        var bestScore = double.MaxValue;
        for (var i = 0; i < layout.Cells.Count; i++)
        {
            var r = SnapLayoutCatalog.CellRect(layout, i, area);
            var score = Math.Abs(r.X - target.X) + Math.Abs(r.Y - target.Y) + Math.Abs(r.Width - target.Width) + Math.Abs(r.Height - target.Height);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

}
