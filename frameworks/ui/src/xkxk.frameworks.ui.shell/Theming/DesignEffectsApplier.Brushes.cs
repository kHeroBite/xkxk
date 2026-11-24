// 디자인 테마 효과 적용기의 그림자·표면·장식 브러시 생성 — DesignEffectsApplier.cs 에서 분리한 partial
using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class DesignEffectsApplier
{
    /// <summary>none = 빈 그림자, soft·float = 설정 키 shadow.&lt;값&gt;(BoxShadows 문자열). 형식 오류면 표 기본값.</summary>
    private static BoxShadows ShadowOf(string value)
    {
        var texts = value is "none" or Inherit ? Array.Empty<string?>() : new[] { DesignThemeResolver.Effective("shadow." + value), DesignThemes.Spec("shadow." + value)?.Default };
        foreach (var text in texts.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try { return BoxShadows.Parse(text!); }
            catch (FormatException) { } // 다음 후보(표 기본값)
        }

        return default;
    }

    /// <summary>flat = 기존 PdBrushSurface 브러시 그대로, gradient = 표면→보조 표면 세로 그라데이션, glass = 같은 그라데이션을 하한 불투명도로.</summary>
    private static IBrush? SurfaceOf(Application app, string value)
    {
        var surface = Token(app, "PdBrushSurface");
        if (value != "gradient" && value != "glass")
        {
            return app.TryGetResource("PdBrushSurface", app.ActualThemeVariant, out var b) ? b as IBrush : null;
        }

        var alpha = value == "glass" ? Math.Clamp(DesignThemeResolver.Number("glass.minOpacity"), 0, 1) : 1;
        return Vertical(WithAlpha(surface, alpha), WithAlpha(Token(app, "PdBrushSurfaceAlt"), alpha));
    }

    /// <summary>작업 영역 위 장식 층 브러시. none = 투명, dots·waves·grid·diagonal·hex·topo = 타일 무늬, aurora·gradient = 그라데이션, image = 사용자 이미지.</summary>
    private static IBrush DecorOf(Application app, string value)
    {
        var bg = Token(app, "PdBrushMdiBackground"); // 반투명 장식이 MDI 바탕을 가리지 않게 바탕을 함께 칠한다
        var ink = Token(app, "PdBrushAccent");
        var accent = Over(bg, WithAlpha(ink, DecorOpacity(bg, ink)));
        var tile = Math.Max(1, DesignThemeResolver.Number("decor.tile"));
        var size = Math.Max(1, DesignThemeResolver.Number("decor.dotSize"));
        switch (value)
        {
            case "dots":
                return Tile(tile, bg, new GeometryDrawing { Brush = new SolidColorBrush(accent), Geometry = new EllipseGeometry(new Rect((tile - size) / 2, (tile - size) / 2, size, size)) });
            case "waves":
                var mid = tile / 2;
                var path = string.Format(CultureInfo.InvariantCulture, "M0,{0} Q{1},0 {2},{0} T{3},{0}", mid, tile / 4, mid, tile);
                return Tile(tile, bg, new GeometryDrawing { Pen = new Pen(new SolidColorBrush(accent), size / 2), Geometry = Geometry.Parse(path) });
            case "aurora":
                return new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(accent, 0), new GradientStop(bg, 0.5), new GradientStop(accent, 1) },
                };
            case "grid": // 칸 위·왼쪽 변에만 선을 그어 바둑판이 이어진다
                return Tile(tile, bg, Stroke(accent, size / 4, F("M0,0 H{0} M0,0 V{0}", tile)));
            case "diagonal": // 오른쪽 위로 오르는 사선(칸 모서리끼리 이어진다)
                return Tile(tile, bg, Stroke(accent, size / 4, F("M0,{0} L{0},0", tile)));
            case "hex": // 뾰족한 위 벌집 — 칸 폭 √3·r, 높이 3r 에 육각형 1개와 아래 이음선
                var r = tile / 2;
                var w = Math.Sqrt(3) * r;
                return Tile(w, 3 * r, bg, Stroke(accent, size / 4, F("M{0},0 L{1},{2} L{1},{3} L{0},{4} L0,{3} L0,{2} Z M{0},{4} V{5}", w / 2, w, r / 2, 1.5 * r, 2 * r, 3 * r)));
            case "topo": // 등고선 느낌의 겹친 타원 고리
                var c = tile / 2;
                var rings = new GeometryGroup();
                foreach (var k in new[] { 0.16, 0.30, 0.44 }) { rings.Children.Add(new EllipseGeometry(new Rect(c - tile * k * 1.1, c - tile * k * 0.8, tile * k * 2.2, tile * k * 1.6))); }
                return Tile(tile, bg, new GeometryDrawing { Pen = new Pen(new SolidColorBrush(accent), Math.Max(1, size / 4)), Geometry = rings });
            case "gradient": // 위 바탕 → 아래 강조색 세로 2색
                return Vertical(bg, accent);
            case "image": // 사용자 배경 이미지(09/21 §6.2) — 준비 전·실패면 투명(none)
                return ImageOf();
            default:
                return Brushes.Transparent;
        }
    }

    /// <summary>
    /// 무늬 농도. 강조색이 바탕과 가까워 채널 차가 decor.minDelta 에 못 미치면 상한 안에서 올린다(18 §3).
    /// 사용자·회사가 정한 농도, ds-classic, 실효 surface=glass(글자 대비)는 그대로 둔다.
    /// </summary>
    private static double DecorOpacity(Color bg, Color ink)
    {
        var set = Math.Clamp(DesignThemeResolver.Number("decor.opacity"), 0, 1);
        if (DesignThemeResolver.Source("decor.opacity") is "user" or "company"
            || DesignThemes.FindOrDefault(DesignThemeResolver.Current).Id == DesignThemes.DefaultId
            || DesignThemeResolver.Effective("surface") == "glass")
        {
            return set;
        }

        var reach = Math.Max(Math.Abs(ink.R - bg.R), Math.Max(Math.Abs(ink.G - bg.G), Math.Abs(ink.B - bg.B))) * ink.A / 255.0;
        if (reach <= 0)
        {
            return set; // 두 색이 같으면 농도로는 벌릴 수 없다
        }

        var needed = Math.Ceiling(DesignThemeResolver.Number("decor.minDelta") * 255 / reach) / 255; // 알파 1/255 단위 올림 — Over 의 내림에 모자라지 않게
        var max = DesignThemes.Spec("decor.opacity")?.Max ?? set;
        return Math.Max(set, Math.Min(max, needed));
    }

    private static GeometryDrawing Stroke(Color color, double thickness, string path) =>
        new() { Pen = new Pen(new SolidColorBrush(color), Math.Max(1, thickness)), Geometry = Geometry.Parse(path) };

    private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    private static IBrush Tile(double tile, Color bg, Drawing mark) => Tile(tile, tile, bg, mark);

    private static IBrush Tile(double width, double height, Color bg, Drawing mark)
    {
        var cell = new Rect(0, 0, width, height);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(cell) }); // 타일 크기 고정 + 바탕 채우기
        group.Children.Add(mark);
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            SourceRect = new RelativeRect(cell, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(cell, RelativeUnit.Absolute),
        };
    }

    private static LinearGradientBrush Vertical(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
    };

    private static Color Token(Application app, string key) =>
        app.TryGetResource(key, app.ActualThemeVariant, out var r) && r is ISolidColorBrush b ? b.Color : Colors.Transparent;

    private static Color WithAlpha(Color c, double factor) => Color.FromArgb((byte)Math.Round(c.A * factor), c.R, c.G, c.B);
    private static Color Over(Color bg, Color fg) => Color.FromRgb((byte)(bg.R + (fg.R - bg.R) * fg.A / 255), (byte)(bg.G + (fg.G - bg.G) * fg.A / 255), (byte)(bg.B + (fg.B - bg.B) * fg.A / 255));
}
