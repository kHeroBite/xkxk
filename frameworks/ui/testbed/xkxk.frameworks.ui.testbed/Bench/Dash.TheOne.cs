// TheOne.Avalonia 대시보드 어댑터(빌드 가능 여부 확인용 PoC) — 선·영역·막대·파이·도넛·게이지를 DashChartSpec 으로 만든다. 누적·콤보는 문서에서 확인하지 못해 미지원으로 둔다
#if PD_CHART_THEONE
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using TheOne.Avalonia.Controls;

namespace xkxk.frameworks.ui.testbed.Bench;

public sealed class DashTheOne : IDashboardAdapter
{
    private static bool s_themeAdded;

    public string Name => "TheOne.Avalonia 1.0.0-preview.11";

    // 누적 막대·콤보(보조축)는 문서에서 형식을 확인하지 못했다 [미확인] — 지원 안 함으로 세고, 후에 확인되면 바꾼다
    public bool Supports(DashKind kind) => kind is not (DashKind.StackedBar or DashKind.Combo);

    public Control Create(DashChartSpec s, DashStyle st)
    {
        EnsureTheme();
        var source = Series(s);
        var anim = st.Animate ? ChartAnimationStyle.Auto : ChartAnimationStyle.None;
        Control c = s.Kind switch
        {
            DashKind.Line => new LineChart { Title = s.Title, SeriesSource = source, AnimationStyle = anim },
            DashKind.Area => new AreaChart { Title = s.Title, SeriesSource = source, AnimationStyle = anim },
            DashKind.Bar => new BarChart { Title = s.Title, SeriesSource = source, AnimationStyle = anim },
            DashKind.Pie => new PieChart { Title = s.Title, SeriesSource = source, AnimationStyle = anim },
            DashKind.Donut => new DoughnutChart { Title = s.Title, SeriesSource = source, AnimationStyle = anim, InnerRadius = 0.55 },
            DashKind.Gauge => new GaugeChart { Title = s.Title, Minimum = 0, Maximum = 100, Value = s.Gauge },
            _ => throw new NotSupportedException($"TheOne 은 {s.Kind} 를 지원하지 않는다(미확인)"),
        };
        c.MinHeight = 160;
        return c;
    }

    // OneTheme 은 App.axaml 대신 코드로 한 번 붙인다(문서의 Application.Styles 예시와 같은 효과) [동작 미확인]
    private static void EnsureTheme()
    {
        if (s_themeAdded || Application.Current is not { } app) { return; }
        app.Styles.Add(new TheOne.Avalonia.Themes.OneTheme());
        s_themeAdded = true;
    }

    private static List<ChartSeries> Series(DashChartSpec s)
    {
        var list = new List<ChartSeries>();
        for (var i = 0; i < s.Series.Length; i++)
        {
            var pts = s.Series[i].Select((y, x) => new ChartPoint(x, y, s.Labels.Length > x ? s.Labels[x] : null)).ToList();
            list.Add(new ChartSeries { Name = s.SeriesNames[i], ItemsSource = pts });
        }

        return list;
    }
}
#endif
