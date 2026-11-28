// Android 호스트 옵션(공개) — 제품 모듈 등록과 임베드 한글 글꼴 URI 를 호스트에 넘기는 init 전용 묶음
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.android;

/// <summary>
/// <see cref="XkxkAndroidApplication{TApp}.CreateHostOptions"/> 가 돌려주는 옵션이다. 모든 값의 기본은 null 이며
/// null 이면 호스트가 해당 동작을 하지 않는다(모듈 0, 시스템 글꼴).
/// </summary>
public sealed class AndroidHostOptions
{
    /// <summary>빌더에 제품 모듈을 등록하는 대리자(예: <c>b =&gt; b.AddModule&lt;MyModule&gt;()</c>). null 이면 모듈 0.</summary>
    public Action<XkxkUiBuilder>? ConfigureUi { get; init; }

    /// <summary>임베드 한글 글꼴의 avares URI(예: <c>avares://&lt;어셈블리&gt;/Assets/Fonts#Noto Sans KR</c>). null 이면 시스템 글꼴을 쓴다.</summary>
    public string? EmbeddedFontUri { get; init; }

    /// <summary>앱 데이터 폴더의 하위 폴더 구성(예: <c>["acme", "app"]</c>). null 이면 기본 xkxk/frameworks/ui 를 쓴다. 호스트가 첫 데이터 폴더 읽기 전에 <c>AppDataFolder.UseSegments</c> 로 넘긴다.</summary>
    public string[]? DataFolderSegments { get; init; }

    /// <summary>빌더 옵션의 ApplicationId(예: Android 패키지 이름). null 이면 기존 값 "xkxk.frameworks.ui" 를 쓴다.</summary>
    public string? ApplicationId { get; init; }
}
