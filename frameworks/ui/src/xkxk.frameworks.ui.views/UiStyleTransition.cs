// 화면 방식 전환 시 메인 콘텐츠를 페이드(나감→교체→들어옴)로 바꿔 주는 UI 레이어 전환 효과
using xkxk.frameworks.ui.shell;
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.Motion;
using MotionPreference = xkxk.frameworks.ui.Motion.MotionPreference;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// UiStyleViewModel 의 전환 알림을 받아 대상 컨트롤의 불투명도 1→0.15(120ms)·6px 미끄럼 → (VM 이 형 교체) → 0.15→1(180ms) 로 약 300ms 전환한다.
/// 규칙 판단은 하지 않는다(어떤 형으로 바꿀지는 컨트롤 레이어 몫). 애니메이션 줄이기면 대기 없이 즉시 교체한다.
/// 중첩 방지는 VM 의 직렬 처리(마지막 요청 1개만 대기)를 따르고, 여기서는 진행 중 애니메이션을 취소·복원만 한다.
/// </summary>
public sealed class UiStyleTransition : IDisposable
{
    public const int OutMs = 120;
    public const int InMs = 180;
    private const double DimOpacity = 0.15;
    private const double SlidePx = 6;

    private readonly Control _target;
    private readonly UiStyleViewModel _style;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private bool _animated;
    private int _totalMs;

    public UiStyleTransition(Control target, UiStyleViewModel style, Action<string> log)
    {
        _target = target;
        _style = style;
        _log = log;
        _style.TransitionOut = FadeOutAsync;
        _style.StyleChanged += OnChanged;
        _style.IsInteractionBusy = IsBusy;
    }

    /// <summary>마지막 전환에서 실제 애니메이션을 했는지(진단 lastTransition.animated).</summary>
    public bool LastAnimated => _animated;

    /// <summary>마지막 전환의 애니메이션 합계 시간(ms, 줄이기면 0).</summary>
    public int LastMs => _totalMs;

    private async Task FadeOutAsync()
    {
        CancelPending();
        if (MotionPreference.Reduced)
        {
            _animated = false;
            _totalMs = 0;
            _log("ui.style.transition animated=false ms=0 reason=reduced-motion");
            return;
        }

        var cts = _cts = new CancellationTokenSource();
        _animated = true;
        _totalMs = OutMs + InMs;
        Prepare(OutMs);
        _target.Opacity = DimOpacity;
        _target.RenderTransform = TransformOperations.Parse($"translateY({SlidePx}px)");
        await DelayAsync(OutMs, cts.Token); // 페이드아웃이 끝난 뒤 VM 이 형을 교체한다
    }

    private void OnChanged(UiStyle from, UiStyle to)
    {
        if (!_animated)
        {
            Restore();
            return;
        }

        var cts = _cts;
        Prepare(InMs);
        _target.Opacity = 1;
        _target.RenderTransform = TransformOperations.Parse("translateY(0px)");
        _log($"ui.style.transition animated=true ms={_totalMs} {from}->{to}");
        _ = FinishAsync(cts?.Token ?? CancellationToken.None);
    }

    private async Task FinishAsync(CancellationToken ct)
    {
        await DelayAsync(InMs, ct);
        if (!ct.IsCancellationRequested)
        {
            Restore();
        }
    }

    private void Prepare(int ms)
    {
        var d = TimeSpan.FromMilliseconds(ms);
        _target.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = d, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = d, Easing = new CubicEaseOut() },
        };
    }

    private void Restore()
    {
        _target.Transitions = null;
        _target.Opacity = 1;
        _target.RenderTransform = null;
    }

    private void CancelPending()
    {
        _cts?.Cancel();
        _cts = null;
        Restore();
    }

    private static async Task DelayAsync(int ms, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ms, ct);
        }
        catch (TaskCanceledException)
        {
            // 창 닫기·앱 종료·새 전환으로 취소 — 예외 없이 끝낸다
        }
    }

    /// <summary>끌기·모달 중이면 true. 이 Avalonia 버전의 TextBox 에는 IME 조합 상태(PreeditText)가 없어 IME 판정은 하지 않는다(입력 텍스트는 전환으로 지워지지 않음).</summary>
    private bool IsBusy()
    {
        return false; // 독립 창(SDI)도 소유 창일 수 있어 소유 창 유무로는 모달을 구분하지 못한다 — 끌기·모달 판정은 추후 확장
    }

    public void Dispose()
    {
        _style.StyleChanged -= OnChanged;
        _style.TransitionOut = null;
        _style.IsInteractionBusy = null;
        CancelPending();
    }
}
