// 대시보드 style=pd 에서 두 차트 라이브러리가 똑같이 쓰는 PD 테마 색·글꼴 토큰 — 앱 리소스 PdBrush* 를 읽는다(공용, 스타일 비용 계산에서 뺀다)
// 초안(odev-gk, 2026-10-30, 컴파일 확인 전). 반영은 i2410 대기열. 대상 경로: testbed/xkxk.frameworks.ui.testbed/Bench/Dash.PdTokens.cs
// 근거: 키 이름은 Theming/ThemeDefinition.cs KeyList(PdBrushSurface·PdBrushText·PdBrushTextMuted·PdBrushGridLine·PdBrushBorder·PdBrushAccent 등)에서 직접 확인했다.
// 글꼴: '각자 가능한 최선'(team-lead 05:4x). LiveCharts2 는 앱 글꼴 Noto Sans KR(BenchFonts "noto" = PdFontFamily 계열), ScottPlot 은 차트 전용 나눔고딕("nanum").
//   ScottPlot × Noto KR 은 회전 축 이름·범례가 깨져 PD 앱 글꼴을 쓸 수 없다(BenchFonts.cs 15행, logs/odevgc/spfont 재현) — 스타일 비용 감점 사유.
using xkxk.frameworks.ui.shell;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace xkxk.frameworks.ui.testbed.Bench;

public static class DashPdTokens
{
    public const string ScottPlotFontKey = "nanum";
    public const string LiveChartsFontKey = BenchFonts.DefaultKey; // "noto"

    // 시리즈 색: PD 의미 색 토큰 순서(강조·성공·주의·경고·오류·간트 진행). 리소스가 없으면 pd-light 값으로 대신한다(BuiltinThemes.cs pd-light 문자열)
    private static readonly (string Key, uint Fallback)[] SeriesKeys =
    {
        ("PdBrushAccent", 0xFF40568D), ("PdBrushSuccess", 0xFF15803D), ("PdBrushAttention", 0xFFFFB000),
        ("PdBrushWarning", 0xFFB45309), ("PdBrushError", 0xFFDC2626), ("PdBrushGanttProgress", 0xFF1E40AF),
    };

    public static SKTypeface LiveChartsFont => BenchFonts.Get(LiveChartsFontKey);
    public static SKColor Surface => Get("PdBrushSurface", BenchChartColors.Background);
    public static SKColor Text => Get("PdBrushText", BenchChartColors.Text);
    public static SKColor Muted => Get("PdBrushTextMuted", BenchChartColors.Soft);
    public static SKColor GridLine => Get("PdBrushGridLine", BenchChartColors.Grid);
    public static SKColor Border => Get("PdBrushBorder", BenchChartColors.Soft);

    public static SKColor Series(int i)
    {
        var (key, fb) = SeriesKeys[i % SeriesKeys.Length];
        return Get(key, new SKColor(fb));
    }

    // 현재 테마(라이트·다크)의 리소스 값을 읽는다. 단색 브러시가 아니면 대체값을 쓴다
    private static SKColor Get(string key, SKColor fallback)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is ISolidColorBrush b)
        {
            return new SKColor(b.Color.R, b.Color.G, b.Color.B, b.Color.A);
        }

        return fallback;
    }
}
