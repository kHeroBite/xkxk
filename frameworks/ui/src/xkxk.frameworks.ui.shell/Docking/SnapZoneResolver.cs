// 창 배치(스냅) 구역 판정 — 포인터·기준 영역·감지 거리 → 탭 합치기 > 막대 칸 > 모서리 > 위쪽 가장자리 최대화 > 좌우 가장자리, 최소 크기 미달 칸 비활성(순수 계산, 11 문서 §5·§10)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>판정된 구역 종류. 숫자가 작을수록 우선순위가 높다는 뜻은 아니다(우선순위는 SnapZoneContext.Resolve 순서).</summary>
public enum SnapZoneKind
{
    /// <summary>스냅 없음 — 놓으면 일반 이동만.</summary>
    None,
    /// <summary>탭 합치기 표적 위 — 스냅 미리보기·막대를 숨기고 적용하지 않는다(탭 합류가 이긴다).</summary>
    TabJoin,
    /// <summary>상단 막대의 칸.</summary>
    BarCell,
    /// <summary>모서리 4분의 1.</summary>
    Corner,
    /// <summary>위쪽 가장자리(막대 칸 밖) — 최대화.</summary>
    TopEdge,
    /// <summary>좌·우 가장자리 반쪽.</summary>
    Edge,
}

/// <summary>
/// 판정 결과. Target 이 비어 있지 않을 때만(HasTarget) 미리보기를 보이고 놓을 때 적용한다.
/// WantsBar = 막대를 띄우거나 유지할 위치인가(머무름 지연 SnapBarDelayMs 는 호출자 — 컨트롤 레이어가 잰다).
/// LayoutId·Cell 은 BarCell 일 때, Corner 는 Corner 일 때, Left 는 Edge 일 때만 뜻이 있다.
/// </summary>
public readonly record struct SnapZone(
    SnapZoneKind Kind, Rect Target, bool WantsBar,
    string? LayoutId = null, int Cell = -1, SnapCorner Corner = SnapCorner.TopLeft, bool Left = false)
{
    public static SnapZone None { get; } = new(SnapZoneKind.None, default, false);

    public bool HasTarget => Target.Width > 0 && Target.Height > 0;
}

/// <summary>막대 안 칸 하나의 판정·그리기 정보. Enabled=false 면 흐리게 그리고 적용하지 않는다(최소 크기 미달).</summary>
public sealed record SnapBarCell(string LayoutId, int Cell, Rect HitRect, Rect Target, bool Enabled);

/// <summary>막대 안 칸 종류 하나(배치도 타일).</summary>
public sealed record SnapBarTile(SnapLayout Layout, Rect TileRect, IReadOnlyList<SnapBarCell> Cells);

/// <summary>
/// 끌기 시작 때 1회 만드는 판정 문맥(기준 영역·옵션·막대 모양·칸 사각형을 미리 계산). 이동마다는 Resolve 의 포인터 판정만 한다(끌기 프레임 비용 — 11 문서 §5).
/// 막대 모양(BarRect·Tiles)은 UI 레이어(SnapLayoutBar)가 같은 값으로 그려 판정과 그림이 어긋나지 않게 한다.
/// 설정 레이어를 참조하지 않는다 — 값은 호출자가 넘긴다(UiSettings.CreateSnapContext 가 편의 입구).
/// </summary>
public sealed class SnapZoneContext
{
    // 막대 모양 초기 제시값(DIP, 실측 근거 없음 — 11 문서 머리 주의). 바꿀 일이 생기면 옵션으로 올린다.
    public const double BarTopMargin = 8;
    public const double BarPadding = 8;
    public const double TileGap = 8;
    public const double TileLong = 64;
    public const double TileShort = 40;
    public const double TileInset = 3;
    public const double CellGap = 2;

    private SnapZoneContext(Rect area, double distance, bool enabled, bool edgeEnabled, bool topEdgeMaximize,
        Size minSize, IReadOnlyList<SnapBarTile> tiles, Rect barRect)
    {
        Area = area;
        Distance = distance;
        Enabled = enabled;
        EdgeEnabled = edgeEnabled;
        TopEdgeMaximize = topEdgeMaximize;
        MinSize = minSize;
        Tiles = tiles;
        BarRect = barRect;
        HasBar = enabled && distance > 0 && tiles.Any(t => t.Cells.Any(c => c.Enabled));
    }

    public Rect Area { get; }
    public double Distance { get; }
    public bool Enabled { get; }
    public bool EdgeEnabled { get; }
    public bool TopEdgeMaximize { get; }
    public Size MinSize { get; }

    /// <summary>막대 타일(켜진 칸 종류 순서). 막대를 띄울 수 없으면(HasBar=false) 그리지 않는다.</summary>
    public IReadOnlyList<SnapBarTile> Tiles { get; }

    /// <summary>막대 바깥 사각형(기준 영역 좌표, 위쪽 가운데).</summary>
    public Rect BarRect { get; }

    /// <summary>막대를 띄울 수 있는가. 켜진 칸 종류가 없거나, 모든 칸이 최소 크기 미만이거나, 감지 거리 0 이면 false.</summary>
    public bool HasBar { get; }

    /// <summary>스냅 끔 문맥(처리기는 설치되지만 첫 판정에서 바로 돌아간다).</summary>
    public static SnapZoneContext Disabled { get; } = new(default, 0, false, false, false, default, Array.Empty<SnapBarTile>(), default);

    /// <summary>
    /// 판정 문맥 생성. area = 기준 영역(MDI 영역 또는 모니터 작업 영역, DIP), distance = 감지 거리(0 이면 가장자리·모서리·막대 끔),
    /// layouts = 켜진 칸 종류, minSize = 창 최소 크기, edgeEnabled = 좌우·모서리, topEdgeMaximize = 막대 칸 밖 위쪽 띠 놓기 → 최대화.
    /// </summary>
    public static SnapZoneContext Create(Rect area, double distance, IReadOnlyList<SnapLayout> layouts, Size minSize,
        bool edgeEnabled = true, bool topEdgeMaximize = true, bool enabled = true)
    {
        if (!enabled || area.Width <= 0 || area.Height <= 0 || double.IsNaN(area.Width) || double.IsNaN(area.Height))
        {
            return Disabled;
        }

        distance = Math.Max(0, distance);
        var (tiles, bar) = BuildBar(area, layouts, minSize);
        return new SnapZoneContext(area, distance, true, edgeEnabled, topEdgeMaximize, minSize, tiles, bar);
    }

    /// <summary>
    /// 포인터 판정(이동마다). p = 기준 영역과 같은 좌표계의 포인터, barVisible = 막대가 지금 떠 있는가, overTabJoinTarget = 탭 합치기 표적 위인가.
    /// 포인터가 영역 밖이면 영역 경계로 당겨 판정한다(화면 끝에 닿은 커서와 같게).
    /// 우선순위: ① 탭 합치기 ② 막대 칸 ③ 모서리 ④ 위쪽 가장자리 최대화 ⑤ 좌·우 가장자리(11 문서 §5).
    /// </summary>
    public SnapZone Resolve(Point p, bool barVisible, bool overTabJoinTarget)
    {
        if (!Enabled)
        {
            return SnapZone.None;
        }

        if (overTabJoinTarget)
        {
            return new SnapZone(SnapZoneKind.TabJoin, default, false);
        }

        var inBar = barVisible && HasBar && BarRect.Contains(p);
        if (inBar)
        {
            foreach (var tile in Tiles)
            {
                foreach (var cell in tile.Cells)
                {
                    if (cell.HitRect.Contains(p))
                    {
                        // 비활성 칸 위는 막대를 유지하되 적용하지 않는다
                        return cell.Enabled
                            ? new SnapZone(SnapZoneKind.BarCell, cell.Target, true, cell.LayoutId, cell.Cell)
                            : new SnapZone(SnapZoneKind.None, default, true, cell.LayoutId, cell.Cell);
                    }
                }
            }
        }

        if (Distance <= 0)
        {
            return inBar ? new SnapZone(SnapZoneKind.None, default, true) : SnapZone.None;
        }

        var c = new Point(Math.Clamp(p.X, Area.Left, Area.Right), Math.Clamp(p.Y, Area.Top, Area.Bottom));
        var top = c.Y - Area.Top <= Distance;
        var bottom = Area.Bottom - c.Y <= Distance;
        var left = c.X - Area.Left <= Distance;
        var right = Area.Right - c.X <= Distance;
        var inCorner = EdgeEnabled && (top || bottom) && (left || right);
        var wantsBar = inBar || (top && !inCorner && HasBar);

        if (inCorner)
        {
            var corner = top ? (left ? SnapCorner.TopLeft : SnapCorner.TopRight) : (left ? SnapCorner.BottomLeft : SnapCorner.BottomRight);
            var rect = SnapLayoutCatalog.QuarterRect(Area, corner);
            return SnapLayoutCatalog.Fits(rect, MinSize)
                ? new SnapZone(SnapZoneKind.Corner, rect, wantsBar, Corner: corner)
                : new SnapZone(SnapZoneKind.None, default, wantsBar);
        }

        if (top)
        {
            return TopEdgeMaximize && SnapLayoutCatalog.Fits(Area, MinSize)
                ? new SnapZone(SnapZoneKind.TopEdge, SnapLayoutCatalog.MaximizeRect(Area), wantsBar)
                : new SnapZone(SnapZoneKind.None, default, wantsBar);
        }

        if (EdgeEnabled && (left || right))
        {
            var rect = SnapLayoutCatalog.HalfRect(Area, left);
            return SnapLayoutCatalog.Fits(rect, MinSize)
                ? new SnapZone(SnapZoneKind.Edge, rect, wantsBar, Left: left)
                : new SnapZone(SnapZoneKind.None, default, wantsBar);
        }

        return wantsBar ? new SnapZone(SnapZoneKind.None, default, true) : SnapZone.None;
    }

    /// <summary>창 메뉴 '배치'·단축키용 — 칸 종류 id·칸 번호 → 이 영역의 사각형. 모르는 id·범위 밖·최소 크기 미달이면 빈 사각형.</summary>
    public Rect CellTarget(string layoutId, int cell)
    {
        var layout = SnapLayoutCatalog.Find(layoutId);
        if (!Enabled || layout is null)
        {
            return default;
        }

        var rect = SnapLayoutCatalog.CellRect(layout, cell, Area);
        return SnapLayoutCatalog.Fits(rect, MinSize) ? rect : default;
    }

    /// <summary>막대 모양 계산: 타일을 가로 한 줄로 놓고 위쪽 가운데 정렬, 영역보다 넓으면 비율을 줄인다.</summary>
    private static (IReadOnlyList<SnapBarTile> Tiles, Rect Bar) BuildBar(Rect area, IReadOnlyList<SnapLayout> layouts, Size minSize)
    {
        if (layouts.Count == 0)
        {
            return (Array.Empty<SnapBarTile>(), default);
        }

        var portrait = SnapLayoutCatalog.IsPortrait(area);
        var tileW = portrait ? TileShort : TileLong;
        var tileH = portrait ? TileLong : TileShort;
        var barW = BarPadding * 2 + layouts.Count * tileW + (layouts.Count - 1) * TileGap;
        var barH = BarPadding * 2 + tileH;
        var scale = Math.Min(1, Math.Max(0, area.Width - BarTopMargin * 2) / barW);
        if (scale <= 0)
        {
            return (Array.Empty<SnapBarTile>(), default);
        }

        var bar = new Rect(area.X + (area.Width - barW * scale) / 2, area.Y + BarTopMargin, barW * scale, barH * scale);
        var tiles = new List<SnapBarTile>(layouts.Count);
        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            var tile = new Rect(bar.X + (BarPadding + i * (tileW + TileGap)) * scale, bar.Y + BarPadding * scale, tileW * scale, tileH * scale);
            var inner = Shrink(tile, TileInset * scale);
            var cells = new List<SnapBarCell>(layout.Cells.Count);
            for (var c = 0; c < layout.Cells.Count; c++)
            {
                var target = SnapLayoutCatalog.CellRect(layout, c, area);
                var hit = Shrink(SnapLayoutCatalog.CellRect(layout, c, inner), CellGap * scale / 2);
                cells.Add(new SnapBarCell(layout.Id, c, hit, target, SnapLayoutCatalog.Fits(target, minSize)));
            }

            tiles.Add(new SnapBarTile(layout, tile, cells));
        }

        return (tiles, bar);
    }

    private static Rect Shrink(Rect r, double by) =>
        new(r.X + by, r.Y + by, Math.Max(0, r.Width - by * 2), Math.Max(0, r.Height - by * 2));
}

/// <summary>계획서 시그니처(Resolve(point, area, distance, layouts, minSize, topEdgeMaximize))의 1회용 입구. 끌기 중에는 SnapZoneContext 를 재사용한다.</summary>
public static class SnapZoneResolver
{
    public static SnapZone Resolve(Point point, Rect area, double distance, IReadOnlyList<SnapLayout> layouts, Size minSize,
        bool topEdgeMaximize = true, bool edgeEnabled = true, bool barVisible = false, bool overTabJoinTarget = false) =>
        SnapZoneContext.Create(area, distance, layouts, minSize, edgeEnabled, topEdgeMaximize).Resolve(point, barVisible, overTabJoinTarget);
}
