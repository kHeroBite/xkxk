// 단일 뷰(웹·Android) 시스템 Flyout 공용 동작 — 뷰포트 맞춤, 크기가 바뀌면 다시 맞춤(창 줄임·기기 회전·소프트 키보드), Esc·뒤로 가기로 맨 위부터 닫기
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 시스템 Flyout 하나의 열림 동안의 맞춤·닫기 처리. 내용 크기는 요청 크기와 뷰포트 중 작은 쪽이다.
/// Esc 는 포커스가 없어도 받도록 TopLevel 터널 단계에서 처리해, 전역 Esc 창닫기(EscCloseInput)가 활성 문서를 닫기 전에 끝낸다.
/// </summary>
public sealed class FittedFlyout
{
    /// <summary>FlyoutPresenter 기본 상한(폭 456·높이 758)과 안쪽 여백을 푸는 스타일 클래스(xkxk.frameworks.ui/Styles/PdUiStyles.axaml).</summary>
    private const string PresenterClass = "pdSystemDialog";

    /// <summary>뷰포트에서 빼는 폭·높이: 양쪽 바깥 여백 8 + 프레젠터 테두리 1.</summary>
    private const double ViewportInset = 18;

    /// <summary>열린 순서대로 쌓인 Flyout. 마지막이 맨 위다.</summary>
    private static readonly List<FittedFlyout> OpenStack = new();

    private readonly Flyout _flyout;
    private readonly Control _content;
    private readonly double _width;
    private readonly double _height;
    private readonly bool _fixedHeight;
    private readonly double _minWidth;
    private readonly double _minHeight;
    private TopLevel? _top;
    private Control? _viewport;

    private FittedFlyout(Flyout flyout, Control content, double width, double height, bool fixedHeight)
    {
        _flyout = flyout;
        _content = content;
        _width = width;
        _height = height;
        _fixedHeight = fixedHeight;
        _minWidth = content.MinWidth;
        _minHeight = content.MinHeight;
    }

    /// <summary>열려 있는 시스템 Flyout 이 있는가. 있으면 Esc 는 여기서 처리한다(같은 키를 따로 받는 화면이 먼저 닫히지 않게 한다).</summary>
    public static bool AnyOpen => OpenStack.Count > 0;

    /// <summary>맨 위 시스템 Flyout 을 닫는다. 없으면 false 다(BackDismissal 이 부른다).</summary>
    internal static bool TryHideTop()
    {
        if (OpenStack.Count == 0)
        {
            return false;
        }

        OpenStack[^1]._flyout.Hide();
        return true;
    }

    /// <summary>웹 호스트가 넣는다. 앱 안 초점과 별개로 문서 초점이 앱에 있게 한다(없으면 키가 앱에 오지 않는다). 다른 헤드는 null 이다.</summary>
    public static Action? HostKeyFocus { get; set; }

    /// <summary>
    /// hasCallerAnchor 가 false 면 anchor 는 뷰 전체라 가운데에 둔다('아래'가 뜻이 없고, 키보드가 올라와도 본문이 덜 가려진다).
    /// fixedHeight 면 내용 크기로 줄지 않게 높이를 뷰포트에 맞는 최대값으로 고정한다.
    /// </summary>
    public static Flyout Create(Control content, Control anchor, bool hasCallerAnchor, double width, double height, bool fixedHeight)
    {
        var flyout = new Flyout
        {
            Placement = hasCallerAnchor ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.Center,
            // Standard 는 열 때 첫 입력 칸에 초점을 줘 터치 기기에서 소프트 키보드가 올라온다
            ShowMode = InputFormFactor.IsTouchDevice ? FlyoutShowMode.Transient : FlyoutShowMode.Standard,
            Content = content,
        };
        flyout.FlyoutPresenterClasses.Add(PresenterClass);

        var session = new FittedFlyout(flyout, content, width, height, fixedHeight);
        session.Fit(ViewportOf(anchor, new Size(width, height)));
        flyout.Opened += session.OnOpened;
        flyout.Closed += session.OnClosed;
        return flyout;
    }

    /// <summary>
    /// 팝업이 실제로 놓이는 층의 크기. 시스템 바·키보드 여백을 뺀 값이다(Android 는 층 여백으로 준다, AndroidSafeArea). TopLevel 크기는 키보드가 올라와도 그대로라 쓰지 않는다.
    /// 층이 아직 배치되기 전이면 TopLevel 크기로 대신한다.
    /// </summary>
    private static Size ViewportOf(Visual anchor, Size fallback)
    {
        if (OverlayLayer.GetOverlayLayer(anchor) is { Bounds: { Width: > 0, Height: > 0 } } layer)
        {
            return layer.Bounds.Size;
        }

        return TopLevel.GetTopLevel(anchor)?.Bounds.Size ?? fallback;
    }

    private void Fit(Size viewport)
    {
        _content.Width = Math.Max(0, Math.Min(_width, viewport.Width - ViewportInset));
        _content.MaxHeight = Math.Max(0, Math.Min(_height, viewport.Height - ViewportInset));
        _content.MinWidth = Math.Min(_minWidth, _content.Width);
        _content.MinHeight = Math.Min(_minHeight, _content.MaxHeight);
        if (_fixedHeight)
        {
            _content.Height = _content.MaxHeight;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _top = TopLevel.GetTopLevel(_content);
        OpenStack.Add(this);
        if (_top is null)
        {
            return;
        }

        _viewport = OverlayLayer.GetOverlayLayer(_content) ?? (Control)_top;
        _viewport.SizeChanged += OnViewportChanged;
        _top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _top.BackRequested += OnBackRequested; // 제스처 뒤로 가기(Android 13+)는 키 이벤트 없이 이 경로로만 온다
        if (OperatingSystem.IsBrowser())
        {
            Dispatcher.UIThread.Post(EnsureBrowserKeyFocus, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 웹은 문서 초점이 BODY 면 키가 앱에 오지 않는다(화면설계 W-16). 입력 칸이 없는 내용은 앱 안 초점만으로 문서 초점이 옮겨지지 않아(실측) 호스트가 옮긴다.
    /// 앱 안 초점은 안쪽에 없을 때만 내용 자체에 준다(입력 칸이 아니라 소프트 키보드는 뜨지 않는다).
    /// </summary>
    private void EnsureBrowserKeyFocus()
    {
        HostKeyFocus?.Invoke();
        var focused = _top?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is not null && focused.GetSelfAndVisualAncestors().Contains(_content))
        {
            return;
        }

        _content.Focusable = true;
        _content.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        OpenStack.Remove(this);
        if (_top is null)
        {
            return;
        }

        if (_viewport is not null)
        {
            _viewport.SizeChanged -= OnViewportChanged;
            _viewport = null;
        }

        _top.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _top.BackRequested -= OnBackRequested;
        _top = null;
    }

    private void OnViewportChanged(object? sender, SizeChangedEventArgs e)
    {
        Fit(e.NewSize);
        // 크기 변화가 없어도(내용이 이미 작은 경우) 뷰포트 안으로 옮겨야 하므로, 새 배치가 끝난 뒤 위치를 다시 계산시킨다
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
    }

    /// <summary>Popup 은 공개된 위치 갱신 API 가 없어, 위치 입력값(오프셋)이 바뀔 때 다시 계산하는 경로를 쓴다.</summary>
    private void Reposition()
    {
        var popup = _flyout.Popup;
        if (!popup.IsOpen)
        {
            return;
        }

        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 1;
        popup.HorizontalOffset = offset;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !IsTop || IsOwnedByInnerPopup())
        {
            return;
        }

        e.Handled = true;
        _flyout.Hide();
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || !IsTop || IsOwnedByInnerPopup())
        {
            return;
        }

        e.Handled = true;
        _flyout.Hide();
    }

    private bool IsTop => OpenStack.Count > 0 && ReferenceEquals(OpenStack[^1], this);

    /// <summary>열린 콤보 목록·색 선택기처럼 Flyout 안에서 다시 띄운 팝업이 키를 먼저 쓴다(Flyout 까지 같이 닫지 않는다).</summary>
    private bool IsOwnedByInnerPopup()
    {
        var focused = _top?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is ComboBox { IsDropDownOpen: true })
        {
            return true;
        }

        if (focused is null)
        {
            return false;
        }

        // 단일 뷰의 팝업은 OverlayLayer 바로 아래 호스트 하나씩이다. 초점이 내용이 아니라 프레젠터에 있을 수 있으므로(입력 칸 없는 내용) 호스트로 가린다
        var chain = focused.GetSelfAndVisualAncestors().ToList();
        var layer = chain.FindIndex(v => v is OverlayLayer);
        if (layer <= 0)
        {
            return false;
        }

        var host = chain[layer - 1];
        return !OpenStack.Any(s => s._content.GetVisualAncestors().Contains(host));
    }
}
