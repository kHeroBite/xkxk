// 문서 뷰를 창 크기에 맞춰 늘린 채 배율만 적용하는 래퍼(UI-06) — LayoutTransformControl 대체(가운데 정렬·미확장 결함 해소)
using Pd.Poc.Shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
#if PD_DIAGNOSTICS
using Pd.Poc.Shell.Diagnostics;
#endif

namespace Pd.Ui.Views;

/// <summary>
/// 자식을 (받은 크기 ÷ 배율)로 측정·배치해 공간을 꽉 채우게 한 뒤 RenderTransform(Scale, 좌상단 기준)으로 키운다.
/// 결과적으로 배율 z 에서 자식은 창 크기의 1/z 영역에 레이아웃되고 화면에서는 창을 정확히 채운다.
/// 배율 1 이면 변환 없이 일반 Decorator 와 같다. Tag 에 문서를 담아 Ctrl+휠 대상 찾기에 쓴다.
/// </summary>
public sealed class ZoomDecorator : Decorator, Pd.Poc.Shell.Docking.IDocumentZoomHost
{
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ZoomDecorator, double>(nameof(Zoom), 1.0);

    static ZoomDecorator()
    {
        AffectsMeasure<ZoomDecorator>(ZoomProperty);
    }

    public ZoomDecorator()
    {
        ClipToBounds = true;
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private double SafeZoom => Zoom > 0.01 ? Zoom : 1.0;

    private Size _lastArrange;      // 마지막 실제 배치 크기(0 = 아직 없음)
    private Size _lastChildMeasure; // 마지막으로 자식에게 준 측정 크기(배치 크기와 같으면 재측정 생략)
    private Size _lastAvailable = new(-1, -1);     // 마지막 Measure 때 부모가 준 크기
    private Size _availableAtArrange = new(-1, -1); // 마지막 Arrange 직전 Measure 때 부모가 준 크기(그 제약에도 더 크게 배치됐는지 판정)

#if PD_DIAGNOSTICS
    // D-PF2 R1 진단 계수기(진단 빌드 전용, lead 승인) — 모든 인스턴스 합계 누적값. perf.get metrics 의 zoom_* 를 끌기 앞뒤 차분으로 읽는다. 동작은 바꾸지 않는다
    private static long s_diagMeasureCalls;
    private static long s_diagSizeChanged;
    private static long s_diagArrangeCalls;
    private Size _diagLastDesired;
#endif

    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        PerfProbe.Current.SetMetric("zoom_measure_calls", ++s_diagMeasureCalls);
#endif
        if (Child is null)
        {
            return default;
        }

        // Dock MdiLayoutPanel 은 MDI 창을 문서 영역 전체 폭으로 Measure 하고 MdiBounds(더 좁음)로 Arrange 한다(odev-3 확인,
        // Dock MdiLayoutPanel.cs). 그러면 접히는 줄(WrapPanel·TextWrapping)이 1줄 높이로 잡혀 겹친다(RETEST 2차 UI-06 200%).
        // 그래서 지난 실제 배치 크기가 받은 크기보다 작으면 그 크기로 자식을 잰다(Arrange 의 재측정과 같은 크기 → 측정 캐시로 끝나 반복 없음).
        // Q4(Z1): 받은 크기가 지난 배치 크기보다 '조금' 작을 때(Android 배율 2.25 에서 문서 영역 측정 제약이 배치보다 1.111 DIP 작음 — otest-m-1 i1222 grid.mtrace)
        // min 을 쓰면 측정은 작은 크기, Arrange 의 재측정은 배치 크기로 자식을 번갈아 재며 한 패스에 약 10쌍 왕복했다(그리드 DesiredSize 변화 → 조상 측정 무효화).
        // 축별 차이가 허용치 미만이면 지난 배치 크기로 재서 Measure·Arrange 가 같은 크기를 쓰게 한다(Arrange 재측정 생략 → 왕복 없음). 큰 차이는 위 MDI 규칙(min) 그대로.
        // 그리드 순환(odebug-grid): 부모가 같은 제약(예: 높이 354)으로 재 놓고 그보다 큰 크기(363)로 배치하기를 되풀이하면
        // 측정은 354·Arrange 재측정은 363 으로 자식을 번갈아 재며 한 패스가 10회 반복 한도까지 돈다(perf_split n1: 그리드 측정 크기 변경 9634회,
        // 'Layout cycle detected' 15421줄, 정렬 1회 layout1 p50 161ms). 지난 배치 직전과 같은 제약이면 지난 배치 크기로 잰다(Hold).
        var w = Pick(availableSize.Width, _lastArrange.Width, _availableAtArrange.Width);
        var h = Pick(availableSize.Height, _lastArrange.Height, _availableAtArrange.Height);
        _lastAvailable = availableSize;
        (w, h) = CapFirstMeasure(w, h); // 첫 측정만 MDI 창 크기로 상한(아래 CapFirstMeasure)
        var z = SafeZoom;
        _lastChildMeasure = new Size(w / z, h / z);
        Child.Measure(_lastChildMeasure);
        var d = Child.DesiredSize;
        var result = new Size(Math.Min(d.Width * z, availableSize.Width), Math.Min(d.Height * z, availableSize.Height));
#if PD_DIAGNOSTICS
        if (!result.Equals(_diagLastDesired))
        {
            _diagLastDesired = result;
            PerfProbe.Current.SetMetric("zoom_measure_size_changed", ++s_diagSizeChanged);
        }
#endif
        return result;
    }

#if PD_DIAGNOSTICS
    /// <summary>진단판 대조 스위치 --grid-first-measure off(기본 켬). 끄면 첫 측정 상한이 없던 예전 동작으로 돌아간다. 제품판에는 스위치가 없다.</summary>
    private static bool? s_capFirstMeasure;

    private static long s_diagFirstMeasureCapped;

    private static bool CapFirstMeasureEnabled => s_capFirstMeasure ??= ReadCapSwitch();

    private static bool ReadCapSwitch()
    {
        var a = App.StartupArgs;
        var off = false;
        for (var i = 0; i < a.Count; i++)
        {
            off |= a[i].Equals("--grid-first-measure=off", StringComparison.OrdinalIgnoreCase)
                || (a[i].Equals("--grid-first-measure", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count && a[i + 1].Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        PerfProbe.Current.SetText("grid_first_measure", off ? "off" : "cap");
        return !off;
    }
#else
    private static bool CapFirstMeasureEnabled => true;
#endif

    /// <summary>
    /// 첫 측정 상한(odev-perf Q2·Q5): MDI 창 안 문서의 첫 측정(지난 배치 없음)은 Dock MdiLayoutPanel 이 준 문서 영역 전체 크기로 잰다.
    /// 실측으로는 layout.load 때 그리드마다 906x559 로 잰 뒤 542x321 로 배치해 다시 쟀다. 그래서 첫 측정만 문서 MdiBounds 크기를 상한으로 쓴다
    /// (w4 진단판 5회 교차: 그리드 첫 Measure −39%, 최종 배치 크기·화면 동일 — logs/odev_perf/result_odev_perf.md §4).
    /// 배치(ArrangeOverride)는 언제나 실제 크기로 다시 재므로 최종 배치 크기는 같다. 보통 상태 MDI 창 안일 때만 적용한다.
    /// </summary>
    private (double W, double H) CapFirstMeasure(double w, double h)
    {
        if (!CapFirstMeasureEnabled || _lastArrange.Width > 0 || _lastArrange.Height > 0
            || Tag is not Pd.Poc.Shell.Docking.PocDocument doc || doc.MdiState != Dock.Model.Core.MdiWindowState.Normal
            || doc.MdiBounds.Width <= 0 || doc.MdiBounds.Height <= 0
            || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Dock.Avalonia.Controls.MdiDocumentWindow>(this) is null)
        {
            return (w, h);
        }

#if PD_DIAGNOSTICS
        PerfProbe.Current.SetMetric("zoom_first_measure_capped", ++s_diagFirstMeasureCapped);
#endif
        return (Math.Min(w, doc.MdiBounds.Width), Math.Min(h, doc.MdiBounds.Height));
    }

    /// <summary>측정·배치 크기 차이 허용치(DIP). 실측 1.111 DIP(2.5px@2.25)보다 크고, 사용자가 느낄 만한 크기 변경보다 작게.</summary>
    private const double SnapTolerance = 1.5;

    /// <summary>
    /// 한 축의 자식 측정 크기: 지난 배치 없음 → 받은 크기, 차이 허용치 미만 → 지난 배치 크기,
    /// 받은 크기가 지난 배치보다 작지만 지난 배치 직전에도 같은 크기를 받고서 더 크게 배치됐음 → 지난 배치 크기(Hold, 측정·배치 왕복 차단),
    /// 그 밖 → 둘 중 작은 값. 처음 보는 작은 제약(진짜 축소)은 그대로 작은 값으로 재므로 축소 반영은 바뀌지 않는다.
    /// </summary>
    private static double Pick(double available, double lastArrange, double availableAtArrange)
    {
        if (lastArrange <= 0) { return available; }
        if (!double.IsInfinity(available) && Math.Abs(available - lastArrange) < SnapTolerance) { return lastArrange; }
        if (HoldEnabled && available < lastArrange && Math.Abs(available - availableAtArrange) < 0.01)
        {
#if PD_DIAGNOSTICS
            PerfProbe.Current.SetMetric("zoom_measure_hold", ++s_diagHold);
#endif
            return lastArrange;
        }

        return Math.Min(available, lastArrange);
    }

#if PD_DIAGNOSTICS
    private static long s_diagHold;
    private static bool? s_hold;

    /// <summary>진단판 대조 스위치 --zoom-measure-hold off(기본 켬). 끄면 Hold 없던 예전 Pick 으로 돌아간다(같은 빌드 A/B). 제품판에는 스위치가 없다.</summary>
    private static bool HoldEnabled => s_hold ??= ReadHoldSwitch();

    private static bool ReadHoldSwitch()
    {
        var a = App.StartupArgs;
        var off = false;
        for (var i = 0; i < a.Count; i++)
        {
            off |= a[i].Equals("--zoom-measure-hold=off", StringComparison.OrdinalIgnoreCase)
                || (a[i].Equals("--zoom-measure-hold", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count && a[i + 1].Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        PerfProbe.Current.SetText("zoom_measure_hold_mode", off ? "off" : "on");
        return !off;
    }
#else
    private const bool HoldEnabled = true;
#endif

    protected override Size ArrangeOverride(Size finalSize)
    {
#if PD_DIAGNOSTICS
        PerfProbe.Current.SetMetric("zoom_arrange_calls", ++s_diagArrangeCalls);
#endif
        if (Child is null)
        {
            return finalSize;
        }

        var z = SafeZoom;
        var arrangeSize = new Size(finalSize.Width / z, finalSize.Height / z);

        // 실제 배치 크기가 마지막 측정 크기와 다를 때만 그 크기로 자식을 바로 다시 잰 뒤 배치한다
        // (InvalidateMeasure 만 걸면 다음 패스까지 반영이 늦었다 — odev-3 진단). 같으면 건너뛴다(20창 이동처럼 크기가 그대로면 비용 0).
        // 재측정으로 자식 DesiredSize 가 바뀌면 Avalonia 가 래퍼 측정을 1회 무효화하지만, 다음 Measure 는 Pick(받은 크기, _lastArrange)
        // = 같은 크기로 재므로 _lastChildMeasure 와 같아져 여기서 다시 재지 않는다 → 반복 루프 없음(크기 변경당 추가 패스 최대 1회).
        _lastArrange = finalSize;
        _availableAtArrange = _lastAvailable; // 이 배치가 어떤 측정 제약 뒤에 왔는지(Pick 의 Hold 판정)
#if !PD_ZOOM_NO_REMEASURE // 회귀 대조 빌드 전용(odev-3, -p:PdZoomNoRemeasure=true) — 기본 동작은 즉시 재측정
        if (!_lastChildMeasure.Equals(arrangeSize))
        {
            _lastChildMeasure = arrangeSize;
            Child.Measure(arrangeSize);
        }
#endif

        Child.Arrange(new Rect(arrangeSize));
        if (Math.Abs(z - 1.0) < 0.001)
        {
            Child.RenderTransform = null;
        }
        else
        {
            Child.RenderTransformOrigin = RelativePoint.TopLeft;
            Child.RenderTransform = new ScaleTransform(z, z);
        }

        return finalSize;
    }
}
