// 빌더 옵션 객체 — 새 설정은 기본값 있는 속성 추가로만 넓힌다(UADR-0009 원칙 1·3)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="XkxkUi.CreateBuilder(XkxkUiBuilderOptions)"/> 에 넘기는 옵션이다.
/// </summary>
/// <remarks>
/// 확장 규칙(UADR-0009): 새 기능은 기본값이 있는 속성을 추가하는 방식으로만 넓힌다.
/// 기존 속성의 의미·기본값은 바꾸지 않는다. 정식 속성이 생기기 전의 실험 값은 <see cref="Properties"/> 에 둔다.
/// </remarks>
public sealed class XkxkUiBuilderOptions
{
    private static readonly IReadOnlyList<string> EmptyArgs = Array.Empty<string>();

    private IReadOnlyList<string> _args = EmptyArgs;
    private string _hostKind = UiHostKinds.Unknown;

    /// <summary>기본값으로 옵션을 만든다.</summary>
    public XkxkUiBuilderOptions()
    {
    }

    /// <summary>명령줄 인자. 기본값은 빈 목록이며 null 을 대입하면 빈 목록이 된다.</summary>
    public IReadOnlyList<string> Args
    {
        get => _args;
        set => _args = value ?? EmptyArgs;
    }

    /// <summary>앱 식별자. 기본값 null(미지정). 별별UI 호스트는 "xkxk.frameworks.ui" 를 준다.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>호스트 값 묶음. 기본값은 빈 묶음(모두 null)이며 대입할 수 없다.</summary>
    public UiHostOptions Host { get; } = new();

    /// <summary>호스트 종류(<see cref="UiHostKinds"/> 상수). 기본값 unknown 이며 null·공백은 unknown 이 된다.</summary>
    public string HostKind
    {
        get => _hostKind;
        set => _hostKind = string.IsNullOrWhiteSpace(value) ? UiHostKinds.Unknown : value;
    }

    /// <summary>구성 오류 처리 정책. 기본값 <see cref="UiErrorPolicy.Collect"/>.</summary>
    public UiErrorPolicy ErrorPolicy { get; set; } = UiErrorPolicy.Collect;

    /// <summary>확장용 이름-값 저장소(키 비교 Ordinal). 정식 속성이 생기기 전 값을 둔다.</summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
