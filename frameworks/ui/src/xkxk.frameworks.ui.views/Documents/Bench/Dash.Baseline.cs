// 기준선 어댑터 — 차트 없이 같은 크기의 단순 Border 를 만든다(라이브러리 비용 = 대시보드 − 기준선, 설계서 B1 4·5절)
using xkxk.frameworks.ui.shell;
using Avalonia.Controls;
using Avalonia.Media;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public sealed class DashBaseline : IDashboardAdapter
{
    public string Name => "baseline(Border)";

    public bool Supports(DashKind kind) => true;

    public Control Create(DashChartSpec spec, DashStyle style) => new Border
    {
        BorderBrush = Brushes.Gray,
        BorderThickness = new Avalonia.Thickness(1),
        Child = new TextBlock { Text = spec.Title, Margin = new Avalonia.Thickness(8) },
    };
}
