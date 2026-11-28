// 데스크톱 호스트 공개 옵션 — 소비자가 DesktopHost.Run 에 넘기는 값(모듈 등록 등). 확장은 init 속성 추가로만 한다(UADR-0009·UADR-0018)
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// <see cref="DesktopHost.Run(string[], DesktopHostOptions)"/> 에 넘기는 데스크톱 호스트 옵션이다.
/// 모든 속성은 init 전용이고 기본값은 null 이다(null 이면 기존 동작). 새 값은 기본값을 가진 init 속성을 더하는 방식으로만 늘린다(UADR-0009 바이너리 호환, UADR-0018).
/// </summary>
public sealed class DesktopHostOptions
{
    /// <summary>별별UI 빌더 구성(모듈 등록 등). null 이면 모듈을 하나도 등록하지 않는다.</summary>
    /// <example><code>DesktopHost.Run(args, new DesktopHostOptions { ConfigureUi = b => b.AddModule&lt;MyModule&gt;() });</code></example>
    public Action<XkxkUiBuilder>? ConfigureUi { get; init; } = null;

    /// <summary>LocalApplicationData 아래 데이터 폴더 하위 구성(예 "xkxk","frameworks","ui","testbed"). null 이면 기본 구성 그대로다.
    /// 호스트 진입 맨 앞에서 <c>AppDataFolder.UseSegments</c> 를 1회 부른다(규칙·예외는 그 메서드를 따른다).</summary>
    public string[]? DataFolderSegments { get; init; } = null;

    /// <summary>단일 인스턴스 이름의 접두(기본 "xkxk.frameworks.ui"). 값이 있으면 뮤텍스는 "{값}.{채널}.Instance", 활성화 파이프는 "{값}.{채널}.{세션}.Activate" 가 된다. null 이면 기존 이름 그대로다.</summary>
    public string? InstanceName { get; init; } = null;
}
