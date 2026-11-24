// 패널 끝 확장 버튼(요건 3) — 잘린 끝에 놓이는 작은 쉐브론 버튼. 방향에 맞는 글리프·툴팁·자동화 이름을 스스로 갖추고, 평소에는 숨었다가 포인터가 가까이 오면 보이며 키보드로 닿는다
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using xkxk.frameworks.ui.Text;

namespace xkxk.frameworks.ui.Controls;

/// <summary>버튼이 가리키는 확장 방향.</summary>
public enum PanelCornerDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// 마우스 환경에서는 숨겨 두고(불투명도 0·적중 안 됨, 자리는 그대로) <see cref="PanelCornerProximity"/> 가 가까움을 알리거나 키보드 포커스가 오면 보인다. 터치 환경은 호버가 없어 항상 보인다.
/// 보일 때 불투명도 0.6, 포인터가 올라가거나 포커스를 받으면 1.0. 색은 상위 층 스타일이 Foreground 로 정한다(이 컨트롤은 색을 모른다).
/// 눌렀을 때의 동작은 상위 층이 Click 으로 받는다. 문자열은 UiText 의 panel.corner.* 키다.
/// </summary>
public sealed class PanelCornerButton : Button
{
    /// <summary>마우스 환경 크기(dip).</summary>
    public const double PointerSize = 18;

    /// <summary>터치 환경 크기(dip).</summary>
    public const double TouchSize = 28;

    private const double IdleOpacity = 0.6;

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(100);

    private readonly bool _alwaysShown;
    private readonly Path _glyph;
    private PanelCornerProximity? _proximity;
    private bool _revealed;
    private bool _keyboardFocused;

    public PanelCornerButton(PanelCornerDirection direction, bool touch)
    {
        Direction = direction;
        _alwaysShown = touch;
        Focusable = true;
        IsTabStop = true;
        Width = touch ? TouchSize : PointerSize;
        Height = touch ? TouchSize : PointerSize;
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        HorizontalAlignment = direction switch
        {
            PanelCornerDirection.Left => HorizontalAlignment.Left,
            PanelCornerDirection.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        VerticalAlignment = direction switch
        {
            PanelCornerDirection.Up => VerticalAlignment.Top,
            PanelCornerDirection.Down => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        _glyph = new Path
        {
            Data = Geometry.Parse(GlyphData(direction)),
            Stretch = Stretch.None,
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            [!Shape.StrokeProperty] = this[!ForegroundProperty],
        };
        Content = _glyph;
        RefreshText();
        ApplyShown();
        if (!touch)
        {
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = FadeDuration } };
        }
    }

    public PanelCornerDirection Direction { get; }

    /// <summary>지금 보이고 눌리는 상태인가.</summary>
    public bool IsShown => _alwaysShown || _revealed || _keyboardFocused;

    /// <summary>포인터가 가까운가. <see cref="PanelCornerProximity"/> 가 정한다.</summary>
    internal bool IsRevealed
    {
        get => _revealed;
        set
        {
            if (_revealed != value)
            {
                _revealed = value;
                ApplyShown();
            }
        }
    }

    /// <summary>쉐브론 모양 이름(시험·진단용): 좌우 패널은 ∧∨, 상하 패널은 &lt;&gt;.</summary>
    public string GlyphName => Direction switch
    {
        PanelCornerDirection.Left => "<",
        PanelCornerDirection.Right => ">",
        PanelCornerDirection.Up => "∧",
        _ => "∨",
    };

    /// <summary>현재 언어의 툴팁·자동화 이름으로 다시 맞춘다. 언어가 바뀐 뒤 상위 층이 부른다.</summary>
    public void RefreshText()
    {
        var text = UiText.Tr(Direction switch
        {
            PanelCornerDirection.Left => "panel.corner.expandLeft",
            PanelCornerDirection.Right => "panel.corner.expandRight",
            PanelCornerDirection.Up => "panel.corner.expandUp",
            _ => "panel.corner.expandDown",
        });
        ToolTip.SetTip(this, text);
        AutomationProperties.SetName(this, text);
    }

    /// <summary>숨은 상태에서 터치 첫 탭이 이 버튼 자리에 닿았다. 적중 검사를 다시 할 수 없어 근접 추적이 대신 누른다.</summary>
    internal void ClickFromTouch() => OnClick();

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_alwaysShown && TopLevel.GetTopLevel(this) is { } top)
        {
            _proximity = PanelCornerProximity.Register(top, this);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _proximity?.Unregister(this);
        _proximity = null;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _keyboardFocused = e.NavigationMethod != NavigationMethod.Pointer; // 마우스로 누른 포커스는 포인터가 멀어지면 숨어야 한다
        ApplyShown();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _keyboardFocused = false;
        ApplyShown();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsFocusedProperty)
        {
            ApplyShown();
        }
        else if (change.Property == BoundsProperty)
        {
            _proximity?.RequestReevaluate();
        }
    }

    private void ApplyShown()
    {
        IsHitTestVisible = IsShown; // 숨김 상태에서는 아래 패널로 클릭이 지나간다
        Opacity = !IsShown ? 0 : IsPointerOver || IsFocused ? 1.0 : IdleOpacity;
    }

    private static string GlyphData(PanelCornerDirection direction) => direction switch
    {
        PanelCornerDirection.Left => "M6,1 L2,5 L6,9",
        PanelCornerDirection.Right => "M2,1 L6,5 L2,9",
        PanelCornerDirection.Up => "M1,6 L5,2 L9,6",
        _ => "M1,2 L5,6 L9,2",
    };
}
