// UI 언어 1건 — 언어 코드·표시 이름을 담는 불변 값, 튜플 대신 확장 가능한 형식(UADR-0011)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>UI 가 지원하는 언어 1건(코드와 표시 이름).</summary>
/// <remarks>
/// <see cref="IUiLocalizer.Languages"/> 의 항목이다. 튜플 대신 클래스로 두어 뒤에 정보를 더할 수 있게 했다.
/// 확장 규칙: 새 정보는 생성자 오버로드와 속성 추가로만 넓힌다(기본값 파라미터 금지).
/// </remarks>
public sealed class UiLanguage
{
    /// <summary>언어를 만든다.</summary>
    /// <param name="code">언어 코드(예 "ko", "en"). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="name">사용자에게 보일 언어 이름(예 "한국어"). null 이면 <see cref="ArgumentNullException"/>.</param>
    public UiLanguage(string code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
    }

    /// <summary>언어 코드(예 "ko", "en").</summary>
    public string Code { get; }

    /// <summary>사용자에게 보일 언어 이름.</summary>
    public string Name { get; }

    /// <summary>코드와 이름으로 분해한다(<c>var (code, name) = language;</c>).</summary>
    /// <param name="code">언어 코드.</param>
    /// <param name="name">언어 이름.</param>
    public void Deconstruct(out string code, out string name)
    {
        code = Code;
        name = Name;
    }
}
