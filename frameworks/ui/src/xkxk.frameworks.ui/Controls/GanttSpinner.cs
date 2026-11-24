// 간트 스피너 — 막대 4개가 위에서 아래로 하나씩 그려지는 진행 표시. 보이는 동안만 렌더 스레드 애니메이션을 돌리고 입력을 막지 않는다
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using xkxk.frameworks.ui.Motion;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 한 주기 1.6초(그리기 1.2초 + 사라짐 0.4초)를 반복한다. 움직임 줄이기가 켜져 있으면 구동 없이 막대 4개가 완성된 정지 프레임을 그린다.
/// 막대 색은 <see cref="BarBrush"/> 이고 기본값은 PdUiStyles.axaml 의 테마 토큰이다.
/// 구동은 <see cref="GanttSpinnerVisual"/> 이 렌더 스레드에서 맡고, 이 컨트롤은 정지 프레임만 직접 그린다.
/// </summary>
public sealed class GanttSpinner : Control
{
    /// <summary>막대 개수.</summary>
    public const int BarCount = 4;

    private const double DrawSeconds = 1.2;
    private const double FadeSeconds = 0.4;
    private static readonly double[] BarStart = { 0.00, 0.15, 0.30, 0.45 };
    private static readonly double[] BarLength = { 0.60, 0.55, 0.50, 0.45 };

    /// <summary>막대 색.</summary>
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<GanttSpinner, IBrush?>(nameof(BarBrush));

    /// <summary>값이 있으면 구동을 멈추고 그 그리기 진행도(0~1)로 고정한다. 시험·진단용.</summary>
    public static readonly StyledProperty<double?> FrozenProgressProperty =
        AvaloniaProperty.Register<GanttSpinner, double?>(nameof(FrozenProgress));

    private CompositionCustomVisual? _visual;
    private bool _reduced;

    static GanttSpinner()
    {
        AffectsRender<GanttSpinner>(BarBrushProperty, FrozenProgressProperty);
    }

    public GanttSpinner()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>지금 구동 중인 스피너 수. busy 가 아니면 0 이어야 한다(진단 카운터).</summary>
    public static int RunningTimerCount { get; private set; }

    /// <summary>렌더 스레드가 그린 누적 프레임 수. UI 스레드를 막은 전후 차이로 애니메이션 지속을 판정한다(진단 카운터).</summary>
    public static long RenderThreadFrames => GanttSpinnerVisual.FramesDrawn;

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public double? FrozenProgress
    {
        get => GetValue(FrozenProgressProperty);
        set => SetValue(FrozenProgressProperty, value);
    }

    /// <summary>켜질 때 렌더 스레드에서 기다릴 표시 지연(ms). UI 스레드가 막혀도 지연 뒤에 나타난다. 켜기 전에 정한다.</summary>
    public int ShowDelayMs { get; set; }

    /// <summary>이 스피너가 구동 중인가. 움직임 줄이기 정지 프레임은 구동이 아니다.</summary>
    public bool IsRunning => _visual is not null && !_reduced;

    /// <summary>움직임 줄이기로 정지 프레임을 그리고 있는가.</summary>
    public bool IsStaticFrame => _reduced;

    /// <summary>막대 i 가 그리기 진행도 t(0~1)에서 그려진 비율(0~1). 막대는 위에서부터 t 를 4등분한 구간에 차례로 그려진다.</summary>
    public static double BarProgress(int barIndex, double t) => Math.Clamp((t - (double)barIndex / BarCount) * BarCount, 0, 1);

    /// <summary>size×size 영역에서 진행도 t 의 막대 i 사각형. 아직 시작하지 않은 막대는 null.</summary>
    public static Rect? BarRect(int barIndex, double t, double size)
    {
        double width = BarLength[barIndex] * size * BarProgress(barIndex, t);
        if (width <= 0)
        {
            return null;
        }

        double pitch = size / BarCount;
        double height = pitch * 0.6;
        return new Rect(BarStart[barIndex] * size, barIndex * pitch + (pitch - height) / 2, width, height);
    }

    /// <summary>시작 후 경과 초의 (그리기 진행도, 투명도).</summary>
    public static (double T, double Alpha) FrameAt(double elapsedSeconds)
    {
        double elapsed = elapsedSeconds % (DrawSeconds + FadeSeconds);
        return elapsed < DrawSeconds ? (elapsed / DrawSeconds, 1) : (1, 1 - (elapsed - DrawSeconds) / FadeSeconds);
    }

    /// <summary>그리기 진행도 t 의 막대들을 size×size 영역에 그린다. 정지 프레임·커서 프레임·진단 렌더가 쓴다.</summary>
    public static void DrawBars(DrawingContext context, double size, double t, IBrush brush)
    {
        for (int i = 0; i < BarCount; i++)
        {
            if (BarRect(i, t, size) is { } rect)
            {
                context.DrawRectangle(brush, null, rect, rect.Height / 4, rect.Height / 4);
            }
        }
    }

    /// <summary>그리기 진행도 t 의 프레임을 비트맵으로 만든다. 호출자가 Dispose 한다.</summary>
    public static RenderTargetBitmap RenderFrame(double t, int pixelSize, IBrush brush)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(pixelSize, pixelSize));
        using (var context = bitmap.CreateDrawingContext())
        {
            DrawBars(context, pixelSize, t, brush);
        }

        return bitmap;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_visual is not null || !IsVisible || BarBrush is not { } brush)
        {
            return; // 구동 중에는 처리기가 그린다
        }

        double size = Math.Min(Bounds.Width, Bounds.Height);
        using (context.PushTransform(Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2)))
        {
            DrawBars(context, size, Math.Clamp(FrozenProgress ?? 1, 0, 1), brush);
        }
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
        if (change.Property == IsVisibleProperty || change.Property == FrozenProgressProperty)
        {
            UpdateRunning();
        }
        else if (change.Property == BarBrushProperty)
        {
            _visual?.SendHandlerMessage(new GanttSpinnerMessage.SetBrush(BarBrush?.ToImmutable()));
        }
        else if (change.Property == BoundsProperty && _visual is { } visual)
        {
            visual.Size = new Vector(Bounds.Width, Bounds.Height);
            visual.SendHandlerMessage(new GanttSpinnerMessage.SetSize(Bounds.Size));
        }
    }

    private void UpdateRunning()
    {
        StopVisual();
        _reduced = false;
        if (!IsVisible || VisualRoot is null || FrozenProgress is not null)
        {
            InvalidateVisual();
            return;
        }

        _reduced = MotionPreference.Reduced;
        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
        {
            InvalidateVisual(); // 컴포지터가 없으면 Render 가 정지 프레임을 그린다
            return;
        }

        _visual = compositor.CreateCustomVisual(new GanttSpinnerVisual());
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.SendHandlerMessage(new GanttSpinnerMessage.Start(BarBrush?.ToImmutable(), Bounds.Size, ShowDelayMs / 1000.0, _reduced));
        if (!_reduced)
        {
            RunningTimerCount++;
        }

        InvalidateVisual();
    }

    private void StopVisual()
    {
        if (_visual is null)
        {
            return;
        }

        _visual.SendHandlerMessage(new GanttSpinnerMessage.Stop());
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        if (!_reduced)
        {
            RunningTimerCount--;
        }
    }
}
