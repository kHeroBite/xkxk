// 구성이 끝난 별별UI 앱 — 옵션·서비스·진단·구성된 모듈 ID 를 읽기 전용으로 노출한다(생성자 internal)
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="XkxkUiBuilder.Build"/> 결과. 소비자는 직접 만들 수 없다.
/// </summary>
/// <remarks>확장 규칙(UADR-0009): 새 정보는 읽기 전용 속성 추가로만 넓힌다.</remarks>
public sealed class XkxkUiApplication
{
    /// <summary>프레임워크 내부 생성자.</summary>
    internal XkxkUiApplication(
        XkxkUiBuilderOptions options,
        IUiServiceProvider services,
        UiDiagnostics diagnostics,
        IReadOnlyList<string> moduleIds)
    {
        Options = options;
        Services = services;
        Diagnostics = diagnostics;
        ModuleIds = moduleIds;
    }

    /// <summary>빌더 옵션.</summary>
    public XkxkUiBuilderOptions Options { get; }

    /// <summary>서비스 조회.</summary>
    public IUiServiceProvider Services { get; }

    /// <summary>진단 모음.</summary>
    public UiDiagnostics Diagnostics { get; }

    /// <summary>구성에 성공한 모듈 ID(등록 순서).</summary>
    public IReadOnlyList<string> ModuleIds { get; }

    /// <summary>Avalonia 앱 설정 뒤 연결되었으면 true(<see cref="XkxkUiAppBuilderExtensions.UseXkxkUi"/>).</summary>
    public bool IsAttached { get; internal set; }
}
