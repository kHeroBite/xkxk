// 시험용 화면 트리 — DOCK 탭(그림자 없는 Panel)과 MDI 창(그림자·둥근 클립이 있는 바깥 Border)을 닮게 만들고 위치를 아는 색 표식 2개를 둔다
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Pd.Test.SnapshotScale;

/// <param name="ShadowBug">원인 B 가설: 옛 찍기(dpi 배율)가 이 트리에서 배율만큼 어긋나야 하는가(그림자를 그린 뒤 자식이 오면 참).</param>
internal sealed record Tree(string Name, Func<Size, Control> Build, bool ShadowBug);

internal static class Trees
{
    public const double Mark = 6;            // 표식 한 변(DIP)
    public static readonly Color Red = Color.FromRgb(255, 0, 0);
    public static readonly Color Blue = Color.FromRgb(0, 0, 255);

    /// <summary>표식 왼쪽 위(DIP). 빨강은 왼쪽 위 근처, 파랑은 오른쪽 아래 근처 — 확대·잘림·이동을 함께 본다.</summary>
    public static Point RedAt(Size s) => new(Math.Min(14, s.Width / 4), Math.Min(10, s.Height / 4));

    public static Point BlueAt(Size s) => new(s.Width - Mark - Math.Min(12, s.Width / 5), s.Height - Mark - Math.Min(9, s.Height / 5));

    public static readonly Tree Dock = new("dock", s => Markers(s, new Panel { Background = Brushes.White }), false);

    /// <summary>MDI 창 닮은 트리: 바깥 Border(그림자·둥근 모서리·밖으로 그림) > 안쪽 Border(둥근 클립) > 표식.</summary>
    public static readonly Tree Mdi = new("mdi", s => Window(s, shadow: true, outer: true, clip: true), true);

    /// <summary>변형: 그림자만 뺌.</summary>
    public static readonly Tree MdiNoShadow = new("mdi_noshadow", s => Window(s, shadow: false, outer: true, clip: true), false);

    /// <summary>변형: 둥근 클립만 뺌(그림자는 남김).</summary>
    public static readonly Tree MdiNoClip = new("mdi_noclip", s => Window(s, shadow: true, outer: true, clip: false), true);

    /// <summary>변형: 바깥 Border 를 뺌(안쪽 둥근 클립 Border 만).</summary>
    public static readonly Tree MdiNoOuter = new("mdi_noouter", s => Window(s, shadow: false, outer: false, clip: true), false);

    public static readonly Tree[] All = { Dock, Mdi, MdiNoShadow, MdiNoClip, MdiNoOuter };

    private static Control Window(Size s, bool shadow, bool outer, bool clip)
    {
        var inner = new Border { CornerRadius = clip ? new CornerRadius(6) : default, ClipToBounds = clip, Background = Brushes.White };
        Markers(s, inner);
        if (!outer)
        {
            return inner;
        }

        inner.Width = double.NaN; // 바깥 Border 가 크기를 정한다
        inner.Height = double.NaN;
        return new Border
        {
            Width = s.Width,
            Height = s.Height,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = false, // 템플릿처럼 그림자를 밖으로 그린다
            BoxShadow = shadow ? BoxShadows.Parse("0 4 12 0 #40000000, 0 1 3 0 #30000000") : default,
            Background = Brushes.White,
            Child = inner,
        };
    }

    /// <summary>
    /// 레이아웃 반올림을 끈다. 화면 밖(TopLevel 없는) 트리는 배율 1 로 DIP 단위 반올림을 해 표식이 최대 0.5 DIP(= 0.5 × 배율 px) 옮겨지므로
    /// 기대 위치(소수 DIP)와 어긋난다. 제품 화면은 실제 배율로 반올림하므로 시험에서는 끄고 순수 좌표로 비교한다.
    /// </summary>
    public static void NoLayoutRounding(Control root)
    {
        root.UseLayoutRounding = false;
        foreach (var child in root.GetLogicalDescendants())
        {
            if (child is Control c)
            {
                c.UseLayoutRounding = false;
            }
        }
    }

    private static Control Markers(Size s, Control host)
    {
        var canvas = new Canvas();
        canvas.Children.Add(Square(RedAt(s), Red));
        canvas.Children.Add(Square(BlueAt(s), Blue));
        switch (host)
        {
            case Panel p:
                p.Children.Add(canvas);
                break;
            case Border b:
                b.Child = canvas;
                break;
        }

        host.Width = s.Width;
        host.Height = s.Height;
        return host;
    }

    private static Control Square(Point at, Color color)
    {
        var r = new Border { Width = Mark, Height = Mark, Background = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Canvas.SetLeft(r, at.X);
        Canvas.SetTop(r, at.Y);
        return r;
    }
}
