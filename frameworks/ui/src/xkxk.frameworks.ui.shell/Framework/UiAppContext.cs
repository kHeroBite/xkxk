// 앱 컨텍스트 구현 — XkxkUi.Current 가 돌려주는 프로세스 단일 인스턴스(문구·화면 등록·메인 뷰모델·셸 명령 보관, UADR-0011)
using System;
using System.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
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
    private IViewRegistry? _views;
    private MainViewModel? _mainViewModel;
    private IShellCommands? _shell;

    private UiAppContext()
    {
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
