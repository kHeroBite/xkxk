// 별별UI 공개 진입점 — 빌더를 만드는 유일한 정적 팩터리(UADR-0009 원칙 1·2)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 별별UI 프레임워크의 공개 진입점이다. <see cref="XkxkUiBuilder"/> 는 이 형식의 <c>CreateBuilder</c> 로만 만든다.
/// </summary>
/// <remarks>
/// 확장 규칙(UADR-0009): 새 설정은 <see cref="XkxkUiBuilderOptions"/> 에 기본값 있는 속성으로만 추가한다.
/// 기본값 파라미터(선택 파라미터)나 기존 서명 변경은 하지 않는다.
/// </remarks>
public static class XkxkUi
{
    /// <summary>기본 옵션(인자 없음·호스트 unknown·오류 정책 Collect)으로 빌더를 만든다.</summary>
    public static XkxkUiBuilder CreateBuilder()
    {
        return new XkxkUiBuilder(new XkxkUiBuilderOptions());
    }

    /// <summary>명령줄 인자로 빌더를 만든다. 배열은 복사해 보관한다.</summary>
    /// <param name="args">명령줄 인자. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static XkxkUiBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new XkxkUiBuilderOptions
        {
            Args = new List<string>(args).AsReadOnly(),
        };
        return new XkxkUiBuilder(options);
    }

    /// <summary>옵션 객체로 빌더를 만든다. 넘긴 인스턴스를 그대로 쓴다.</summary>
    /// <param name="options">빌더 옵션. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static XkxkUiBuilder CreateBuilder(XkxkUiBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new XkxkUiBuilder(options);
    }
}
