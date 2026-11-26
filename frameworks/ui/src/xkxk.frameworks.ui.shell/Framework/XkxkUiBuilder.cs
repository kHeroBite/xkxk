// 별별UI 빌더 — 모듈·서비스를 모아 Build 에서 등록 순서대로 1회 구성한다(생성자 internal)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 별별UI 앱 구성 빌더. <see cref="XkxkUi.CreateBuilder()"/> 로만 만든다.
/// </summary>
/// <remarks>
/// 규칙 위반(빈 ID·중복 ID·구성 예외)은 진단으로 모은다. null 인자는 <see cref="ArgumentNullException"/> 이다.
/// 확장 규칙(UADR-0009): 새 기능은 메서드 추가로만 넓히고 기존 서명은 바꾸지 않는다.
/// </remarks>
public sealed class XkxkUiBuilder
{
    private readonly List<UiModule> _modules = new();
    private bool _built;

    /// <summary>프레임워크 내부 생성자. 소비자는 <see cref="XkxkUi"/> 를 쓴다.</summary>
    internal XkxkUiBuilder(XkxkUiBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Diagnostics = new UiDiagnostics();
        Services = new UiServiceRegistry(Diagnostics);
    }

    /// <summary>빌더 옵션.</summary>
    public XkxkUiBuilderOptions Options { get; }

    /// <summary>서비스 등록부.</summary>
    public UiServiceRegistry Services { get; }

    /// <summary>진단 모음.</summary>
    public UiDiagnostics Diagnostics { get; }

    /// <summary>모듈을 등록한다. 구성은 <see cref="Build"/> 때 등록 순서대로 한다.</summary>
    /// <param name="module">모듈. null 이면 <see cref="ArgumentNullException"/>.</param>
    public XkxkUiBuilder AddModule(UiModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ThrowIfBuilt();
        _modules.Add(module);
        return this;
    }

    /// <summary>모듈 형식으로 등록한다(매개변수 없는 생성자로 만든다).</summary>
    public XkxkUiBuilder AddModule<TModule>() where TModule : UiModule, new()
    {
        return AddModule(new TModule());
    }

    /// <summary>서비스 등록부를 구성한다.</summary>
    /// <param name="configure">구성 동작. null 이면 <see cref="ArgumentNullException"/>.</param>
    public XkxkUiBuilder ConfigureServices(Action<UiServiceRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfBuilt();
        configure(Services);
        return this;
    }

    /// <summary>
    /// 모듈을 등록 순서대로 1회씩 구성하고 앱을 만든다. 두 번째 호출은 <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <exception cref="UiConfigurationException">Error 진단이 있고 정책이 <see cref="UiErrorPolicy.Throw"/> 일 때.</exception>
    public XkxkUiApplication Build()
    {
        ThrowIfBuilt();
        _built = true;

        var configured = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var session = new UiBuildSession();
        foreach (var module in _modules)
        {
            ConfigureModule(module, configured, seen, session);
        }

        // 모듈 순서와 무관하게 메뉴의 명령(XKUI0405)과 명령 아이콘을 Build 끝에 검사한다. 이 뒤의 모듈 레지스트리 등록은 실행 중 등록이다(ErrorPolicy 미적용).
        if (UiAppContext.Instance.MenuStoreCreated)
        {
            UiAppContext.Instance.MenuStore.CompleteBuild(session, Diagnostics);
        }

        session.Complete(UiAppContext.Instance.IconStore, Diagnostics);

        if (Diagnostics.HasErrors && Options.ErrorPolicy == UiErrorPolicy.Throw)
        {
            throw new UiConfigurationException("별별UI 구성 오류가 있습니다.", Diagnostics.Entries);
        }

        return new XkxkUiApplication(Options, Services, Diagnostics, configured.AsReadOnly());
    }

    private void ConfigureModule(UiModule module, List<string> configured, HashSet<string> seen, UiBuildSession session)
    {
        var id = module.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            Diagnostics.Report(new UiDiagnostic(
                UiDiagnosticIds.ModuleIdEmpty,
                UiDiagnosticSeverity.Error,
                $"모듈 ID 가 비어 있어 건너뜁니다: {module.GetType().FullName}"));
            return;
        }

        if (!seen.Add(id))
        {
            Diagnostics.Report(new UiDiagnostic(
                UiDiagnosticIds.ModuleDuplicate,
                UiDiagnosticSeverity.Error,
                $"같은 모듈 ID 가 이미 등록되어 뒤 모듈을 건너뜁니다: {id}",
                id));
            return;
        }

        try
        {
            module.Configure(new UiModuleContext(id, Options, Services, Diagnostics, session));
            configured.Add(id);
        }
        catch (Exception ex)
        {
            Diagnostics.Report(new UiDiagnostic(
                UiDiagnosticIds.ModuleConfigureFailed,
                UiDiagnosticSeverity.Error,
                $"모듈 구성 중 예외: {ex.GetType().FullName}: {ex.Message}",
                id));
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("이 빌더는 이미 Build 되었습니다.");
        }
    }
}
