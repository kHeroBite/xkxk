// 모듈 계약 인터페이스 — 동결(멤버 추가 금지), 소비자는 UiModule 을 상속한다(UADR-0009 원칙 4)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 별별UI 모듈의 최소 계약이다.
/// </summary>
/// <remarks>
/// <para>동결: 이 인터페이스에는 멤버를 추가하지 않는다. 멤버 추가는 기존 구현체를 깨뜨린다.</para>
/// <para>소비자는 이 인터페이스를 직접 구현하지 말고 <see cref="UiModule"/> 을 상속한다.
/// <see cref="XkxkUiBuilder.AddModule(UiModule)"/> 는 <see cref="UiModule"/> 만 받는다.</para>
/// <para>모듈 기능 확장은 <see cref="UiModule"/> 의 기본 구현 있는 가상 멤버 또는 별도 선택 인터페이스로 한다.</para>
/// </remarks>
public interface IUiModule
{
    /// <summary>모듈 ID(Ordinal 비교). null·공백이면 XKUI0001 진단으로 건너뛴다.</summary>
    string Id { get; }

    /// <summary>모듈을 구성한다. <see cref="XkxkUiBuilder.Build"/> 가 등록 순서대로 1회 호출한다.</summary>
    /// <param name="context">이 모듈 전용 구성 컨텍스트.</param>
    void Configure(IUiModuleContext context);
}
