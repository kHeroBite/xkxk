// 앱 컨텍스트 구현 — XkxkUi.Current 가 돌려주는 프로세스 단일 인스턴스(문구·화면 등록·메인 뷰모델·셸 명령 보관, UADR-0011)
using System;
using System.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiAppContext"/> 의 유일한 구현이다. 소비자에게는 <see cref="XkxkUi.Current"/> 로만 보인다.
/// </summary>
/// <remarks>
/// 프로세스 전역 인스턴스 1개이며 리셋하지 않는다. 옛 정적 접근자(Loc.Instance·ViewRegistry.Current·App.MainViewModel·App.Shell)는
/// 모두 이 인스턴스로 위임한다(B04). 리플렉션을 쓰지 않는다(AOT·WASM).
/// </remarks>
internal sealed class UiAppContext : IUiAppContext
{
    // Loc 생성은 처음 쓰일 때 한 번만 한다. Loc 정적 초기화(Warn 등)는 new Loc() 직전에 끝나므로 순환 초기화가 없다.
    private readonly Lazy<Loc> _loc = new(() => new Loc(), LazyThreadSafetyMode.ExecutionAndPublication);

    // B05 컨텍스트 서비스. 생성자 연쇄(UiSettings → 부품, ThemeCatalog → Localization, MonitorMenuViewModel → Localization·Settings)는
    // 자기 서비스를 다시 부르지 않는다. 같은 스레드 재귀 접근이면 Lazy 가 InvalidOperationException 을 낸다.
    private readonly Lazy<UiSettings> _settings = new(() => new UiSettings(), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<ThemeCatalog> _themes = new(() => new ThemeCatalog(), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<MonitorMenuViewModel> _monitors = new(() => new MonitorMenuViewModel(), LazyThreadSafetyMode.ExecutionAndPublication);

    // 투어 카탈로그: 소스와 캐시는 _tourLock 안에서만 읽고 쓴다. 소스를 바꾸면 캐시를 비워 다음 접근 때 다시 읽는다.
    private readonly object _tourLock = new();
    private ITourCatalogSource _tourSource = new EmbeddedTourCatalogSource();
    private TourCatalogData? _tour;

    private IViewRegistry? _views;
    private MainViewModel? _mainViewModel;
    private IShellCommands? _shell;

    // B06 레지스트리. 생성 연쇄(UiCommandStore → ShellDefaultCommands → IconStore)는 CommandStore 를 다시 부르지 않고,
    // UiIconStore 는 다른 저장소를 부르지 않는다. 같은 스레드 재귀 접근이면 Lazy 가 InvalidOperationException 을 낸다.
    private readonly UiDiagnostics _diagnostics = new();
    private readonly Lazy<UiIconStore> _iconStore;
    private readonly Lazy<UiCommandStore> _commandStore;

    private UiAppContext()
    {
        _iconStore = new(() => new UiIconStore(_diagnostics), LazyThreadSafetyMode.ExecutionAndPublication);
        _commandStore = new(() => new UiCommandStore(this, IconStore, _diagnostics), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>프로세스 단일 인스턴스.</summary>
    internal static UiAppContext Instance { get; } = new();

    /// <summary>구체 문구 서비스. 셸 내부 진단 멤버(AllKeys·MissingKeys)는 이 경로로 쓴다.</summary>
    internal Loc CoreLoc => _loc.Value;

    /// <inheritdoc/>
    public IUiLocalizer Localization => CoreLoc;

    /// <inheritdoc/>
    public IViewRegistry Views =>
        Volatile.Read(ref _views)
        ?? throw new InvalidOperationException("IViewRegistry 가 등록되지 않았다. Application 생성자에서 등록해야 한다."); // i18n-ok 개발자 오류

    /// <inheritdoc/>
    public MainViewModel? MainViewModel => Volatile.Read(ref _mainViewModel);

    /// <inheritdoc/>
    public IShellCommands? Shell => Volatile.Read(ref _shell);

    /// <inheritdoc/>
    public UiSettings Settings => _settings.Value;

    /// <inheritdoc/>
    public ThemeCatalog Themes => _themes.Value;

    /// <inheritdoc/>
    public MonitorMenuViewModel Monitors => _monitors.Value;

    /// <inheritdoc/>
    public TourCatalogData Tour
    {
        get
        {
            lock (_tourLock)
            {
                return _tour ??= TourCatalog.Load(_tourSource);
            }
        }
    }

    /// <inheritdoc/>
    public IUiIconRegistry Icons => IconStore.Runtime;

    /// <inheritdoc/>
    public IUiCommandRegistry Commands => CommandStore.Runtime;

    /// <inheritdoc/>
    public UiDiagnostics Diagnostics => _diagnostics;

    /// <summary>아이콘 저장소(모듈 보기·그리기 실패 보고용 내부 경로).</summary>
    internal UiIconStore IconStore => _iconStore.Value;

    /// <summary>명령 저장소(모듈 보기용 내부 경로).</summary>
    internal UiCommandStore CommandStore => _commandStore.Value;

    /// <summary>디자인 테마 해석기(DesignThemeResolver)의 가변 상태. 컨텍스트 생성 때 함께 만든다(지연 아님).</summary>
    internal DesignThemeState DesignTheme { get; } = new();

    /// <summary>투어 카탈로그 소스를 바꾼다. null 이면 내장 소스로 되돌린다. 캐시를 비워 다음 <see cref="Tour"/> 접근 때 다시 읽는다.</summary>
    /// <remarks>옛 TourCatalog.UseSource 가 이 메서드로 위임한다. 진행 중 투어 세션은 시작할 때 받은 단계를 그대로 쓴다.</remarks>
    internal void UseTourSource(ITourCatalogSource? source)
    {
        lock (_tourLock)
        {
            _tourSource = source ?? new EmbeddedTourCatalogSource();
            _tour = null;
        }
    }

    /// <inheritdoc/>
    public void RegisterViews(IViewRegistry views)
    {
        ArgumentNullException.ThrowIfNull(views);
        Volatile.Write(ref _views, views);
    }

    /// <summary>메인 뷰모델을 넣는다. App 기동(OnFrameworkInitializationCompleted)에서만 부른다.</summary>
    internal void SetMain(MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        Volatile.Write(ref _mainViewModel, vm);
    }

    /// <summary>셸 명령 표면을 넣는다. App 기동에서 시작 인자 적용 뒤에 부른다.</summary>
    internal void SetShell(IShellCommands shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        Volatile.Write(ref _shell, shell);
    }
}
