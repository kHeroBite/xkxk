// 메인 창 통합 제목줄 코드비하인드(Q14) — 캡션 버튼 표시 조건·최대화 아이콘 전환·창 상태 명령·F10 메뉴 진입·좁은 폭 메뉴 더보기(D-W2, 데스크톱 포함)
using Pd.Poc.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;

namespace Pd.Ui.Views;

public partial class MainTitleBar : UserControl
{
    private Window? _window;
    private TopLevel? _top;

    // 원래 순서의 최상위 메뉴(더보기 제외) — 좁은 폭에서 뒤쪽부터 더보기로 옮기고 넓어지면 되돌린다(D-W2)
    private readonly List<MenuItem> _menuOrder = new();
    private bool _reflowQueued;

    public MainTitleBar()
    {
        InitializeComponent();
        _menuOrder.AddRange(MainMenu.Items.OfType<MenuItem>().Where(m => m != MoreMenu));
        Bar.SizeChanged += (_, _) => QueueReflow();
        UiModeSwitch.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                QueueReflow();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnTopKeyDown, RoutingStrategies.Tunnel);
        _window = _top as Window; // 웹·Android 는 Window 가 아니다 → 캡션 버튼 숨김
        if (_window is null)
        {
            Bar.ContextMenu = null; // 메인 화면이 브라우저 창·앱 전체 화면이라 최소화·최대화·이동·닫기 대상이 없다(W-20)
        }

        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
            _window.Opened += OnWindowOpened; // 확장 여부는 플랫폼 창이 만들어진 뒤 확정된다
        }

        Loc.Instance.LanguageChanged += UpdateCaption;
        Loc.Instance.LanguageChanged += QueueReflow; // 메뉴 문구 길이가 바뀐다
        CaptionLayout.Applied += UpdateCaption; // 크롬 스타일 전환(macOS 네이티브 신호등 사용 여부)
        UpdateCaption();
        UpdateActiveState();
        QueueReflow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _top?.RemoveHandler(KeyDownEvent, OnTopKeyDown);
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window.Opened -= OnWindowOpened;
        }

        Loc.Instance.LanguageChanged -= UpdateCaption;
        Loc.Instance.LanguageChanged -= QueueReflow;
        CaptionLayout.Applied -= UpdateCaption;
        _window = null;
        _top = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.IsExtendedIntoWindowDecorationsProperty
            || e.Property == Window.ExtendClientAreaToDecorationsHintProperty)
        {
            UpdateCaption();
        }
        else if (e.Property == WindowBase.IsActiveProperty)
        {
            UpdateActiveState(); // Activated 이벤트는 IsActive 를 올리기 전에 와서 쓰지 않는다(SdiTitleBar 와 같은 이유)
        }
    }

    // 창이 아닌 웹·Android 는 흐리지 않는다(_window 가 null). 색·투명도는 axaml 스타일과 토큰이 정한다.
    private void UpdateActiveState() => Bar.Classes.Set("inactive", _window is { IsActive: false });

    private void OnWindowOpened(object? sender, EventArgs e) => UpdateCaption();

    /// <summary>
    /// 데스크톱 창: 호스트가 OS 제목줄을 클라이언트 영역으로 확장했으면(실제 상태 또는 요청 힌트) 로고·제목·캡션 버튼을 보이고,
    /// 확장하지 않았으면 OS 제목줄과 겹치므로 로고·제목·캡션 버튼을 숨기고 메뉴만 둔다(team-lead Q14 결정).
    /// 웹·Android(창이 아님)는 로고·제목을 보이고 캡션 버튼만 숨긴다. 최대화 버튼은 상태에 따라 아이콘·툴팁이 바뀐다.
    /// </summary>
    private void UpdateCaption()
    {
        var extended = _window is not null && (_window.IsExtendedIntoWindowDecorations || _window.ExtendClientAreaToDecorationsHint);
        // macOS 실행 + macOS 스타일: 확장 제목줄 위에 OS 신호등이 그려진다(장비 미검증) → 앱 캡션 버튼은 숨기고 신호등 자리를 비운다(MacNativeCaption)
        CaptionButtons.IsVisible = extended && !MacNativeCaption.Apply(Bar, extended ? _window : null, ownsDecorations: false);
        Logo.IsVisible = TitleArea.IsVisible = _window is null || extended;
        var max = _window?.WindowState == WindowState.Maximized;
        ToolTip.SetTip(MaxButton, Loc.Tr(max ? "titlebar.restore" : "titlebar.maximize"));
        if (this.TryFindResource(max ? "DockIconRestoreGeometry" : "DockIconMaximizeGeometry", out var g) && g is Geometry geometry)
        {
            MaxIcon.Data = geometry;
        }
    }

    /// <summary>배치가 끝난 뒤(Background) 1회 메뉴 넘침을 다시 계산한다. 같은 프레임의 여러 요청은 하나로 합친다.</summary>
    private void QueueReflow()
    {
        if (_reflowQueued)
        {
            return;
        }

        _reflowQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _reflowQueued = false;
            ReflowMenu();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 웹·Android(창이 아닌 TopLevel)의 좁은 폭에서 제목줄에 다 들어가지 않는 뒤쪽 최상위 메뉴를 '더보기' 아래로 옮긴다(D-W2 — 접힘 402dp 에서 '시스템(Y)' 잘림).
    /// 모두 들어가면 더보기를 숨기고 원래 배치 그대로다(펼침·태블릿 폭). 데스크톱 창도 넘칠 때만 같은 규칙을 쓴다(창 폭 504 컴팩트 — otest-12, 평상시 폭은 배치 불변).
    /// 폭 = 제목줄 폭 − (로고·제목·오른쪽 도구(UI 형·테마)·캡션 버튼) − 메뉴 자체 여백. 메뉴 폭은 모두 제목줄로 되돌린 뒤 제약 없이 잰 값이다.
    /// </summary>
    private void ReflowMenu()
    {
        RestoreMenu();
        if (_top is null || Bar.Bounds.Width <= 0)
        {
            return;
        }

        MainMenu.Measure(Size.Infinity);
        var items = _menuOrder.Select(m => m.DesiredSize.Width).ToArray();
        var menuChrome = Math.Max(0, MainMenu.DesiredSize.Width - items.Sum());
        TitleTools.Measure(Size.Infinity); // 오른쪽 도구 칸(UI 형 4칸 + 테마 아이콘, 여백 포함) 폭을 새로 잰다 — 메뉴는 이 폭만큼 일찍 더보기로 접힌다
        var fixedWidth = Logo.DesiredSize.Width + TitleArea.DesiredSize.Width + TitleTools.DesiredSize.Width + CaptionButtons.DesiredSize.Width;
        var avail = Bar.Bounds.Width - fixedWidth - menuChrome - 1; // 반올림 여유 1px
        if (items.Sum() <= avail)
        {
            return;
        }

        MoreMenu.IsVisible = true;
        MoreMenu.Measure(Size.Infinity);
        var room = avail - MoreMenu.DesiredSize.Width;
        var keep = 0;
        for (double used = 0; keep < items.Length && used + items[keep] <= room; keep++)
        {
            used += items[keep];
        }

        for (var i = keep; i < _menuOrder.Count; i++)
        {
            MainMenu.Items.Remove(_menuOrder[i]);
            MoreMenu.Items.Add(_menuOrder[i]);
        }
    }

    /// <summary>더보기로 옮긴 메뉴를 원래 순서대로 제목줄에 되돌리고 더보기를 숨긴다.</summary>
    private void RestoreMenu()
    {
        if (MoreMenu.Items.Count > 0)
        {
            MoreMenu.Close();
            foreach (var m in _menuOrder)
            {
                if (MoreMenu.Items.Contains(m))
                {
                    MoreMenu.Items.Remove(m);
                    MainMenu.Items.Insert(MainMenu.Items.Count - 1, m); // 더보기(마지막) 앞 — 옮길 때 뒤쪽부터 연속으로 옮겼으므로 순서가 유지된다
                }
            }
        }

        MoreMenu.IsVisible = false;
    }

    /// <summary>F10(수정키 없음): 메뉴 첫 항목으로 들어간다. Alt 는 Avalonia 메뉴 접근 키(_F 등)가 처리한다.</summary>
    private void OnTopKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None && IsEffectivelyVisible)
        {
            if (MainMenu.Items.Count > 0 && MainMenu.Items[0] is MenuItem first)
            {
                first.Focus(NavigationMethod.Directional);
                first.Open();
                e.Handled = true;
            }
        }
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeToggle(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    /// <summary>끌기 빈칸 더블클릭: OS 가 처리하지 않고 입력을 넘겨준 경우(비 Windows 등)에만 여기로 온다.</summary>
    private void OnDragAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        OnMaximizeToggle(sender, e);
        e.Handled = true;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => _window?.Close(); // App 종료 처리(자동 저장·설정 플러시)를 그대로 탄다
}
