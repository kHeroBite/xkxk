// 창 전체 보기 덮개의 카드 배치(회전목마·평면 격자·히트 사각형) — WindowOverview3D.cs 에서 분리한 partial
using System;
using Avalonia;
using Avalonia.Media;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

public sealed partial class WindowOverview3D
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        Children[0].Arrange(new Rect(finalSize));
        _hitRects = new Rect[_docs.Count];
        var w = Math.Min(_cardSize.Width, finalSize.Width);
        var h = Math.Min(_cardSize.Height, finalSize.Height);
        if (w <= 0 || h <= 0)
        {
            return finalSize; // 아직 배치 전(크기 0) — 카드는 다음 배치에서 놓는다
        }

        var deg = DesignThemeResolver.Number("depth.tiltDeg");
        var carousel = _mode == Carousel;
        var grid = carousel ? default : FlatGrid(finalSize, ref w, ref h);
        var gap = w * GapRatio;
        var projected = w * Math.Cos(deg * Math.PI / 180);
        var (first, step) = carousel ? CarouselSpacing(finalSize.Width, w, gap, projected) : (0, 0);
        var spans = new (double Left, double Right)[_docs.Count]; // 화면에 보이는(투영) 가로 구간
        for (var i = 0; i < _docs.Count; i++)
        {
            var d = i - _selected;
            var angle = carousel && d != 0 ? -Math.Sign(d) * deg : 0;
            var cx = (finalSize.Width / 2) + (d == 0 ? 0 : Math.Sign(d) * (first + ((Math.Abs(d) - 1) * step)));
            var rect = carousel
                ? new Rect(cx - (w / 2), (finalSize.Height - h) / 2, w, h)
                : new Rect(grid.Item1 + (i % grid.Item3 * (w + gap)), grid.Item2 + (i / grid.Item3 * (h + gap)), w, h);
            _cards[i].Arrange(rect);
            _cards[i].RenderTransform = angle == 0 ? null : new Rotate3DTransform { AngleY = angle, Depth = DesignThemeResolver.Number("depth.perspective") };
            var visible = rect.Width * Math.Cos(angle * Math.PI / 180);
            spans[i] = (rect.Center.X - (visible / 2), rect.Center.X + (visible / 2));
            _hitRects[i] = new Rect(spans[i].Left, rect.Y, visible, rect.Height);
        }

        if (carousel)
        {
            // 겹친 회전목마: 각 카드의 히트 사각형 = 가운데 쪽 이웃 카드(위에 그려짐)에 가리지 않는 띠. 띠 중심이 곧 원격 state 의 x
            for (var i = 0; i < _docs.Count; i++)
            {
                var d = i - _selected;
                var (left, right) = spans[i];
                if (d < 0)
                {
                    right = Math.Min(right, spans[i + 1].Left);
                }
                else if (d > 0)
                {
                    left = Math.Max(left, spans[i - 1].Right);
                }

                _hitRects[i] = new Rect(left, _hitRects[i].Y, Math.Max(0, right - left), _hitRects[i].Height);
            }
        }

        for (var i = 0; i < _docs.Count; i++)
        {
            _hits[i].Arrange(_hitRects[i]);
        }

        return finalSize;
    }

    /// <summary>
    /// 회전목마 간격 (가운데 카드 중심 → 첫 옆 카드 중심, 그 뒤 카드 사이). 기본은 겹치지 않는 간격이고, 선택 카드 기준 먼 쪽 끝 카드가
    /// 덮개 밖으로 나가면 화면 안에 들어오도록 간격을 줄여 겹친다(화면 밖 카드는 누를 수 없다 — K9 hit_test_3d, 창 수 ≤ depth.maxWindows).
    /// </summary>
    private (double First, double Step) CarouselSpacing(double width, double w, double gap, double projected)
    {
        var first = (w / 2) + gap + (projected / 2);
        var step = projected + gap;
        var side = Math.Max(_selected, _docs.Count - 1 - _selected);
        var room = (width / 2) - (projected / 2); // 가운데에서 끝 카드 중심까지 둘 수 있는 최대 거리
        if (side < 1 || first + ((side - 1) * step) <= room)
        {
            return (first, step);
        }

        first = Math.Max(0, Math.Min(first, room));
        return (first, side > 1 ? Math.Max(0, (room - first) / (side - 1)) : step);
    }

    /// <summary>평면 격자: 들어가는 열 수로 나누고 넘치면 카드를 비율 유지로 줄인다. 반환 = (왼쪽, 위, 열 수), w·h 는 줄인 카드 크기.</summary>
    private (double, double, int) FlatGrid(Size area, ref double w, ref double h)
    {
        var gap = w * GapRatio;
        var cols = Math.Max(1, Math.Min(_docs.Count, (int)((area.Width - gap) / (w + gap))));
        var rows = (int)Math.Ceiling(_docs.Count / (double)cols);
        var fit = Math.Clamp(Math.Min((area.Width - gap) / (cols * (w + gap)), (area.Height - gap) / (rows * (h + gap))), 0, 1);
        w *= fit;
        h *= fit;
        gap *= fit;
        return ((area.Width - (cols * (w + gap)) + gap) / 2, (area.Height - (rows * (h + gap)) + gap) / 2, cols);
    }
}
