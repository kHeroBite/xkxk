// 간트 캔버스의 막대 영역 층 그리기·기하 보조·글자 배치 캐시(GanttCanvas 부분 클래스, 동작은 분할 전과 같다)
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace xkxk.frameworks.ui.testbed.Documents;

public sealed partial class GanttCanvas
{
    /// <summary>
    /// 막대 영역을 층 순서(주 눈금선 → 줄무늬 → 막대·진척 → 선행선)로 그린다. 주 눈금선·줄무늬는 층마다 기하 1개·그리기 1번이다(A15).
    /// 예전에는 눈금선마다, 줄무늬 행마다 따로 그려 보이는 행·주 수에 비례해 호출이 늘었다(A15 프로파일 — 그리기 호출이 프레임 비용의 대부분).
    /// 눈금선끼리·줄무늬끼리는 겹치지 않아 묶어도 화소가 같다. 막대·진척·선행선은 묶으면 화소가 달라져 그대로 따로 그린다(각 층 주석).
    /// </summary>
    private void DrawBarLayers(DrawingContext context, int first, int last, int firstDay, int lastDay, double w, double h)
    {
        var weeks = new StreamGeometry();
        using (var g = weeks.Open())
        {
            for (var d = firstDay - (firstDay % 7); d <= lastDay; d += 7)
            {
                var x = LabelWidth + (d * _dayWidth) - _offsetX;
                AddLine(g, new Point(x, HeaderHeight), new Point(x, h));
            }
        }

        context.DrawGeometry(null, _gridPen, weeks);

        var stripes = new StreamGeometry();
        var anyStripe = false;
        using (var gs = stripes.Open())
        {
            for (var i = first + ((first & 1) == 0 ? 1 : 0); i <= last; i += 2)
            {
                AddRect(gs, new Rect(LabelWidth, HeaderHeight + (i * RowHeight) - _offsetY, w - LabelWidth, RowHeight));
                anyStripe = true;
            }
        }

        if (anyStripe) { context.DrawGeometry(_pal[6], null, stripes); }

        // 막대·진척은 행마다 둥근 사각형 그대로 그린다. 기하로 묶으면 모서리 안티에일리어싱이 둥근 사각형 전용 그리기와 화소 단위로 달라졌다
        // (A15 대조 2026-11-14: 묶음판 간트 영역 400~460화소·채널 최대 80 차이, 행별판 0).
        var pad = Math.Round(RowHeight * 0.18);
        for (var i = first; i <= last; i++)
        {
            var t = _tasks[i];
            var y = HeaderHeight + (i * RowHeight) - _offsetY;
            var x = BarX(t);
            var bw = Math.Max(2, t.Days * _dayWidth);
            context.FillRectangle(_pal[4], new Rect(x, y + pad, bw, RowHeight - (2 * pad)), 3);
            context.FillRectangle(_pal[5], new Rect(x, y + pad, bw * t.Progress / 100.0, RowHeight - (2 * pad)), 3);
        }

        // 선행 작업 종료 → 이 작업 시작 연결선(화면 내 항목만 — 선행 행이 화면 밖이면 생략). 선분마다 따로 그린다:
        // 같은 선행 작업에서 나온 세로선이 겹칠 때 따로 그리면 반투명 가장자리가 두 번 섞이는데, 한 기하로 묶으면 한 번만 섞여
        // 웹에서 화소가 달라졌다(A15 대조 — 라이트 524·다크 682화소, 채널 1단계). 펜만 재사용한다.
        // 선행선은 앞 행에서 내려와 뒤 행 막대와 겹치지 않으므로 막대를 모두 그린 뒤 그려도 예전(행마다 막대 뒤)과 겹침 순서가 같다.
        if (!ReferenceEquals(_depPen?.Brush, _pal[3])) { _depPen = new Pen(_pal[3], 1); }
        for (var i = first; i <= last; i++)
        {
            var t = _tasks[i];
            if (t.DependsOn is not int p || p < 0 || p >= _tasks.Count || Math.Abs(p - i) >= 60) { continue; }
            var y = HeaderHeight + (i * RowHeight) - _offsetY + (RowHeight / 2);
            var pt = _tasks[p];
            var px = LabelWidth + ((pt.Start.AddDays(pt.Days) - _origin).TotalDays * _dayWidth) - _offsetX;
            var py = HeaderHeight + (p * RowHeight) - _offsetY + (RowHeight / 2);
            context.DrawLine(_depPen, new Point(px, py), new Point(px + 4, py));
            context.DrawLine(_depPen, new Point(px + 4, py), new Point(px + 4, y));
            context.DrawLine(_depPen, new Point(px + 4, y), new Point(BarX(t), y));
        }
    }

    private IPen? _depPen;

    private double BarX(GanttTask t) => LabelWidth + ((t.Start - _origin).TotalDays * _dayWidth) - _offsetX;

    /// <summary>열린 선분 하나(끝 모양은 펜 기본 Flat — 예전 DrawLine 과 같다).</summary>
    private static void AddLine(StreamGeometryContext g, Point a, Point b)
    {
        g.BeginFigure(a, false);
        g.LineTo(b);
        g.EndFigure(false);
    }

    /// <summary>모서리가 각진 사각형 하나(채움 도형)를 더한다.</summary>
    private static void AddRect(StreamGeometryContext g, Rect r)
    {
        g.BeginFigure(r.TopLeft, true);
        g.LineTo(r.TopRight);
        g.LineTo(r.BottomRight);
        g.LineTo(r.BottomLeft);
        g.EndFigure(true);
    }

    // 글자 배치 캐시(A15) — 같은 문구·크기·붓(같은 인스턴스)이면 FormattedText 를 다시 만들지 않는다.
    // 글꼴·문화권이 바뀌거나 항목이 많아지면 통째로 비운다. 테마 색 편집은 붓 인스턴스의 Color 만 바꾸므로(ThemeApplier) 캐시된 글자도 새 색으로 그려진다.
    private readonly Dictionary<(string Text, double Size, IBrush Brush), FormattedText> _textCache = new(TextKeyComparer.Instance);
    private FontFamily? _textCacheFamily;
    private CultureInfo? _textCacheCulture;

    private void DrawText(DrawingContext context, string text, Point at, double size, IBrush brush)
    {
        var culture = CultureInfo.CurrentCulture;
        if (!ReferenceEquals(_textCacheFamily, TextFamily) || !ReferenceEquals(_textCacheCulture, culture) || _textCache.Count > 1024)
        {
            _textCache.Clear();
            _textCacheFamily = TextFamily;
            _textCacheCulture = culture;
        }

        var key = (text, size, brush);
        if (!_textCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(text, culture, FlowDirection.LeftToRight, new Typeface(TextFamily), size, brush);
            _textCache[key] = ft;
        }

        context.DrawText(ft, at);
    }

    /// <summary>캐시 키 비교 — 붓은 인스턴스(참조)로 비교한다(값 비교면 색이 같은 다른 붓을 섞어 쓰게 된다).</summary>
    private sealed class TextKeyComparer : IEqualityComparer<(string Text, double Size, IBrush Brush)>
    {
        public static readonly TextKeyComparer Instance = new();

        public bool Equals((string Text, double Size, IBrush Brush) a, (string Text, double Size, IBrush Brush) b) =>
            a.Size.Equals(b.Size) && ReferenceEquals(a.Brush, b.Brush) && string.Equals(a.Text, b.Text, StringComparison.Ordinal);

        public int GetHashCode((string Text, double Size, IBrush Brush) k) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(k.Text), k.Size, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Brush));
    }
}
