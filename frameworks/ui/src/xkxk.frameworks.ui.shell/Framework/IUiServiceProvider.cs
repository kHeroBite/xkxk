// 서비스 조회 계약 — 프레임워크만 구현, 뒤에 GetAll·키 조회를 추가할 예정(UADR-0009 원칙 3)
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 등록된 서비스를 형식으로 조회한다.
/// </summary>
/// <remarks>
/// 프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다.
/// 확장 규칙: 뒤 batch 에서 여러 개 조회(GetAll)·키 조회 멤버를 추가할 예정이다(멤버 추가 허용).
/// </remarks>
public interface IUiServiceProvider
{
    /// <summary>서비스가 등록되어 있으면 true.</summary>
    bool Contains<TService>() where TService : class;

    /// <summary>서비스를 찾으면 true 와 인스턴스를 돌려준다. 팩토리 등록은 첫 조회 때 1회만 만든다.</summary>
    bool TryGet<TService>([MaybeNullWhen(false)] out TService service) where TService : class;

    /// <summary>서비스를 돌려준다. 없으면 <see cref="System.InvalidOperationException"/>.</summary>
    TService Get<TService>() where TService : class;
}
