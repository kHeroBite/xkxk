// 아이콘 레지스트리 공개 계약 — 명시 등록·공급자·해석·변경 알림의 단일 표면(UADR-0013)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 아이콘 키를 그림 정의로 해석하는 레지스트리다. <see cref="IUiAppContext"/> 와 <see cref="IUiModuleContext"/> 의 Icons 로 얻는다.
/// </summary>
/// <remarks>
/// <para>프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다(멤버 추가가 호환 변경이다).
/// 앞으로의 확장(공급자 변경 알림·아이콘 격자·다색 등)은 멤버 추가로만 연다.</para>
/// <para>해석 순서: ① 명시 등록(<see cref="Add"/>) ② 공급자(<see cref="Providers"/> 순서) — 첫 일치를 돌려준다.
/// 명시 등록이 공급자 키를 가리는 것은 정상 재정의이며 진단이 없다. 명시 키를 <see cref="Remove"/> 하면 공급자 값이 다시 보인다.</para>
/// <para>예약(T-RESERVED): 아이콘 키는 예약하지 않는다. 프레임워크는 아이콘을 명시 등록하지 않고 공급자로만 더한다.
/// 공급자 Id 의 예약 접두어(<see cref="UiRegistryOwners.ReservedPrefix"/>)는 프레임워크 주체만 쓸 수 있다(아니면 XKUI0206).</para>
/// <para>등록 정책(T-POLICY): P1 <see cref="UiIconOptions.Replace"/>=true 인데 기존 명시 등록이 없으면 새로 등록한다(Added).
/// P2 <see cref="Remove"/>·<see cref="RemoveProvider"/> 는 그 항목을 등록한 주체만 할 수 있다(아니면 XKUI0207, 지우지 않음).
/// Replace=true 교체는 주체와 무관하게 허용되고 등록 주체·제거 권한은 교체한 주체로 옮긴다.
/// P4 빌더 Build 가 끝난 뒤 모듈이 붙잡아 둔 레지스트리로 등록하면 실행 중 등록과 같다 — 즉시 검사, 진단은 앱 진단 모음, 예외 없음.</para>
/// <para>규칙 위반은 예외 대신 진단(XKUI02xx)을 남기고 false 를 돌려준다. 인자가 null 이면 <see cref="ArgumentNullException"/>.
/// <see cref="Changed"/> 는 잠금 밖에서 발생한다.</para>
/// </remarks>
public interface IUiIconRegistry
{
    /// <summary>아이콘을 명시 등록한다(또는 Replace=true 로 교체한다).</summary>
    /// <param name="key">아이콘 키(Ordinal). 비었거나 공백뿐이면 XKUI0201.</param>
    /// <param name="configure">옵션을 채우는 대리자.</param>
    /// <returns>등록·교체했으면 true, 규칙 위반으로 진단을 남겼으면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Add(string key, Action<UiIconOptions> configure);

    /// <summary>SVG path 문법 그림 데이터로 아이콘을 명시 등록한다. <c>Add(key, o =&gt; o.PathData = pathData)</c> 와 같다.</summary>
    /// <param name="key">아이콘 키(Ordinal).</param>
    /// <param name="pathData">SVG path 문법 문자열(24 격자 좌표).</param>
    /// <returns>등록했으면 true, 규칙 위반으로 진단을 남겼으면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 가 null 일 때.</exception>
    bool AddGeometry(string key, string pathData);

    /// <summary>명시 등록 아이콘을 제거한다. 공급자 키만 있으면 false.</summary>
    /// <param name="key">아이콘 키(Ordinal).</param>
    /// <returns>제거했으면 true. 없거나, 다른 주체의 항목이라 거부(XKUI0207)했으면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 가 null 일 때.</exception>
    bool Remove(string key);

    /// <summary>키를 해석할 수 있는지(명시 등록 또는 공급자) 확인한다.</summary>
    /// <param name="key">아이콘 키(Ordinal).</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 가 null 일 때.</exception>
    bool Contains(string key);

    /// <summary>키를 해석해 아이콘 정의를 얻는다. 돌려주는 정의의 Owner 는 등록 주체로 채워져 있다.</summary>
    /// <param name="key">아이콘 키(Ordinal).</param>
    /// <param name="icon">찾으면 정의, 못 찾으면 null.</param>
    /// <returns>찾으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 가 null 일 때.</exception>
    bool TryGet(string key, [NotNullWhen(true)] out UiIconDefinition? icon);

    /// <summary>해석 가능한 전체 키(명시 등록 + 공급자, 중복 제거, Ordinal 정렬). 호출 시점의 사본이다.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>등록된 공급자(우선순위 높은 순 — 나중에 더한 공급자가 앞, 기본 생성 공급자 <see cref="UiRegistryOwners.Generated"/> 가 맨 끝). 호출 시점의 사본이다.</summary>
    IReadOnlyList<UiIconProvider> Providers { get; }

    /// <summary>아이콘 공급자를 맨 앞(가장 높은 우선순위)에 더한다.</summary>
    /// <param name="provider">공급자.</param>
    /// <returns>더했으면 true. Id 가 비었거나 중복(XKUI0205), 예약 Id(XKUI0206)면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> 가 null 일 때.</exception>
    bool AddProvider(UiIconProvider provider);

    /// <summary>공급자를 Id 로 제거한다.</summary>
    /// <param name="providerId">공급자 Id(Ordinal).</param>
    /// <returns>제거했으면 true. 없거나, 다른 주체가 등록한 공급자라 거부(XKUI0207)했으면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerId"/> 가 null 일 때.</exception>
    bool RemoveProvider(string providerId);

    /// <summary>명시 등록·공급자가 바뀌면 발생한다(잠금 밖). 모르는 <see cref="UiRegistryChangeKind"/> 값은 전체 변경으로 본다.</summary>
    event EventHandler<UiRegistryChangedEventArgs>? Changed;
}
