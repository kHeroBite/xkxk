// 브라우저 호스트 공개 옵션 — 모듈 등록 대리자만 가진 init 전용 옵션 객체(기본값 null, UADR-0018 D1)
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.browser;

/// <summary>
/// <see cref="BrowserHost.RunAsync(string[], BrowserHostOptions)"/> 에 넘기는 옵션이다. 모든 속성은 init 전용이며 기본값은 null 이라, 새 속성을 더해도 기존 호출은 그대로 컴파일·동작한다.
/// </summary>
public sealed class BrowserHostOptions
{
    /// <summary>모듈 등록 대리자. 빌더가 만들어진 직후 호출되며 null 이면 모듈 0 개로 시작한다.</summary>
    public Action<XkxkUiBuilder>? ConfigureUi { get; init; }
}
