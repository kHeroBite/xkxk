// 셸 메인 화면 코드비하인드 — 로드 시 계측(PerfProbe) 연결·상태바 FPS 갱신·기동 절차(Lifecycle/ShellStartup) 1회 시작
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Settings;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Lifecycle;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.views.Depth;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

public partial class MainView : UserControl
{
    /// <summary>제목 탭 툴팁: 전체 제목, 프로젝트가 있으면 "제목 — 프로젝트"(공통원칙 §2-1).</summary>
    public static readonly IMultiValueConverter TabToolTipConverter = new TabToolTipMultiConverter();

    private IDisposable? _perfFps;
    private bool _started;
    private UiStyleTransition? _styleTransition;
    private string? _overviewKeysText;
    private KeyGesture? _overviewKeys;

    public MainView()
    {
        InitializeComponent();

        // 키 바인딩은 도움말 카탈로그 등록에서 만든다. Esc 는 EscCloseInput 이 맡는다
        DataContextChanged += (_, _) => xkxk.frameworks.ui.views.Input.ShortcutBindings.Apply(this, DataContext as MainViewModel);

        // 닫힌 문서의 뷰를 Dock 문서 뷰 캐시에서 뺀다(A11 — MainView.RecyclePrune.cs)
        DataContextChanged += (_, _) => WatchClosedDocuments(DataContext as MainViewModel);

        // 브라우저 호스트가 DockSettings.UseManagedWindows=true 로 설정하면 플로팅(SDI)을 관리형 창 레이어에 그린다.
        DockHost.EnableManagedWindowLayer = DockSettings.UseManagedWindows;
        // 관리형 창 레이어를 Dock 템플릿 Grid 첫 자식으로 옮겨 PointerMoved 터널의 레이어 탐색 순회를 없앤다(그리기·히트 순서는 ZIndex 로 유지, 전 플랫폼 기본)
        ManagedLayerFirst.Attach(DockHost);

        // 탭 우클릭: 활성화하지 않고 그 탭을 창 메뉴 대상으로 정한다(MDI-03). 활성화하면 최소화 탭이 복원되고 SDI 가 앞으로 나와 메뉴 상태가 틀어진다.
        TaskbarTabs.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);

        // 독립 창 아이콘 우클릭: 탭과 같이 활성화하지 않고 그 창의 문서를 창 메뉴 대상으로 정한다(§7.2)
        SdiIcons.AddHandler(PointerPressedEvent, OnSdiIconPointerPressed, RoutingStrategies.Tunnel);

        // 탭 더블클릭 닫기(MDI-08, 옵션). 첫 클릭의 활성화/최소화 뒤 두 번째 탭에서 닫는다.
        TaskbarTabs.AddHandler(DoubleTappedEvent, OnTabDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);

        // 탭을 끌어 탐색기·로그 패널에 놓으면 그 패널 탭으로 합류(Q15 후속, 판정·강조는 PanelJoinDrag)
        TaskbarTabDrag.Attach(TaskbarTabs);

        // 폭이 600px 미만이면 단일 문서 모드(모바일·좁은 브라우저), 복귀 시 Dock 레이아웃.
        SizeChanged += (_, e) => (DataContext as MainViewModel)?.Compact.UpdateWidth(e.NewSize.Width, e.NewSize.Height);

        // U7 탭 상태색: 탭 항목에 문서 크롬 상태(DocumentChromeState.IsMinimized)를 .minimized 클래스로 단다(색은 MainView.axaml 스타일·테마 토큰)
        TaskbarTabs.ContainerPrepared += (_, e) => AttachTabState(e.Container);
        TaskbarTabs.ContainerClearing += (_, e) => DetachTabState(e.Container);

        // 제목 탭 가운데 클릭 = 닫기(01 §2-1, 2026-11-12). 누름은 활성화·끌기로 가지 않게 처리하고 뗌에서 닫는다
        TaskbarTabs.AddHandler(PointerPressedEvent, OnTabMiddlePressed, RoutingStrategies.Tunnel);
        TaskbarTabs.AddHandler(PointerReleasedEvent, OnTabMiddleReleased, RoutingStrategies.Tunnel);

        // 키보드로 탭 안에 포커스가 오면 × 를 보인다(.kbfocus — 마우스 클릭 포커스는 제외해 클릭 뒤 × 가 남지 않게 한다)
        TaskbarTabs.AddHandler(GotFocusEvent, OnTabGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        TaskbarTabs.AddHandler(LostFocusEvent, OnTabLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        // 창 전체 보기 단축키(REQ-104, 설정 shortcut.window.overview). 메뉴와 같은 명령 xkxk.window.overview(ShellCommandIds.WindowOverview)로 보낸다
        AddHandler(KeyDownEvent, OnOverviewKey, RoutingStrategies.Tunnel);

        // 활성화 출처 표식(제목 탭·문서 탭·본문 누름 — 전환 효과 판정, MainView.TitleOrigin.cs)
        InstallOriginMarks();
    }

    /// <summary>설정 단축키(기본 Ctrl+Alt+O)와 맞으면 전체 보기를 연다/닫는다. 값이 바뀌면 다음 입력부터 반영하고, 형식이 틀린 값은 무시한다.</summary>
    private void OnOverviewKey(object? sender, KeyEventArgs e)
    {
        var text = DesignThemeResolver.Stored(DesignThemes.OverviewShortcutKey);
        if (!string.Equals(text, _overviewKeysText, StringComparison.Ordinal))
        {
            _overviewKeysText = text;
            try
            {
                _overviewKeys = KeyGesture.Parse(text);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                _overviewKeys = null;
            }
        }

        if (_overviewKeys?.Matches(e) == true)
        {
            e.Handled = true;
            XkxkUi.Current.Commands.Execute(ShellCommandIds.WindowOverview);
        }
    }

    // 제목 탭 닫기(×) 표시(TitleFade·표시 방식·키보드 포커스·가운데 클릭)는 MainView.TabClose.cs

    private readonly Dictionary<Control, (DocumentChromeState State, PropertyChangedEventHandler Handler)> _tabStates = new();

    private void AttachTabState(Control container)
    {
        DetachTabState(container);
        if ((container.DataContext ?? (container as ContentControl)?.Content) is not PocDocument doc)
        {
            return;
        }

        var state = WindowCommands.ChromeOf(doc);
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentChromeState.IsMinimized))
            {
                container.Classes.Set("minimized", state.IsMinimized);
            }
        };
        state.PropertyChanged += handler;
        _tabStates[container] = (state, handler);
        container.Classes.Set("minimized", state.IsMinimized);
    }

    private void DetachTabState(Control container)
    {
        if (_tabStates.Remove(container, out var old))
        {
            old.State.PropertyChanged -= old.Handler;
        }

        container.Classes.Set("minimized", false);
    }

    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is PocDocument doc
            && DataContext is MainViewModel { DoubleClickCloseEnabled: true } vm)
        {
            vm.Commands.CloseDocument(doc);
            e.Handled = true;
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            && (e.Source as StyledElement)?.DataContext is PocDocument doc)
        {
            WindowMenuOps.TabTarget = doc;
            e.Handled = true; // ListBox 우클릭 선택이 활성 문서를 바꾸지 않게 한다. 메뉴는 뗌(ContextRequested)에서 열려 영향이 없다
        }
    }

    private void OnSdiIconPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            && (e.Source as StyledElement)?.DataContext is SdiItem { Document: { } doc })
        {
            WindowMenuOps.TabTarget = doc;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // 제목 탭 × 표시 방식: 지금 값 반영 + 실행 중 변경 즉시 반영(OnUnloaded 에서 해제)
        XkxkUi.Current.Settings.PropertyChanged -= OnUiSettingChanged;
        XkxkUi.Current.Settings.PropertyChanged += OnUiSettingChanged;
        ApplyTabCloseMode();
        if (_started)
        {
            return;
        }

        _started = true;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel vm)
        {
            return;
        }

        // L3: 화면 방식 전환 효과(약 300ms 페이드). 대상은 DockPanel 의 마지막 자식(문서 영역 Grid) — 제목줄·상태바는 고정
        var fadeTarget = (Content as DockPanel)?.Children.LastOrDefault() as Control;
        if (fadeTarget is not null)
        {
            _styleTransition = new UiStyleTransition(fadeTarget, vm.Compact.Style, vm.AddLog);
        }

        vm.Compact.UpdateWidth(Bounds.Width, Bounds.Height);
        // 진단 빌드에서만 프레임 계측·상태바 FPS(제품 빌드는 null — rAF·타이머 없음, odev-3 PerfProbe.AttachIfEnabled)
        _perfFps = PerfProbe.Current.AttachIfEnabled(topLevel, fps => vm.StatusText = $"FPS {fps:0}");

        // 진행 표시(요건 2): 메인 창 첫 열림 스코프. 브라우저·모바일의 포인터 추적 스피너도 여기서 잇는다
        // 바쁨 표시 시간은 설정값(BusyShowDelayMs·BusyMinVisibleMs)을 호출 때마다 읽어 변경이 즉시 반영된다
        xkxk.frameworks.ui.shell.Busy.BusyService.ConfigureTiming(
            () => XkxkUi.Current.Settings.BusyShowDelayMs,
            () => XkxkUi.Current.Settings.BusyMinVisibleMs);
        // MDI 창 끌기 잡을 곳 폭(F08): 설정값(MdiMinGrabDip)을 호출 때마다 읽는다 — Docking 은 설정 레이어를 직접 참조하지 않는다
        xkxk.frameworks.ui.shell.Docking.MdiDragLimit.ConfigureMinGrab(
            () => XkxkUi.Current.Settings.MdiMinGrabDip);
        // MDI 창 끌기 잔존 비율(01 §4.3): 설정값(MdiMinVisiblePercent, %)을 비율로 바꿔 호출 때마다 읽는다 — 같은 레이어 분리
        xkxk.frameworks.ui.shell.Docking.MdiDragLimit.ConfigureMinVisibleRatio(
            () => XkxkUi.Current.Settings.MdiMinVisiblePercent / 100.0);
        xkxk.frameworks.ui.shell.Busy.BusyHooks.AttachMain(BusyFollower);
        xkxk.frameworks.ui.shell.Busy.BusyHooks.BeginUntilLaid(xkxk.frameworks.ui.shell.Busy.BusyHooks.MainScope, "open");

        // 기동 절차(복원 판단·자동 저장 시작·자동화 실행)는 Lifecycle/ShellStartup 이 정한다 — 화면은 시점만 넘긴다
        ShellStartup.Start(vm, topLevel);

        // 디자인 테마 3D(REQ-104): 문서 창 열기·닫기 기울기, 창 전체 보기 덮개 등록(Shell 은 Views 를 모르므로 WindowOverviewHost 대리자로 — review H-1), 창 수 폴백 공급
        var overviewAnchor = fadeTarget ?? this;
        DepthTransitions.Attach(vm.Factory, overviewAnchor);
        WindowOverview3D.Register(vm, overviewAnchor);
        DesignThemeResolver.WindowCount ??= () => vm.OpenDocuments.Count;
        vm.OpenDocuments.CollectionChanged += (_, _) => DesignThemeResolver.Refresh();

        // 뷰 예열: 첫 프레임 뒤 1초 유휴에 1회(설정 PrewarmViews, 기본 켬). 실패해도 열기에는 영향이 없다
        DocumentPrewarm.StartOnce(Content as Panel); // 화면 밖 예열 호스트를 넣을 메인 패널(GridViewPool.AttachHost)
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        XkxkUi.Current.Settings.PropertyChanged -= OnUiSettingChanged;
        _perfFps?.Dispose();
        _styleTransition?.Dispose();
        (DataContext as MainViewModel)?.Compact.Style.Cancel();
        base.OnUnloaded(e);
    }

    private sealed class TabToolTipMultiConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var title = values.Count > 0 ? values[0] as string : null;
            var project = values.Count > 1 ? values[1] as string : null;
            return string.IsNullOrEmpty(project) ? title : $"{title} — {project}";
        }
    }
}
