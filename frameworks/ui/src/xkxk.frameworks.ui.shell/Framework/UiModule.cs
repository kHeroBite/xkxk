// 모듈 추상 기반 클래스 — 소비자 구현 계약, 뒤 batch 는 기본 구현 있는 가상 멤버만 추가한다(UADR-0009 원칙 4)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 소비자가 상속해 만드는 별별UI 모듈의 기반 클래스다.
/// </summary>
/// <remarks>
/// 확장 규칙(UADR-0009): 이 클래스에는 추상 멤버를 추가하지 않는다.
/// 새 모듈 기능(예 의존 목록·순서·비동기 초기화)은 기본 구현이 있는 <c>virtual</c> 멤버로만 추가해
/// 기존 파생 클래스의 소스·바이너리 호환을 지킨다.
/// </remarks>
public abstract class UiModule : IUiModule
{
    /// <summary>파생 클래스용 생성자.</summary>
    protected UiModule()
    {
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract void Configure(IUiModuleContext context);
}
