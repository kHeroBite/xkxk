// 배경 이미지 적용 시간 계측(진단 빌드 전용, PERF-15) — 읽기 시작 → 적용 뒤 첫 프레임을 ui.background.apply.firstframe 로 기록
#if PD_DIAGNOSTICS
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class DesignEffectsApplier
{
    /// <summary>측정 이름(설정 키 아님 — plt_2 §12.1).</summary>
    private const string ImageApplyMetric = "ui.background.apply.firstframe";

    private static double? _imageStartMs;

    static partial void MarkImageLoadStarted() => _imageStartMs = PerfProbe.Current.NowMs;

    static partial void MarkImageLoadFailed() => _imageStartMs = null; // 실패한 읽기는 적용 시간이 아니다

    static partial void MarkImageApplied(Application app)
    {
        if (_imageStartMs is not { } start)
        {
            return;
        }

        _imageStartMs = null;
        var applied = PerfProbe.Current.NowMs - start;
        if (Roots(app).OfType<TopLevel>().FirstOrDefault() is not { } top)
        {
            return; // 그릴 창이 없으면 첫 프레임도 없다 — 값을 만들지 않는다
        }

        // 두 번째 애니메이션 프레임 = 첫 프레임이 실제로 그려진 다음(PerfOpenTracker 와 같은 정의)
        top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ =>
            PerfProbe.Current.RecordOperation(ImageApplyMetric, applied, PerfProbe.Current.NowMs - start)));
    }
}
#endif
