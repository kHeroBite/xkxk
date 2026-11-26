// 모듈 구성 컨텍스트 내부 구현 — 빌더의 옵션·서비스·진단 인스턴스와 앱 레지스트리의 모듈용 보기를 노출한다
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary><see cref="IUiModuleContext"/> 의 프레임워크 내부 구현.</summary>
internal sealed class UiModuleContext : IUiModuleContext
{
    private readonly UiBuildSession _session;
    private IUiIconRegistry? _icons;
    private IUiCommandRegistry? _commands;

    internal UiModuleContext(string moduleId, XkxkUiBuilderOptions options, UiServiceRegistry services, UiDiagnostics diagnostics, UiBuildSession session)
    {
        ModuleId = moduleId;
        Options = options;
        Services = services;
        Diagnostics = diagnostics;
        _session = session;
    }

    public string ModuleId { get; }

    public XkxkUiBuilderOptions Options { get; }

    public UiServiceRegistry Services { get; }

    public UiDiagnostics Diagnostics { get; }

    // 처음 쓸 때 1회 만들어 보관한다. 등록 주체는 모듈 ID, 진단은 Build 중 빌더·Build 뒤 앱(T-POLICY P4).
    public IUiIconRegistry Icons => _icons ??= UiAppContext.Instance.IconStore.ViewFor(ModuleId, Diagnostics, _session);

    public IUiCommandRegistry Commands => _commands ??= UiAppContext.Instance.CommandStore.ViewFor(ModuleId, Diagnostics, _session);
}
