// MDI 창 제목 줄에서 끌 수 있는 구간을 실제 배치에서 재는 도구 — 캡션 위치(Windows 오른쪽·macOS 왼쪽)와 기능 버튼 폭을 값으로 두지 않고 읽는다
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 끌기는 제목 칸(TitleGrid)에서만 시작한다. 제목 칸의 둘째 열은 기능 버튼 줄이 차지한 폭만큼 비워 둔 자리이고(MdiDocumentWindowTheme.axaml), 눌러도 끌리지 않으므로 뺀다.
/// 기능 버튼 줄 폭은 Foldable 로 함께 돌려준다. 줄이 접히면(MdiStripFold) 둘째 열이 0 이 되므로 창별로 마지막 폭을 기억해 접힌 동안에도 같은 전체 구간을 돌려준다.
/// 머리줄 앞쪽 부품만 보면 되므로 찾는 대로 멈춘다(문서 본문까지 내려가지 않는다).
/// </summary>
internal static class MdiGrabZoneReader
{
    private static readonly ConditionalWeakTable<Control, StrongBox<double>> LastStripWidth = new();

    /// <summary>창 항목(패널 자식)의 구간. 템플릿 적용 전이거나 제목 칸을 못 찾으면 MdiGrabZone.Whole.</summary>
    internal static MdiGrabZone Read(Control window) => Read(window, out _);

    /// <summary>partsMissing = 제목 칸 또는 머리줄을 시각 트리에서 못 찾았다(구조 문제). 배치 전(Bounds 0) 같은 일시 상태는 false.</summary>
    internal static MdiGrabZone Read(Control window, out bool partsMissing)
    {
        Control? header = null;
        Grid? grid = null;
        foreach (var visual in window.GetVisualDescendants())
        {
            if (visual is Control { Name: TemplateParts.Header } h)
            {
                header = h;
            }
            else if (visual is Grid { Name: TemplateParts.TitleGrid } g)
            {
                grid = g;
                break; // 제목 칸은 머리줄 안에 있으므로 머리줄을 먼저 만난다
            }
        }

        partsMissing = grid is null || header is null;
        if (grid is null || header is null || grid.Bounds.Width <= 0 || grid.TranslatePoint(default, window) is not { } at
            || header.TranslatePoint(new Point(0, header.Bounds.Height), window) is not { } bottom)
        {
            return MdiGrabZone.Whole;
        }

        var stripWidth = StripWidth(window, grid);
        var right = at.X + grid.Bounds.Width - stripWidth;
        return new MdiGrabZone(at.X, window.Bounds.Width - right, bottom.Y, stripWidth);
    }

    /// <summary>기능 버튼 줄 폭. 접혀 있으면 마지막으로 잰 폭을, 보이면 지금 폭을 기억하고 돌려준다.</summary>
    private static double StripWidth(Control window, Grid grid)
    {
        var box = LastStripWidth.GetOrCreateValue(window);
        if (!MdiStripFold.IsFolded(window))
        {
            box.Value = grid.ColumnDefinitions.Count > 1 ? grid.ColumnDefinitions[1].ActualWidth : 0;
        }

        return box.Value;
    }
}
