// 벤치 문서 화면 코드 — 열린 벤치 문서 목록을 들고 있어 원격 명령(bench.*)이 대상 화면을 찾게 한다
using xkxk.frameworks.ui.shell;
using System.Collections.Generic;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public partial class BenchDocumentView : UserControl
{
    private static readonly List<BenchDocumentView> Open = new();

    public BenchDocumentView() : this("grid") { }

    public BenchDocumentView(string target)
    {
        InitializeComponent();
        Tabs.SelectedIndex = target == "chart" ? 1 : 0;
        AttachedToVisualTree += (_, _) => { if (!Open.Contains(this)) { Open.Add(this); } };
        DetachedFromVisualTree += (_, _) => Open.Remove(this);
    }

    public GridBenchView Grid => GridBench;
    public ChartBenchView Chart => ChartBench;

    /// <summary>가장 최근에 화면에 붙은 벤치 문서(없으면 null).</summary>
    public static BenchDocumentView? Latest => Open.Count > 0 ? Open[^1] : null;

    public void Select(string target) => Tabs.SelectedIndex = target == "chart" ? 1 : 0;
}
