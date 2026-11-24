// 덮개·짝 나감 그림 밑에 깔 바탕 층을 조상에서 모으는 곳(21 설계 §4.1) — 테마·셸에 기대지 않는 시각 트리 규칙이라 단위 시험 test/unit/snapshot_geometry 가 링크한다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Pd.Ui.Views.Depth;

/// <param name="Brush">칠할 브러시(값을 저장해 두지 않고 찍을 때마다 읽은 것).</param>
/// <param name="Area">바탕 주인의 자리(화면 기준 DIP). 그라데이션·무늬가 화면과 같은 자리에 오도록 화면 사각형이 아니라 이 사각형에 칠한다.</param>
/// <param name="Radius">주인의 모서리 반경. 네 모서리가 같을 때만 쓰고 다르면 0.</param>
/// <param name="Source">주인 형 이름(진단 base).</param>
internal readonly record struct BackdropLayer(IBrush Brush, Rect Area, double Radius, string Source);

internal static class SnapshotBackdrop
{
    private const double Epsilon = 0.01; // 변환 오차로 '다 덮음' 판정이 빠지지 않게

    /// <summary>
    /// view 밑에 깔 바탕 층(아래→위 순). view 자기 바탕이 불투명하면 빈 목록이다.
    /// 조상을 가까운 쪽부터 올라가며 투명하지 않은 바탕을 모으고, view 자리를 다 덮는 불투명 바탕을 만나면 멈춘다.
    /// 끝내 못 만나면 fallback(테마 바탕)을 맨 아래에 view 자리로 깐다.
    /// 템플릿 주인(TemplatedControl)의 Background 가 이미 모은 층과 같은 인스턴스면 건너뛴다 — TemplateBinding 으로 템플릿 안 Border 에 넘긴 값이라 주인이 직접 칠하지 않는다.
    /// Border·Panel·ContentPresenter 는 실제로 칠하므로 같은 브러시여도 모두 모은다.
    /// </summary>
    public static IReadOnlyList<BackdropLayer> Collect(Control view, IBrush? fallback, string fallbackSource)
    {
        if (IsOpaque(BackgroundOf(view)))
        {
            return Array.Empty<BackdropLayer>();
        }

        var self = new Rect(view.Bounds.Size);
        var layers = new List<BackdropLayer>();
        var closed = false;
        foreach (var ancestor in view.GetVisualAncestors().OfType<Control>())
        {
            if (BackgroundOf(ancestor) is not { } brush || IsClear(brush)
                || (ancestor is TemplatedControl && layers.Any(l => ReferenceEquals(l.Brush, brush)))
                || ancestor.Bounds.Width <= 0 || ancestor.Bounds.Height <= 0 || ancestor.TransformToVisual(view) is not { } m)
            {
                continue;
            }

            var area = new Rect(ancestor.Bounds.Size).TransformToAABB(m);
            var radius = UniformRadius(ancestor);
            layers.Add(new BackdropLayer(brush, area, radius, ancestor.GetType().Name));
            if (IsOpaque(brush) && area.Deflate(radius).Inflate(Epsilon).Contains(self))
            {
                closed = true;
                break;
            }
        }

        if (!closed && fallback is not null && !IsClear(fallback))
        {
            layers.Add(new BackdropLayer(fallback, self, 0, fallbackSource));
        }

        layers.Reverse();
        return layers;
    }

    public static IBrush? BackgroundOf(Control control) => control switch
    {
        TemplatedControl t => t.Background,
        Panel p => p.Background,
        Border b => b.Background,
        ContentPresenter c => c.Background,
        _ => null,
    };

    public static bool IsClear(IBrush brush) => brush.Opacity <= 0 || brush is ISolidColorBrush { Color.A: 0 };

    /// <summary>단색·그라데이션만 판정한다. 무늬·이미지 브러시는 빈 곳이 있을 수 있어 불투명으로 보지 않는다(아래 층을 더 모을 뿐 결과는 같다).</summary>
    public static bool IsOpaque(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => s.Opacity >= 1 && s.Color.A == byte.MaxValue,
        IGradientBrush g => g.Opacity >= 1 && g.GradientStops.Count > 0 && g.GradientStops.All(x => x.Color.A == byte.MaxValue),
        _ => false,
    };

    private static double UniformRadius(Control control) => control switch
    {
        Border b => Uniform(b.CornerRadius),
        ContentPresenter c => Uniform(c.CornerRadius),
        TemplatedControl t => Uniform(t.CornerRadius),
        _ => 0,
    };

    private static double Uniform(CornerRadius r) =>
        r.TopLeft == r.TopRight && r.TopLeft == r.BottomRight && r.TopLeft == r.BottomLeft ? r.TopLeft : 0;
}
