// 그리드 문서 열기 지연 분해 계측 — 생성자 끝→부착·첫 측정·첫 배치·첫 렌더 시간과 이후 측정·배치 크기 변화(진단 빌드)를 기록
using xkxk.frameworks.ui.shell;
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// GridDocumentView 1개에 붙는 측정·배치 계측기. 뷰의 MeasureOverride·ArrangeOverride 가 base 구현을 넘겨 부른다.
/// 기록 이름: grid.open.{attach|measure1|arrange1|render}.{first|next}
/// attach = 생성자 끝→화면 부착, measure1/arrange1 = 첫 Measure/Arrange 자체 시간, render = 첫 Arrange 끝→두 번째 rAF(첫 프레임 그려짐).
/// 제품 빌드(PerfProbe.IsEnabled=false)에서는 분해를 시작하지 않는다.
/// 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GridLayoutProbe
{
    private static int s_openSeq; // 프로세스 안 그리드 생성 순번(첫 열기 vs 다음 열기 구분)
    private readonly Control _view;
    private string _openTag = "first";
    private double _tCtorEnd = -1;
    private double _tAttach = -1;
    private bool _measured;
    private bool _arranged;

    /// <summary>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
    public GridLayoutProbe(Control view) => _view = view;

    /// <summary>뷰 생성자 끝에서 호출. 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
    public void BeginOpenBreakdown()
    {
        if (!PerfProbe.IsEnabled) { return; }
        _openTag = s_openSeq++ == 0 ? "first" : "next";
        _tCtorEnd = PerfProbe.Current.NowMs;
        _view.AttachedToVisualTree += OnAttachedForBreakdown;
    }

    private void OnAttachedForBreakdown(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _view.AttachedToVisualTree -= OnAttachedForBreakdown;
        _tAttach = PerfProbe.Current.NowMs;
        Record("attach", _tAttach - _tCtorEnd);
    }

    /// <summary>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
    public Size Measure(Size availableSize, Func<Size, Size> baseMeasure)
    {
        if (_measured || _tAttach < 0) { return MeasureLater(availableSize, baseMeasure); }
        _measured = true;
        var t = PerfProbe.Current.NowMs;
        GridOpenBreakdown.Begin(); // A16: 첫 측정 안 시간 분해(진단 빌드만 — Conditional)
        var size = baseMeasure(availableSize); // DataGrid 첫 측정: 행·셀 실현, 텍스트 측정 포함
        var ms = PerfProbe.Current.NowMs - t;
        Record("measure1", ms);
        GridOpenBreakdown.End(_openTag, ms);
        return size;
    }

    /// <summary>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
    public Size Arrange(Size finalSize, Func<Size, Size> baseArrange)
    {
        if (_arranged || !_measured) { return ArrangeLater(finalSize, baseArrange); }
        _arranged = true;
        var t = PerfProbe.Current.NowMs;
        var size = baseArrange(finalSize);
        var end = PerfProbe.Current.NowMs;
        Record("arrange1", end - t);
        if (TopLevel.GetTopLevel(_view) is { } top)
        {
            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => Record("render", PerfProbe.Current.NowMs - end)));
        }

        return size;
    }

    private void Record(string part, double ms) => PerfProbe.Current.RecordOperation($"grid.open.{part}.{_openTag}", ms, ms);

    // Q4 수정안 ①(측정 폭 안정화 StabilizeWidth)은 원인이 Z1(Views/ZoomDecorator.cs)로 해소돼 되돌렸다(lead 2026-10-29, otest-m-1 i1239 PASS).

    // 첫 측정·배치 뒤의 측정·배치(Q4 — 테마·글꼴 전환 때 그리드가 다시 측정되는지, 받은 크기가 바뀌었는지). 진단 빌드에서만 기록.
    // perf 조작 기록 grid.measure·grid.arrange(ms), 지표 grid_measure_sizechg·grid_arrange_sizechg(받은 크기가 직전과 달라진 횟수, 모든 그리드 합).
#if PD_DIAGNOSTICS // 진단판에서만 쓰는 필드(제품판 CS0169 경고 방지)
    private Size _lastMeasureGiven = new(-1, -1);
    private Size _lastArrangeGiven = new(-1, -1);
    private static int s_gridMeasureSizeChanges;
    private static int s_gridArrangeSizeChanges;

    /// <summary>진단(dock.trace): 첫 열기 뒤 마지막 측정 때 받은 크기(없으면 -1,-1). 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
    public Size DiagLastMeasureGiven => _lastMeasureGiven;
#endif

    private Size MeasureLater(Size availableSize, Func<Size, Size> baseMeasure)
    {
#if PD_DIAGNOSTICS
        var t = PerfProbe.Current.NowMs;
        var size = baseMeasure(availableSize);
        LayoutRecord("measure", PerfProbe.Current.NowMs - t, availableSize, ref _lastMeasureGiven, ref s_gridMeasureSizeChanges);
        return size;
#else
        return baseMeasure(availableSize);
#endif
    }

    private Size ArrangeLater(Size finalSize, Func<Size, Size> baseArrange)
    {
#if PD_DIAGNOSTICS
        var t = PerfProbe.Current.NowMs;
        var size = baseArrange(finalSize);
        LayoutRecord("arrange", PerfProbe.Current.NowMs - t, finalSize, ref _lastArrangeGiven, ref s_gridArrangeSizeChanges);
        return size;
#else
        return baseArrange(finalSize);
#endif
    }

#if PD_DIAGNOSTICS
    private static void LayoutRecord(string phase, double ms, Size given, ref Size last, ref int changes)
    {
        PerfProbe.Current.RecordOperation("grid." + phase, ms, ms);
        if (last.Width >= 0 && !last.Equals(given))
        {
            PerfProbe.Current.SetMetric($"grid_{phase}_sizechg", ++changes);
            PerfProbe.Current.SetText($"grid_{phase}_lastsize", $"{last.Width:0.###}x{last.Height:0.###}->{given.Width:0.###}x{given.Height:0.###}"); // 소수 배율 반올림 흔들림까지 보이게
        }

        last = given;
    }
#endif
}
