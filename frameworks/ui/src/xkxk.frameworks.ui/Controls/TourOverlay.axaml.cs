// 도움말 투어 오버레이 코드비하인드(요건 6) — OverlayLayer 위에 올라가 창을 덮고 스포트라이트 이동(200ms)·맥박 링·카드 배치를 그린다. 움직임 줄이기면 전환 없이 즉시 바꾼다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using xkxk.frameworks.ui.Motion;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 상위 층이 <see cref="AttachTo"/> 로 올리고 <see cref="ShowStep"/>·<see cref="ShowShortcuts"/> 로 내용을 바꾼다. 진행 판단은 상위 층 몫이다.
/// 좌표는 오버레이 기준이다. 포인터는 여기서 끝나 아래로 내려가지 않는다.
/// </summary>
public partial class TourOverlay : UserControl
{
    /// <summary>스포트라이트·카드 이동 시간(ms).</summary>
    public const int SpotlightMoveMs = 200;

    public static readonly StyledProperty<double> SpotXProperty = AvaloniaProperty.Register<TourOverlay, double>(nameof(SpotX));
    public static readonly StyledProperty<double> SpotYProperty = AvaloniaProperty.Register<TourOverlay, double>(nameof(SpotY));
    public static readonly StyledProperty<double> SpotWidthProperty = AvaloniaProperty.Register<TourOverlay, double>(nameof(SpotWidth));
    public static readonly StyledProperty<double> SpotHeightProperty = AvaloniaProperty.Register<TourOverlay, double>(nameof(SpotHeight));

    private const double HolePadding = 4;
    private const double HoleRadius = 6;
    private const double CardWidth = 360;
    private const double LayerWidth = 640;
    private const double Edge = 12;
    private const double CardGap = 12;

    private Panel? _layer;
    private Rect? _target;
    private bool _placed;

    public TourOverlay()
    {
        InitializeComponent();
        Card.PreviousRequested += () => PreviousRequested?.Invoke();
        Card.NextRequested += () => NextRequested?.Invoke();
        Card.SkipRequested += () => SkipRequested?.Invoke();
        Card.CloseRequested += () => CloseRequested?.Invoke();
        Shortcuts.PreviousRequested += () => PreviousRequested?.Invoke();
        Shortcuts.CloseRequested += () => CloseRequested?.Invoke();
        AddHandler(PointerPressedEvent, SwallowPointer, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, SwallowPointer, RoutingStrategies.Bubble);
        AddHandler(PointerWheelChangedEvent, SwallowPointer, RoutingStrategies.Bubble);
        Card.SizeChanged += (_, _) => PlaceCard();
    }

    public event Action? PreviousRequested;

    public event Action? NextRequested;

    public event Action? SkipRequested;

    public event Action? CloseRequested;

    public double SpotX { get => GetValue(SpotXProperty); set => SetValue(SpotXProperty, value); }

    public double SpotY { get => GetValue(SpotYProperty); set => SetValue(SpotYProperty, value); }

    public double SpotWidth { get => GetValue(SpotWidthProperty); set => SetValue(SpotWidthProperty, value); }

    public double SpotHeight { get => GetValue(SpotHeightProperty); set => SetValue(SpotHeightProperty, value); }

    /// <summary>지금 전환 없이 즉시 바꾸는 중인가(움직임 줄이기, 시험용).</summary>
    public bool IsMotionReduced { get; private set; }

    /// <summary>창의 오버레이 층에 올린다. 층은 Canvas 라 크기를 직접 맞춘다.</summary>
    public void AttachTo(Panel layer)
    {
        _layer = layer;
        layer.Children.Add(this);
        layer.SizeChanged += OnLayerSizeChanged;
        FitLayer(layer.Bounds.Size);
    }

    /// <summary>층에서 내린다. 스타일 애니메이션·전환도 트리에서 빠지며 함께 멈춘다.</summary>
    public void Detach()
    {
        if (_layer is { } layer)
        {
            layer.SizeChanged -= OnLayerSizeChanged;
            layer.Children.Remove(this);
            _layer = null;
        }
    }

    /// <summary>대상 단계를 보여 준다.</summary>
    public void ShowStep(Rect target, string title, string body, int index, int total, bool canGoBack)
    {
        ApplyMotion();
        Shortcuts.IsVisible = false;
        Card.IsVisible = true;
        Card.Show(title, body, index, total, canGoBack);
        MoveSpotlight(target);
        RestartFade();
        if (_layer is not null && !Card.IsKeyboardFocusWithin)
        {
            Card.FocusPrimary();
        }
    }

    /// <summary>대상 위치만 옮긴다(창 크기 변경).</summary>
    public void MoveSpotlight(Rect target)
    {
        _target = target;
        Ring.IsVisible = true;
        var hole = target.Inflate(HolePadding);
        SpotX = hole.X;
        SpotY = hole.Y;
        SpotWidth = hole.Width;
        SpotHeight = hole.Height;
        PlaceCard();
    }

    /// <summary>마지막 단축키·기능 레이어를 보여 준다. 구멍은 닫는다.</summary>
    public void ShowShortcuts(IReadOnlyList<ShortcutRowItem> keys, IReadOnlyList<FeatureRowItem> features, string contactTitle, string contactBody, int index, int total, bool canGoBack)
    {
        ApplyMotion();
        _target = null;
        SpotWidth = 0;
        SpotHeight = 0;
        Ring.IsVisible = false;
        Card.IsVisible = false;
        Shortcuts.Show(keys, features, contactTitle, contactBody, index, total, canGoBack);
        Shortcuts.IsVisible = true;
        FitLayer(new Size(Width, Height));
        Dispatcher.UIThread.Post(() => { if (_layer is not null) { FocusPrimary(); } }, DispatcherPriority.Loaded); // 방금 보인 버튼은 배치 뒤에야 초점을 받는다
    }

    /// <summary>기본 버튼에 초점을 준다.</summary>
    public void FocusPrimary()
    {
        if (Shortcuts.IsVisible)
        {
            Shortcuts.FocusPrimary();
        }
        else
        {
            Card.FocusPrimary();
        }
    }

    /// <summary>마지막 레이어를 키로 스크롤한다.</summary>
    public void ScrollLayer(Key key) => Shortcuts.ScrollByKey(key);

    /// <summary>Tab: 보이는 활성 버튼 안에서 초점을 돌린다.</summary>
    public void MoveFocus(bool back)
    {
        var buttons = (Shortcuts.IsVisible ? Shortcuts.FocusOrder : Card.FocusOrder).Where(b => b.IsEffectivelyEnabled).ToList();
        if (buttons.Count > 0)
        {
            var index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
            var step = back ? -1 : 1;
            var next = index < 0 ? 0 : (index + step + buttons.Count) % buttons.Count;
            buttons[next].Focus(NavigationMethod.Tab);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpotXProperty || change.Property == SpotYProperty || change.Property == SpotWidthProperty || change.Property == SpotHeightProperty)
        {
            UpdateHole();
        }
    }

    private static void SwallowPointer(object? sender, RoutedEventArgs e) => e.Handled = true;

    private void OnLayerSizeChanged(object? sender, SizeChangedEventArgs e) => FitLayer(e.NewSize);

    private void FitLayer(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || double.IsNaN(size.Width) || double.IsNaN(size.Height))
        {
            return;
        }

        Width = size.Width;
        Height = size.Height;
        Card.Width = Math.Min(CardWidth, Math.Max(0, size.Width - (Edge * 2)));
        Shortcuts.Width = Math.Min(LayerWidth, Math.Max(0, size.Width - (Edge * 2)));
        Shortcuts.MaxHeight = Math.Max(0, size.Height - (Edge * 2));
        UpdateHole();
        PlaceCard();
    }

    /// <summary>움직임 줄이기는 표시할 때마다 읽어 전환·맥박을 켜고 끈다.</summary>
    private void ApplyMotion()
    {
        IsMotionReduced = MotionPreference.Reduced;
        if (IsMotionReduced)
        {
            Transitions = null;
            Card.Transitions = null;
            Ring.Classes.Remove("pulse");
            return;
        }

        if (_placed && Transitions is null)
        {
            var move = TimeSpan.FromMilliseconds(SpotlightMoveMs);
            Transitions = new Transitions
            {
                new DoubleTransition { Property = SpotXProperty, Duration = move },
                new DoubleTransition { Property = SpotYProperty, Duration = move },
                new DoubleTransition { Property = SpotWidthProperty, Duration = move },
                new DoubleTransition { Property = SpotHeightProperty, Duration = move },
            };
            Card.Transitions = new Transitions
            {
                new DoubleTransition { Property = Canvas.LeftProperty, Duration = move },
                new DoubleTransition { Property = Canvas.TopProperty, Duration = move },
            };
        }

        _placed = true;
        if (!Ring.Classes.Contains("pulse"))
        {
            Ring.Classes.Add("pulse");
        }
    }

    private void RestartFade()
    {
        Card.Classes.Remove("fadein");
        if (!IsMotionReduced)
        {
            Card.Classes.Add("fadein");
        }
    }

    private void UpdateHole()
    {
        var full = new Rect(0, 0, double.IsNaN(Width) ? 0 : Width, double.IsNaN(Height) ? 0 : Height);
        Geometry geometry = new RectangleGeometry(full);
        if (SpotWidth > 0 && SpotHeight > 0)
        {
            var hole = new Rect(SpotX, SpotY, SpotWidth, SpotHeight).Intersect(full); // 층 끝에 붙은 대상의 구멍 여유가 시스템 바로 넘어가지 않게 한다
            geometry = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, new RectangleGeometry(hole, HoleRadius, HoleRadius));
            Canvas.SetLeft(Ring, hole.X);
            Canvas.SetTop(Ring, hole.Y);
            Ring.Width = hole.Width;
            Ring.Height = hole.Height;
        }

        Dim.Data = geometry;
    }

    /// <summary>대상 아래 → 위 → 오른쪽 → 왼쪽 순으로 들어가는 자리에 카드를 둔다. 어디에도 안 들어가면 창 아래쪽에 둔다.</summary>
    private void PlaceCard()
    {
        if (!Card.IsVisible || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        var cardW = Card.DesiredSize.Width;
        var cardH = Card.DesiredSize.Height;
        var hole = (_target ?? new Rect(Width / 2, Height / 2, 0, 0)).Inflate(HolePadding);
        double x;
        double y;
        if (hole.Bottom + CardGap + cardH <= Height - Edge)
        {
            (x, y) = (hole.Center.X - (cardW / 2), hole.Bottom + CardGap);
        }
        else if (hole.Y - CardGap - cardH >= Edge)
        {
            (x, y) = (hole.Center.X - (cardW / 2), hole.Y - CardGap - cardH);
        }
        else if (hole.Right + CardGap + cardW <= Width - Edge)
        {
            (x, y) = (hole.Right + CardGap, hole.Center.Y - (cardH / 2));
        }
        else if (hole.X - CardGap - cardW >= Edge)
        {
            (x, y) = (hole.X - CardGap - cardW, hole.Center.Y - (cardH / 2));
        }
        else
        {
            (x, y) = ((Width - cardW) / 2, Height - cardH - Edge);
        }

        Canvas.SetLeft(Card, Math.Clamp(x, Edge, Math.Max(Edge, Width - cardW - Edge)));
        Canvas.SetTop(Card, Math.Clamp(y, Edge, Math.Max(Edge, Height - cardH - Edge)));
    }
}
