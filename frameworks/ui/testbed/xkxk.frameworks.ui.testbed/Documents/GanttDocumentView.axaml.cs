// 간트 문서 화면의 코드 비하인드(샘플 작업 생성·스크롤바 동기화·줌·열기 시간 계측)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.Input;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.testbed.Documents;

public partial class GanttDocumentView : UserControl
#if PD_DIAGNOSTICS
    , IGanttDocumentDiagnostics
#endif
{
    public const int DefaultTasks = 500;

    private bool _syncing;

    public GanttDocumentView()
        : this(DefaultTasks)
    {
    }

    public GanttDocumentView(int tasks)
    {
        PerfProbe.Current.TrackOpen(this, "gantt");
        InitializeComponent();
        Canvas = new GanttCanvas(CreateSample(tasks));
        CanvasHost.Child = Canvas;
        Avalonia.Automation.AutomationProperties.SetAutomationId(Canvas, "gantt.canvas"); // 원격 제어(UI-08) 식별자
        // 셸 글자 토큰 연결: 값이 바뀌면 캔버스가 행 높이를 다시 계산하고 다시 그린다(AffectsRender)
        Canvas.Bind(GanttCanvas.TextSizeProperty, this.GetResourceObservable("PdFontBody"));
        Canvas.Bind(GanttCanvas.TextFamilyProperty, this.GetResourceObservable("PdFontFamily"));
        PerfProbe.Current.SetMetricMax("gantt_tasks", tasks);
        _taskCount = tasks;
        // 상태 문구(작업 수·일당 px)는 언어가 바뀌면 다시 만든다
        AttachedToVisualTree += (_, _) => { XkxkUi.Current.Localization.LanguageChanged += UpdateTexts; UpdateTexts(); };
        DetachedFromVisualTree += (_, _) => XkxkUi.Current.Localization.LanguageChanged -= UpdateTexts;

        Canvas.ScrollChanged += SyncBars;
        HBar.Scroll += OnBarScroll;
        VBar.Scroll += OnBarScroll;
        ZoomInButton.Click += (_, _) => { using var op = PerfProbe.Current.TimeOperation("doc.gantt.zoom"); Canvas.SetZoom(Canvas.DayWidth * 1.25); };
        ZoomOutButton.Click += (_, _) => { using var op = PerfProbe.Current.TimeOperation("doc.gantt.zoom"); Canvas.SetZoom(Canvas.DayWidth * 0.8); };
        DenseWheel.Attach(Canvas, Canvas); // Alt=날짜 폭, Alt+Shift=행 높이
        SyncBars();
    }

    public GanttCanvas Canvas { get; }

    private readonly int _taskCount;

    private void UpdateTexts()
    {
        CountText.Text = Loc.Tr("doc.gantt.taskCount.status", _taskCount);
        ZoomText.Text = Loc.Tr("doc.gantt.dayWidth.status", Canvas.DayWidth);
    }

#if PD_DIAGNOSTICS // 자동 시험은 진단 빌드 전용(AutomationBridge.Commands 만 호출)
    public void DiagZoomStep(int round) => Canvas.SetZoom(Canvas.DayWidth * (round % 2 == 0 ? 1.25 : 0.8));

    public void DiagScrollStep() => Canvas.ScrollTo(Canvas.OffsetX + 40, Canvas.OffsetY + (Canvas.RowHeight * 3));

    /// <summary>
    /// 자동 시험(--cmd gantt-exercise): 20프레임 줌 인·20프레임 줌 아웃 → 60프레임 대각선 스크롤.
    /// 프레임 시간은 phase "gantt-zoom"·"gantt-scroll" 로 남는다.
    /// </summary>
    public async Task ExerciseAsync(TopLevel top)
    {
        var perf = PerfProbe.Current;
        perf.BeginPhase("gantt-zoom");
        var start = perf.NowMs;
        for (var i = 0; i < 40; i++)
        {
            using (perf.TimeOperation("doc.gantt.zoom")) { Canvas.SetZoom(Canvas.DayWidth * (i < 20 ? 1.1 : 1 / 1.1)); }
            await AutomationBridge.WaitFramesAsync(top, 1);
        }

        perf.RecordStep("gantt-zoom", perf.NowMs - start);
        perf.BeginPhase("gantt-scroll");
        start = perf.NowMs;
        var ext = Canvas.Extent;
        for (var i = 1; i <= 60; i++)
        {
            using (perf.TimeOperation("doc.gantt.scroll")) { Canvas.ScrollTo(ext.Width * i / 60, ext.Height * i / 60); }
            await AutomationBridge.WaitFramesAsync(top, 1);
        }

        perf.RecordStep("gantt-scroll", perf.NowMs - start);
    }
#endif

    /// <summary>결정적(시드 고정) 샘플: 연속 작업이 이전 작업에 일부 의존하는 계단형 일정.</summary>
    public static IReadOnlyList<GanttTask> CreateSample(int count)
    {
        var list = new List<GanttTask>(count);
        var rnd = new Random(500);
        var cursor = new DateTime(2026, 1, 5);
        string[] phases = { "요구 분석", "설계", "구현", "단위 시험", "통합 시험", "배포 준비" }; // i18n-ok 샘플 데이터(작업 이름)
        for (var i = 0; i < count; i++)
        {
            var depends = i > 0 && rnd.Next(3) == 0 ? i - 1 - rnd.Next(Math.Min(i, 5)) : (int?)null;
            var start = depends is int d ? list[d].Start.AddDays(list[d].Days) : cursor.AddDays(rnd.Next(-3, 4));
            var days = rnd.Next(2, 21);
            list.Add(new GanttTask(i + 1, $"{phases[i % phases.Length]} {(i / phases.Length) + 1}", start, days, rnd.Next(0, 101), depends));
            cursor = cursor.AddDays(rnd.Next(0, 3));
        }

        return list;
    }

    private void SyncBars()
    {
        _syncing = true;
        var ext = Canvas.Extent;
        var vp = Canvas.Viewport;
        HBar.Maximum = Math.Max(0, ext.Width - vp.Width);
        HBar.ViewportSize = vp.Width;
        HBar.Value = Canvas.OffsetX;
        VBar.Maximum = Math.Max(0, ext.Height - vp.Height);
        VBar.ViewportSize = vp.Height;
        VBar.Value = Canvas.OffsetY;
        ZoomText.Text = Loc.Tr("doc.gantt.dayWidth.status", Canvas.DayWidth);
        _syncing = false;
    }

    private void OnBarScroll(object? sender, ScrollEventArgs e)
    {
        if (!_syncing)
        {
            Canvas.ScrollTo(HBar.Value, VBar.Value);
        }
    }
}
