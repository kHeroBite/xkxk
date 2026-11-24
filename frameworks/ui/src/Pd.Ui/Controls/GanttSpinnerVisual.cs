// 간트 스피너 컴포지션 처리기 — 렌더 스레드에서 프레임을 그려 UI 스레드가 막혀도 애니메이션이 이어지게 한다
using System;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Pd.Ui.Controls;

/// <summary>UI 스레드가 처리기로 보내는 메시지. 시작·정지·색·크기.</summary>
internal abstract record GanttSpinnerMessage
{
    internal sealed record Start(IImmutableBrush? Brush, Size Size, double DelaySeconds, bool IsStatic) : GanttSpinnerMessage;

    internal sealed record Stop : GanttSpinnerMessage;

    internal sealed record SetBrush(IImmutableBrush? Brush) : GanttSpinnerMessage;

    internal sealed record SetSize(Size Size) : GanttSpinnerMessage;
}

/// <summary>
/// 메시지 처리와 그리기는 모두 렌더 스레드에서 돈다(웹처럼 렌더 스레드가 없는 플랫폼은 UI 스레드). Start 와 Stop 사이에만 다음 프레임을 예약하므로 정지하면 구동이 0 이 된다.
/// 메시지는 UI 스레드의 커밋으로 전달되므로, UI 스레드가 막히기 전에 Start 가 커밋돼야 한다.
/// 표시 지연도 여기서 센다. 정지 프레임(움직임 줄이기)은 지연이 지나면 한 번 그린 뒤 프레임 예약을 멈춘다.
/// </summary>
internal sealed class GanttSpinnerVisual : CompositionCustomVisualHandler
{
    private static long s_framesDrawn;

    private IImmutableBrush? _brush;
    private Size _size;
    private TimeSpan _startedAt;
    private double _delaySeconds;
    private bool _isStatic;
    private bool _running;

    /// <summary>프로세스 전체에서 렌더 스레드가 그린 프레임 수. UI 스레드 차단 전후 차이로 애니메이션 지속을 판정한다.</summary>
    internal static long FramesDrawn => Interlocked.Read(ref s_framesDrawn);

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case GanttSpinnerMessage.Start start:
                _brush = start.Brush;
                _size = start.Size;
                _startedAt = CompositionNow;
                _delaySeconds = start.DelaySeconds;
                _isStatic = start.IsStatic;
                _running = true;
                RegisterForNextAnimationFrameUpdate();
                Invalidate();
                break;
            case GanttSpinnerMessage.Stop:
                _running = false;
                Invalidate();
                break;
            case GanttSpinnerMessage.SetBrush setBrush:
                _brush = setBrush.Brush;
                Invalidate();
                break;
            case GanttSpinnerMessage.SetSize setSize:
                _size = setSize.Size;
                Invalidate();
                break;
        }
    }

    public override void OnAnimationFrameUpdate()
    {
        if (!_running)
        {
            return;
        }

        Invalidate();
        if (!_isStatic || ShownSeconds() < 0)
        {
            RegisterForNextAnimationFrameUpdate();
        }
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        double shown = ShownSeconds();
        if (!_running || shown < 0 || _brush is not { } brush)
        {
            return;
        }

        var (t, alpha) = _isStatic ? (1d, 1d) : GanttSpinner.FrameAt(shown);
        double size = Math.Min(_size.Width, _size.Height);
        var origin = new Point((_size.Width - size) / 2, (_size.Height - size) / 2);
        using (context.PushOpacity(alpha, new Rect(_size)))
        {
            for (int i = 0; i < GanttSpinner.BarCount; i++)
            {
                if (GanttSpinner.BarRect(i, t, size) is { } rect)
                {
                    context.FillRectangle(brush, rect.Translate(origin), (float)(rect.Height / 4));
                }
            }
        }

        Interlocked.Increment(ref s_framesDrawn);
    }

    /// <summary>표시 지연이 끝난 뒤 지난 초. 지연 중이면 음수.</summary>
    private double ShownSeconds() => (CompositionNow - _startedAt).TotalSeconds - _delaySeconds;
}
