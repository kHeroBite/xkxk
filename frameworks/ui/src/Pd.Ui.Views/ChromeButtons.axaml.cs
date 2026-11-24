// 제목줄 Mars 버튼 묶음 동작 — 셸 명령 호출·상태별 글리프/색/툴팁 갱신(Mars 표시 방식)·새로고침 제목줄 안 확인(끌기 가드·클릭 계측은 MdiLockDragGuard·CaptionClickTiming)
using Pd.Poc.Shell;
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Settings;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Tour;

namespace Pd.Ui.Views;

public partial class ChromeButtons : UserControl
{
    /// <summary>SDI 창 제목줄에 붙었으면 true(독립 버튼 = 독립모드 상태 표시).</summary>
    public static readonly StyledProperty<bool> IsSdiHostProperty =
        AvaloniaProperty.Register<ChromeButtons, bool>(nameof(IsSdiHost));

    private PocDocument? _doc;
    private DocumentChromeState? _state;
    private readonly CaptionClickTiming _timing = new(); // 같은 행 최소·최대·닫기 실클릭 계측
    private readonly MdiLockDragGuard _guard;            // 위치 고정 시 MDI 제목줄 끌기 막기
    private static bool s_shadowReady; // ApplyShadow 최초 1회 여부

    public ChromeButtons()
    {
        _guard = new MdiLockDragGuard(() => _state?.IsLocked == true, () => { if (_doc is { } d) { Shell?.Activate(d); } });
        InitializeComponent();
        if (!s_shadowReady) { s_shadowReady = true; WindowCommands.ApplyShadow(); } // 그림자·외곽선 리소스 최초 1회 준비(이후 변경은 UiSettings.Changed·setter) — 창 N개 재생성 비용 제거(D-2 예비안 B)
    }

    public bool IsSdiHost
    {
        get => GetValue(IsSdiHostProperty);
        set => SetValue(IsSdiHostProperty, value);
    }

    private static WindowCommands? Shell => App.Shell as WindowCommands;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Bind(WindowCommands.FindDocument(DataContext)); // 웹 관리형 SDI 창은 ManagedDockWindowDocument → 안쪽 문서
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Bind(WindowCommands.FindDocument(DataContext));
        Shell?.EnsureZOrderHook(); // MDI 겹침을 MRU 로 유지(Dock 기본은 열린 순서 + 활성 1개)
        Loc.Instance.LanguageChanged -= Refresh;
        Loc.Instance.LanguageChanged += Refresh; // 코드로 만든 상태별 툴팁을 언어 전환 때 다시 만든다(Q17)
        CaptionLayout.Applied -= Refresh;
        CaptionLayout.Applied += Refresh; // 적중률 색은 코드가 넣으므로 창 스타일·테마가 바뀌면 다시 고른다
        Refresh(); // 창(MdiDocumentWindow)에 붙은 뒤라야 적갈색 틀 클래스를 걸 수 있다

        _timing.Attach(this); // 같은 행의 최소·최대·닫기(Dock 템플릿 파트 / SDI 버튼) 실클릭 시간
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Instance.LanguageChanged -= Refresh;
        CaptionLayout.Applied -= Refresh;
        Bind(null);
        _guard.Hook(null);
        _timing.Detach();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSdiHostProperty)
        {
            Refresh();
        }
    }

    private void Bind(PocDocument? doc)
    {
        if (ReferenceEquals(doc, _doc) && (doc is null || _state is not null))
        {
            return;
        }

        if (_state is not null)
        {
            _state.PropertyChanged -= OnStateChanged;
        }

        _doc = doc;
        _state = doc is null ? null : WindowCommands.ChromeOf(doc);
        if (doc is not null && _state is not null)
        {
            Shell?.SyncChrome(doc);
            _state.PropertyChanged += OnStateChanged;
        }

        Refresh();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>표시를 상태(정본)에 맞춘다. Mars 처럼 독립·동기화·중복은 글리프 교체, 항상 위·캐시는 색으로 표시한다.</summary>
    private void Refresh()
    {
        var sdi = IsSdiHost || (_doc is not null && Shell?.IsFloating(_doc) == true);
        var pinned = _state?.IsPinned == true;
        var sync = _state?.SyncEnabled != false;
        var dup = _state?.AllowDuplicate == true;

        BtnSdi.IsChecked = sdi;
        IconSdi.Data = Res(sdi ? "PdIconSdi" : "PdIconMdiMode") as Geometry;
        ToolTip.SetTip(BtnSdi, Loc.Tr(sdi ? "chrome.tip.sdiFloating" : "chrome.tip.sdiDocked"));

        BtnPin.IsChecked = pinned;
        BtnPin.Classes.Set("pinOn", pinned);
        ToolTip.SetTip(BtnPin, Loc.Tr(pinned ? "chrome.tip.pinOn" : "chrome.tip.pinOff"));
        var mdi = this.FindAncestorOfType<MdiDocumentWindow>();
        mdi?.Classes.Set("pinned", pinned); // MDI 적갈색 제목줄(Mars)
        mdi?.Classes.Set("pdSdi", sdi); // U12: 관리형 SDI 창(웹·Android)은 문서 기호 대신 앱 로고(MdiDocumentWindowTheme.axaml)

        var locked = _state?.IsLocked == true;
        BtnLock.IsChecked = locked;
        BtnLock.Classes.Set("lockOn", locked); // 켜짐 색(틀 색은 바꾸지 않음 — 틀 적갈색은 항상 위 전용)
        IconLock.Data = Res(locked ? "DockIconPinnedGeometry" : "DockIconPinGeometry") as Geometry;
        ToolTip.SetTip(BtnLock, Loc.Tr(locked ? "chrome.tip.lockOn" : "chrome.tip.lockOff"));
        mdi?.Classes.Set("locked", locked); // 크기 손잡이 숨김(MdiDocumentWindowTheme.axaml), 제목줄 끌기는 MdiLockDragGuard 가 막음
        // 근본 차단(team-lead 승인): 웹 관리형 창의 내용 영역 끌기(Dock ContentPressedHandler)·도킹 끌기도 막는다. 풀면 테마 바인딩(CanDrag)으로 원복.
        if (mdi is not null && locked) { DockProperties.SetIsDragEnabled(mdi, false); }
        else { mdi?.ClearValue(DockProperties.IsDragEnabledProperty); }
        _guard.Hook(mdi);

        BtnSync.IsChecked = sync;
        IconSync.Data = Res(sync ? "PdIconLink" : "PdIconLinkBroken") as Geometry;
        ToolTip.SetTip(BtnSync, Loc.Tr(sync ? "chrome.tip.syncOn" : "chrome.tip.syncOff") + "\n" + Loc.Tr("chrome.tip.syncNote"));

        BtnDup.IsChecked = dup;
        IconDup.Data = Res(dup ? "PdIconUnlock" : "PdIconLock") as Geometry;
        ToolTip.SetTip(BtnDup, Loc.Tr(dup ? "chrome.tip.dupOn" : "chrome.tip.dupOff"));

        var hits = _state?.CacheHits ?? 0;
        var total = _state?.CacheTotal ?? 0;
        var ratio = _state?.CacheRatio ?? 0;
        BtnRefresh.Foreground = CacheBrush(ratio);
        ToolTip.SetTip(BtnRefresh, (total == 0 ? Loc.Tr("chrome.tip.refresh") : Loc.Tr("chrome.tip.refreshCache", Math.Round(ratio * 100), hits, total))
            + "\n" + Loc.Tr("chrome.tip.refreshNote"));
    }

    /// <summary>캐시 적중 비율 색(Mars): 0% 은색 · 1~99% 연노랑→금색 · 100% 주황. 색은 테마 토큰이고 제목줄 바탕(강조 띠·표면색)에 맞는 쪽을 CaptionLayout 별칭이 고른다.</summary>
    private IBrush? CacheBrush(double ratio)
    {
        if (ratio <= 0)
        {
            return Res("PdChromeCacheZero") as IBrush;
        }

        if (ratio >= 1)
        {
            return Res("PdChromeCacheFull") as IBrush;
        }

        var low = (Res("PdChromeCacheLow") as ISolidColorBrush)?.Color ?? Colors.LightYellow;
        var high = (Res("PdChromeCacheHigh") as ISolidColorBrush)?.Color ?? Colors.Gold;
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * ratio);
        return new SolidColorBrush(Color.FromArgb(255, Mix(low.R, high.R), Mix(low.G, high.G), Mix(low.B, high.B)));
    }

    private object? Res(string key) => this.TryFindResource(key, out var value) ? value : null;

    private void Run(Action<WindowCommands, PocDocument> action)
    {
        if (_doc is { } doc && Shell is { } shell)
        {
            action(shell, doc);
        }

        Refresh();
    }

    private void OnSdi(object? sender, RoutedEventArgs e)
    {
        Run((s, d) => s.ChromeToggleSdi(d));
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background); // 전환 뒤 다른 창으로 옮겨 붙은 상태 반영
    }

    private void OnPin(object? sender, RoutedEventArgs e) => Run((s, d) => s.ChromeTogglePin(d));

    private void OnLock(object? sender, RoutedEventArgs e) => Run((s, d) => s.ChromeToggleLock(d));

    private void OnSync(object? sender, RoutedEventArgs e) => Run((s, d) => s.ChromeToggleSync(d));

    private void OnDup(object? sender, RoutedEventArgs e) => Run((s, d) => s.ChromeToggleDuplicate(d));

    // 새로고침: 버튼 → 제목줄 안 확인(RefreshConfirm) → 확인 시 ChromeRefresh. 단계마다 로그를 남겨 실측에서 어디서 멈췄는지 가린다.
    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshConfirm.IsVisible = !RefreshConfirm.IsVisible;
        App.MainViewModel?.AddLog(Loc.Tr(RefreshConfirm.IsVisible ? "chrome.log.refreshAskShown" : "chrome.log.refreshAskHidden", _doc?.Title ?? Loc.Tr("chrome.log.noDocument")));
    }

    private void OnRefreshConfirm(object? sender, RoutedEventArgs e)
    {
        RefreshConfirm.IsVisible = false;
        App.MainViewModel?.AddLog(Loc.Tr("chrome.log.refreshConfirmed", _doc?.Title ?? Loc.Tr("chrome.log.noDocumentBlocked")));
        if (_doc is { } doc && Shell is { } shell)
        {
            shell.ChromeRefresh(doc); // 이 제목줄은 닫히는 창과 함께 떨어진다
        }
    }

    private void OnRefreshCancel(object? sender, RoutedEventArgs e) => RefreshConfirm.IsVisible = false;

    /// <summary>도움말: 이 버튼이 속한 창(MDI 문서 창·SDI 창)의 투어를 시작한다(요건 6).</summary>
    private void OnHelp(object? sender, RoutedEventArgs e) => TourController.StartFrom(BtnHelp);
}
