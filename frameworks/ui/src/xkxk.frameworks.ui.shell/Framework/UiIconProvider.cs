// 아이콘 공급자 추상 기반 — 소비자 구현 계약, 뒤 batch 는 기본 구현 있는 가상 멤버만 추가한다(UADR-0009 원칙 4, UADR-0013)
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 키 묶음을 한꺼번에 공급하는 아이콘 공급자의 기반 클래스다. 소비자가 상속해 만든다.
/// </summary>
/// <remarks>
/// <para><see cref="IUiIconRegistry.AddProvider(UiIconProvider)"/> 로 등록한다. 해석 순서는 명시 등록이 먼저이고,
/// 그다음 <see cref="IUiIconRegistry.Providers"/> 순서(나중에 더한 공급자가 앞)대로 <see cref="TryGetIcon"/> 를 묻는다.
/// 공급자가 돌려준 정의의 등록 주체는 레지스트리가 이 공급자의 <see cref="Id"/> 로 맞춘다.</para>
/// <para>등록 규칙: <see cref="Id"/> 가 비었거나 같은 Id 공급자가 이미 있으면 XKUI0205,
/// 프레임워크 아닌 주체가 예약 접두어(<see cref="UiRegistryOwners.ReservedPrefix"/>) Id 를 쓰면 XKUI0206 진단을 남기고 등록하지 않는다.</para>
/// <para>구현 규칙: <see cref="Id"/> 와 <see cref="Keys"/> 는 등록 뒤 바뀌지 않는 것으로 본다.
/// 여러 스레드에서 불릴 수 있으므로 읽기 전용 자료로 답하는 것을 권한다.</para>
/// <para>확장 규칙(UADR-0009): 이 클래스에는 추상 멤버를 추가하지 않는다. 새 기능(예 키 목록 변경 알림)은
/// 기본 구현이 있는 <c>virtual</c> 멤버로만 더해 기존 파생 클래스의 소스·바이너리 호환을 지킨다.</para>
/// </remarks>
public abstract class UiIconProvider
{
    /// <summary>파생 클래스용 생성자.</summary>
    protected UiIconProvider()
    {
    }

    /// <summary>공급자 Id(Ordinal 비교, 레지스트리 안에서 유일). 공급자 아이콘의 등록 주체로도 쓰인다.</summary>
    public abstract string Id { get; }

    /// <summary>이 공급자가 답할 수 있는 아이콘 키 전체.</summary>
    public abstract IReadOnlyCollection<string> Keys { get; }

    /// <summary>키에 해당하는 아이콘 정의를 찾는다.</summary>
    /// <param name="key">아이콘 키(Ordinal).</param>
    /// <param name="icon">찾으면 정의, 못 찾으면 null.</param>
    /// <returns>찾으면 true.</returns>
    public abstract bool TryGetIcon(string key, [NotNullWhen(true)] out UiIconDefinition? icon);
}
