// SDI 호스트 창의 배치 계산(비전경 원점·요구 좌표 소비)과 생성 비용 계측을 모은 정적 도우미
using Avalonia;
using Avalonia.Controls;
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell.Diagnostics;
#endif

namespace xkxk.frameworks.ui.desktop;

/// <summary>SdiWindowHost 에서 분리한 순수 계산부. 상태(s_placement·요구 좌표)는 호출측이 보유하고 인자로 넘긴다.</summary>
internal static class SdiHostPlacement
{
    private const int OffscreenGap = 200;
    private const int Cascade = 40;

    /// <summary>지정 좌표 또는 모든 모니터 합집합의 오른쪽 바깥 좌표(+계단 오프셋).</summary>
    public static PixelPoint ResolveOrigin(BgPlacement? placement, Window window, int index)
    {
        var offset = Cascade * (index % 10);
        if (placement is { Auto: false } fixedPos)
        {
            return new PixelPoint(fixedPos.X + offset, fixedPos.Y + offset);
        }

        var screens = window.Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            return new PixelPoint(20000 + offset, offset);
        }

        var right = screens.Max(s => s.Bounds.Right);
        var top = screens.Min(s => s.Bounds.Y);
        return new PixelPoint(right + OffscreenGap + offset, top + offset);
    }

    /// <summary>표시 전 요구 좌표를 1회 소비한다. 없으면 null → 현재 Position 으로 판정.</summary>
    public static PixelPoint? TakeRequested(ref PixelPoint? requested)
    {
        var value = requested;
        requested = null;
        return value;
    }

#if PD_DIAGNOSTICS
    /// <summary>
    /// SDI 호스트 창 생성 비용 계측(시험 빌드 전용). OnOpened 에서 호출. operations:
    /// sdi.host.open (sync = 생성자→OnOpened, frame = 생성자→첫 프레임),
    /// sdi.host.firstframe (sync = frame = OnOpened→첫 프레임). 셸 ui.sdi.float 와 대조해 Dock 경로 비용을 가른다.
    /// </summary>
    public static void TrackOpenCost(Window window, double ctorMs)
    {
        var opened = PerfProbe.Current.NowMs;
        window.RequestAnimationFrame(_ =>
        {
            var first = PerfProbe.Current.NowMs;
            PerfProbe.Current.RecordOperation("sdi.host.open", opened - ctorMs, first - ctorMs);
            PerfProbe.Current.RecordOperation("sdi.host.firstframe", first - opened, first - opened);
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi.host '{window.Title}' ctor→opened={opened - ctorMs:F1}ms opened→firstframe={first - opened:F1}ms");
        });
    }
#endif
}
