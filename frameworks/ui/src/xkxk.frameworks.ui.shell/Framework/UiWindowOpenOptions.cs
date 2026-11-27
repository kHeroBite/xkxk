// 창 열기 옵션 객체 — 새 입력은 기본값 있는 속성 추가로만 넓힌다(UADR-0009 원칙 1·3, UADR-0016)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiWindowRegistry.Open(string, System.Action{UiWindowOpenOptions})"/> 의 구성 대리자가 채우는 열기 옵션이다.
/// </summary>
/// <remarks>
/// 모든 속성의 기본값은 "창 정의·현재 설정을 따름"이다. 이번에 여는 문서에만 적용하며 등록된 창 정의는 바꾸지 않는다.
/// 확장 규칙(UADR-0009): 새 기능(예 처음 크기)은 기본값이 있는 속성을 추가하는 방식으로만 연다.
/// 기존 속성의 의미·기본값은 바꾸지 않는다.
/// </remarks>
public sealed class UiWindowOpenOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiWindowOpenOptions()
    {
    }

    /// <summary>문서 제목. 기본값 null(셸이 자동 제목을 붙인다).</summary>
    public string? Title { get; set; }

    /// <summary>배치 방식. 기본값 <see cref="UiWindowPlacement.Default"/>(창 정의를 따른다).</summary>
    /// <remarks>
    /// <see cref="UiWindowPlacement.Sdi"/> 이면 정의와 상관없이 분리창으로 연다.
    /// <see cref="UiWindowPlacement.Default"/> 이면 정의의 <see cref="UiWindowDefinition.Placement"/> 를 따른다.
    /// </remarks>
    public UiWindowPlacement Placement { get; set; } = UiWindowPlacement.Default;

    /// <summary>같은 창의 문서를 여러 개 허용할지 여부. 기본값 null(창 정의·현재 설정을 따른다).</summary>
    /// <remarks>
    /// null 이면 현재 설정을 쓴다. 현재 설정의 처음 값은 창 정의의 <see cref="UiWindowDefinition.AllowDuplicate"/> 와
    /// 저장된 레이아웃의 값이며, 실행 중에 사용자가 바꿀 수 있다.
    /// </remarks>
    public bool? AllowDuplicate { get; set; }

    /// <summary>창 내용에 넘기는 값. 기본값 null.</summary>
    /// <remarks>
    /// <para><see cref="UiWindowContentContext.Parameter"/> 로 내용 생성 함수에 전달된다.</para>
    /// <para>레이아웃 저장본에는 남지 않는다. 레이아웃을 복원한 문서에서는 null 이므로 내용 생성 함수는 null 을 처리해야 한다.</para>
    /// </remarks>
    public object? Parameter { get; set; }
}
