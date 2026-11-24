// 창 배치(스냅) 칸 종류 6종 정의와 비율 → 사각형 계산 — 순수 계산(화면·창 조작 없음, 11_창배치_스냅레이아웃.md §3.2·§3.4·§10)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>칸 한 개 — 기준 영역에 대한 비율(0~1). 가로가 긴 영역 기준으로 적고, 세로가 긴 영역에서는 가로·세로를 바꿔 쓴다.</summary>
public readonly record struct SnapCellRatio(double X, double Y, double Width, double Height);

/// <summary>칸 종류 한 개(id·칸 목록). id 는 설정 SnapLayouts 에 저장되는 값이다.</summary>
public sealed record SnapLayout(string Id, IReadOnlyList<SnapCellRatio> Cells);

/// <summary>모서리 4곳(4분의 1 배치).</summary>
public enum SnapCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// 칸 종류 목록과 사각형 계산. 칸 사각형은 기준 영역 안에 있으므로 끌 수 있는 구간 50px 규칙(MdiDragLimit)을 늘 만족한다.
/// 세로가 더 긴 영역은 좌우 배치를 위아래 배치로 바꾼다(Windows 세로 모니터 동작과 같다고 본 것은 추정 — 11 문서 §3.2).
/// Avalonia 값 형식(Rect·Size)만 쓰고 컨트롤·Dock 형식은 참조하지 않는다(레이어 분리, AC-006).
/// </summary>
public static class SnapLayoutCatalog
{
    public const string Max = "max";
    public const string Half = "half";
    public const string TwoThird = "twothird";
    public const string Thirds = "thirds";
    public const string LeftSplit = "leftsplit";
    public const string Quad = "quad";

    private const double Third = 1.0 / 3;

    /// <summary>칸 종류 전체(막대 표시 순서). 11 문서 §3.2 표와 같은 순서다.</summary>
    public static IReadOnlyList<SnapLayout> All { get; } = new[]
    {
        new SnapLayout(Max, new[] { new SnapCellRatio(0, 0, 1, 1) }),
        new SnapLayout(Half, new[] { new SnapCellRatio(0, 0, 0.5, 1), new SnapCellRatio(0.5, 0, 0.5, 1) }),
        new SnapLayout(TwoThird, new[] { new SnapCellRatio(0, 0, 2 * Third, 1), new SnapCellRatio(2 * Third, 0, Third, 1) }),
        new SnapLayout(Thirds, new[]
        {
            new SnapCellRatio(0, 0, Third, 1), new SnapCellRatio(Third, 0, Third, 1), new SnapCellRatio(2 * Third, 0, Third, 1),
        }),
        new SnapLayout(LeftSplit, new[]
        {
            new SnapCellRatio(0, 0, 0.5, 1), new SnapCellRatio(0.5, 0, 0.5, 0.5), new SnapCellRatio(0.5, 0.5, 0.5, 0.5),
        }),
        new SnapLayout(Quad, new[]
        {
            new SnapCellRatio(0, 0, 0.5, 0.5), new SnapCellRatio(0.5, 0, 0.5, 0.5),
            new SnapCellRatio(0, 0.5, 0.5, 0.5), new SnapCellRatio(0.5, 0.5, 0.5, 0.5),
        }),
    };

    /// <summary>기본 설정값(6종 전부, 쉼표 구분) — UiSettings.DefaultSnapLayouts.</summary>
    public static string DefaultIdList { get; } = string.Join(",", All.Select(l => l.Id));

    /// <summary>id 로 찾기(대소문자 무시). 없으면 null.</summary>
    public static SnapLayout? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(l => string.Equals(l.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 설정 문자열(쉼표 구분 id) → 켜진 칸 종류. null 이면 전부, 빈 문자열이면 없음(막대 안 띄움).
    /// 모르는 id·중복은 버리고, 순서는 설정 순서가 아니라 목록(All) 순서로 맞춘다(막대 모양이 저장 순서에 흔들리지 않게).
    /// </summary>
    public static IReadOnlyList<SnapLayout> Parse(string? ids)
    {
        if (ids is null)
        {
            return All;
        }

        var on = new HashSet<string>(
            ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        return All.Where(l => on.Contains(l.Id)).ToList();
    }

    /// <summary>켜진 칸 종류 → 정규화한 설정 문자열(목록 순서, 모르는 id 제거).</summary>
    public static string Normalize(string? ids) => string.Join(",", Parse(ids).Select(l => l.Id));

    /// <summary>세로가 더 긴 영역인가(좌우 → 위아래 전환 판정).</summary>
    public static bool IsPortrait(Rect area) => area.Height > area.Width;

    /// <summary>칸 종류 layout 의 cell 번째 칸이 기준 영역 area 에서 차지하는 사각형. 칸 번호가 범위 밖이면 빈 사각형.</summary>
    public static Rect CellRect(SnapLayout layout, int cell, Rect area)
    {
        if (cell < 0 || cell >= layout.Cells.Count)
        {
            return default;
        }

        var r = layout.Cells[cell];
        return IsPortrait(area) ? FromRatio(area, r.Y, r.X, r.Height, r.Width) : FromRatio(area, r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>칸 종류의 모든 칸 사각형(칸 번호 순서).</summary>
    public static IReadOnlyList<Rect> CellRects(SnapLayout layout, Rect area) =>
        Enumerable.Range(0, layout.Cells.Count).Select(i => CellRect(layout, i, area)).ToList();

    /// <summary>최대화 = 기준 영역 전체.</summary>
    public static Rect MaximizeRect(Rect area) => area;

    /// <summary>좌·우 가장자리 반쪽. 가장자리 판정은 영역 방향과 무관하게 좌우이므로 세로 영역에서도 좌우 반쪽이다.</summary>
    public static Rect HalfRect(Rect area, bool left) => left ? FromRatio(area, 0, 0, 0.5, 1) : FromRatio(area, 0.5, 0, 0.5, 1);

    /// <summary>모서리 4분의 1.</summary>
    public static Rect QuarterRect(Rect area, SnapCorner corner) => corner switch
    {
        SnapCorner.TopLeft => FromRatio(area, 0, 0, 0.5, 0.5),
        SnapCorner.TopRight => FromRatio(area, 0.5, 0, 0.5, 0.5),
        SnapCorner.BottomLeft => FromRatio(area, 0, 0.5, 0.5, 0.5),
        _ => FromRatio(area, 0.5, 0.5, 0.5, 0.5),
    };

    /// <summary>창 최소 크기(WindowMinSize·MdiTileMinSize) 이상인가. 미달 칸은 막대에서 비활성으로 두고 적용하지 않는다(11 문서 §10).</summary>
    public static bool Fits(Rect rect, Size minSize) =>
        rect.Width > 0 && rect.Height > 0 && rect.Width >= minSize.Width && rect.Height >= minSize.Height;

    /// <summary>칸 종류의 모든 칸이 최소 크기 이상인가.</summary>
    public static bool Fits(SnapLayout layout, Rect area, Size minSize) =>
        CellRects(layout, area).All(r => Fits(r, minSize));

    /// <summary>
    /// 비율 → DIP 사각형. 경계는 정수 DIP 로 반올림하고 이웃 칸이 같은 경계를 쓰게 시작·끝을 따로 반올림한다(틈·겹침 없음).
    /// </summary>
    private static Rect FromRatio(Rect area, double x, double y, double w, double h)
    {
        var left = area.X + Math.Round(x * area.Width);
        var top = area.Y + Math.Round(y * area.Height);
        var right = area.X + Math.Round((x + w) * area.Width);
        var bottom = area.Y + Math.Round((y + h) * area.Height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
