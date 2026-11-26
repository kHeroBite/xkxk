// 호스트 종류 문자열 ID 상수 — 열거형 대신 문자열이라 값 추가가 호환을 깨지 않는다(UADR-0009 원칙 5)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="XkxkUiBuilderOptions.HostKind"/> 에 쓰는 호스트 종류 ID 다.
/// </summary>
/// <remarks>확장 규칙: 새 호스트는 상수를 추가한다. 기존 값의 문자열은 바꾸지 않는다.</remarks>
public static class UiHostKinds
{
    /// <summary>알 수 없음(기본값).</summary>
    public const string Unknown = "unknown";

    /// <summary>데스크톱 호스트.</summary>
    public const string Desktop = "desktop";

    /// <summary>브라우저(WASM) 호스트.</summary>
    public const string Browser = "browser";

    /// <summary>Android 호스트.</summary>
    public const string Android = "android";
}
