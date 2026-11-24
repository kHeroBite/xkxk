// 창 아이콘 자리의 작은 회전 스피너 — 컨트롤과 렌더 스레드 컴포지션 처리기(보이는 동안만 프레임 예약, plan/09_화면설계/23 §4)
using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 점 8개가 도는 스피너. 구동은 <see cref="IconSpinnerVisual"/> 이 렌더 스레드에서 맡고 UI 스레드는 켜기·끄기·색·크기만 보낸다.
/// 시작이 커밋되기 전에 UI 스레드가 막히면 나타나지 않는다(GanttSpinner 와 같은 제약).
/// </summary>
public sealed class IconSpinner : Control
{
    /// <summary>점 색.</summary>
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<IconSpinner, IBrush?>(nameof(Brush));

    private CompositionCustomVisual? _visual;

    public IconSpinner()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>지금 구동 중인 스피너 수. 불러오는 창이 없으면 0 이어야 한다(진단 카운터).</summary>
    public static int RunningCount { get; private set; }

    /// <summary>렌더 스레드가 그린 누적 프레임 수(진단 카운터 — UI 스레드 차단 중 회전 판정).</summary>
    public static long RenderThreadFrames => IconSpinnerVisual.FramesDrawn;

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateRunning();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            UpdateRunning();
        }
        else if (change.Property == BrushProperty)
        {
            _visual?.SendHandlerMessage(new IconSpinnerMessage.SetBrush(Brush?.ToImmutable()));
        }
        else if (change.Property == BoundsProperty && _visual is { } visual)
        {
            visual.Size = new Vector(Bounds.Width, Bounds.Height);
            visual.SendHandlerMessage(new IconSpinnerMessage.SetSize(Bounds.Size));
        }
    }

    private void UpdateRunning()
    {
        StopVisual();
        if (!IsVisible || VisualRoot is null || ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor)
        {
            return;
        }

        _visual = compositor.CreateCustomVisual(new IconSpinnerVisual());
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.SendHandlerMessage(new IconSpinnerMessage.Start(Brush?.ToImmutable(), Bounds.Size));
        RunningCount++;
    }

    private void StopVisual()
    {
        if (_visual is null)
        {
            return;
        }

        _visual.SendHandlerMessage(new IconSpinnerMessage.Stop());
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        RunningCount--;
    }
}

/// <summary>UI 스레드가 처리기로 보내는 메시지.</summary>
internal abstract record IconSpinnerMessage
{
    internal sealed record Start(IImmutableBrush? Brush, Size Size) : IconSpinnerMessage;

    internal sealed record Stop : IconSpinnerMessage;

    internal sealed record SetBrush(IImmutableBrush? Brush) : IconSpinnerMessage;

    internal sealed record SetSize(Size Size) : IconSpinnerMessage;
}

/// <summary>메시지 처리와 그리기 모두 렌더 스레드(웹은 UI 스레드)에서 돈다. Start 와 Stop 사이에만 다음 프레임을 예약한다.</summary>
internal sealed class IconSpinnerVisual : CompositionCustomVisualHandler
{
    private const int DotCount = 8;
    private const double TurnSeconds = 0.9;
    private static long s_framesDrawn;

    private readonly IImmutableBrush?[] _dots = new IImmutableBrush?[DotCount];
    private Size _size;
    private TimeSpan _startedAt;
    private bool _running;

    internal static long FramesDrawn => Interlocked.Read(ref s_framesDrawn);

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case IconSpinnerMessage.Start start:
                SetColors(start.Brush);
                _size = start.Size;
                _startedAt = CompositionNow;
                _running = true;
                RegisterForNextAnimationFrameUpdate();
                break;
            case IconSpinnerMessage.Stop:
                _running = false;
                break;
            case IconSpinnerMessage.SetBrush setBrush:
                SetColors(setBrush.Brush);
                break;
            case IconSpinnerMessage.SetSize setSize:
                _size = setSize.Size;
                break;
        }

        Invalidate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_running)
        {
            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        double side = Math.Min(_size.Width, _size.Height);
        if (!_running || side <= 0)
        {
            return;
        }

        double turn = (CompositionNow - _startedAt).TotalSeconds / TurnSeconds % 1;
        double dot = side * 0.11;
        double orbit = side / 2 - dot;
        for (int i = 0; i < DotCount; i++)
        {
            if (_dots[i] is not { } brush)
            {
                continue;
            }

            double angle = 2 * Math.PI * (turn - (double)i / DotCount);
            var center = new Point(_size.Width / 2 + orbit * Math.Sin(angle), _size.Height / 2 - orbit * Math.Cos(angle));
            context.FillRectangle(brush, new Rect(center.X - dot, center.Y - dot, dot * 2, dot * 2), (float)dot);
        }

        Interlocked.Increment(ref s_framesDrawn);
    }

    // 꼬리로 갈수록 옅게 — 프레임마다 브러시를 만들지 않는다
    private void SetColors(IImmutableBrush? brush)
    {
        for (int i = 0; i < DotCount; i++)
        {
            _dots[i] = brush is ISolidColorBrush solid
                ? new ImmutableSolidColorBrush(solid.Color, solid.Opacity * (1 - 0.8 * i / (DotCount - 1)))
                : brush;
        }
    }
}
