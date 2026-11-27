// 창 내용 생성 문맥 — 내용 생성 대리자에 넘기는 읽기 전용 값(UADR-0009 원칙 1, UADR-0016)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="UiWindowOptions.CreateContent"/> 대리자가 받는 내용 생성 문맥이다.
/// </summary>
/// <remarks>
/// 공개 생성자가 없다. 프레임워크가 내용을 만들 때마다 만든다.
/// 확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다.
/// </remarks>
public sealed class UiWindowContentContext
{
    /// <summary>내용 생성 문맥을 만든다(프레임워크 전용).</summary>
    /// <param name="windowId">창 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="documentId">문서 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="title">문서 제목. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="parameter">열 때 넘긴 값. null 허용.</param>
    /// <param name="window">이 문서의 창 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    internal UiWindowContentContext(string windowId, string documentId, string title, object? parameter, IUiWindowContext window)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(window);
        WindowId = windowId;
        DocumentId = documentId;
        Title = title;
        Parameter = parameter;
        Window = window;
    }

    /// <summary>창 ID(<see cref="UiWindowDefinition.Id"/>). 별칭으로 열었어도 등록된 ID 다.</summary>
    public string WindowId { get; }

    /// <summary>내용을 담을 문서의 ID. 같은 창을 여러 개 열면 문서마다 다르다.</summary>
    public string DocumentId { get; }

    /// <summary>내용을 만드는 시점의 문서 제목.</summary>
    public string Title { get; }

    /// <summary><see cref="UiWindowOpenOptions.Parameter"/> 로 넘긴 값. 넘기지 않았으면 null.</summary>
    /// <remarks>이 값은 레이아웃 저장본에 남지 않는다. 레이아웃을 복원한 문서에서는 null 이다.</remarks>
    public object? Parameter { get; }

    /// <summary>이 문서의 창 문맥. 제목 바꾸기·닫기·바쁨 표시를 한다. 셸이 내용을 만들 때는 null 이 아니다.</summary>
    public IUiWindowContext Window { get; }
}
