// 모듈 구성 컨텍스트 내부 구현 — 빌더의 옵션·서비스·진단 인스턴스를 그대로 노출한다
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary><see cref="IUiModuleContext"/> 의 프레임워크 내부 구현.</summary>
internal sealed class UiModuleContext : IUiModuleContext
{
    internal UiModuleContext(string moduleId, XkxkUiBuilderOptions options, UiServiceRegistry services, UiDiagnostics diagnostics)
    {
        ModuleId = moduleId;
        Options = options;
        Services = services;
        Diagnostics = diagnostics;
    }

    public string ModuleId { get; }

    public XkxkUiBuilderOptions Options { get; }

    public UiServiceRegistry Services { get; }

    public UiDiagnostics Diagnostics { get; }
}
