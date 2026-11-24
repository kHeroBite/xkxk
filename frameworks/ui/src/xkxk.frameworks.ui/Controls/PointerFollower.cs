// 포인터 추적 스피너 — 움직이는 커서를 쓸 수 없는 웹·마우스 연결 태블릿에서 창 안의 마우스 포인터를 따라다닌다
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 창 전체를 덮는 칸의 마지막 자식으로 둔다. <see cref="IsActive"/> 가 참인 동안만 포인터 이벤트를 구독하고, 마우스 포인터일 때만 보인다(터치는 숨김).
/// 창 밖으로는 그릴 수 없다. 입력을 막지 않는다.
/// </summary>
public sealed class PointerFollower : Decorator
{
    /// <summary>스피너 크기(dip).</summary>
    public const double SpinnerSize = 16;

    private const double Offset = 14; // 포인터 끝을 가리지 않게 오른쪽 아래로 띄운다

    /// <summary>추적을 켤지. 상위 층이 busy 상태로 정한다.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PointerFollower, bool>(nameof(IsActive));

    private TopLevel? _host;
    private GanttSpinner? _spinner;
    private TranslateTransform? _translate;

    private static event Action? s_hostPointerLeft;

    /// <summary>호스트가 포인터 떠남을 알린다. 웹은 Avalonia 가 페이지 떠남을 받지 못해(PointerExited 없음) 호스트가 직접 부른다. 돌아오면 다음 이동이 다시 보이게 한다.</summary>
    public static void NotifyPointerLeftHost() => s_hostPointerLeft?.Invoke();

    public PointerFollower()
    {
        IsHitTestVisible = false;
        Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>스피너가 지금 포인터 옆에 보이는가. 진단용.</summary>
    public bool IsFollowing => _spinner?.IsVisible == true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            UpdateSubscription();
        }
    }

    private void UpdateSubscription()
    {
        Unsubscribe();
        if (!IsActive || TopLevel.GetTopLevel(this) is not { } host)
        {
            return;
        }

        _host = host;
        // 다른 컨트롤이 처리한 이동도 따라가야 하므로 터널 단계에서 처리된 이벤트까지 받는다
        host.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Direct, handledEventsToo: true);
        s_hostPointerLeft += OnHostPointerLeft;
    }

    private void Unsubscribe()
    {
        if (_host is not null)
        {
            _host.RemoveHandler(PointerMovedEvent, OnPointerMoved);
            _host.RemoveHandler(PointerExitedEvent, OnPointerExited);
            s_hostPointerLeft -= OnHostPointerLeft;
            _host = null;
        }

        SetSpinnerVisible(false);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse)
        {
            SetSpinnerVisible(false);
            return;
        }

        EnsureSpinner();
        var position = e.GetPosition(this);
        _translate!.X = position.X + Offset;
        _translate.Y = position.Y + Offset;
        SetSpinnerVisible(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => SetSpinnerVisible(false);

    private void OnHostPointerLeft() => SetSpinnerVisible(false);

    private void EnsureSpinner()
    {
        if (_spinner is not null)
        {
            return;
        }

        _translate = new TranslateTransform();
        _spinner = new GanttSpinner
        {
            Width = SpinnerSize,
            Height = SpinnerSize,
            Opacity = BusyOverlay.SpinnerOpacity,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _translate,
            RenderTransformOrigin = RelativePoint.TopLeft,
            IsVisible = false,
        };
        Child = _spinner;
    }

    private void SetSpinnerVisible(bool visible)
    {
        if (_spinner is not null)
        {
            _spinner.IsVisible = visible;
        }
    }
}
