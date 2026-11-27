// 창 레지스트리 공개 계약 — 솔루션이 창 종류를 문자열 ID 로 등록·교체·제거·조회·열기하는 단일 표면(UADR-0016)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 창 종류를 ID 로 등록·교체·제거·조회하고 여는 레지스트리다.
/// 앱 전역은 <see cref="IUiAppContext.Windows"/>, 모듈 구성 중에는 <see cref="IUiModuleContext.Windows"/> 로 얻는다.
/// </summary>
/// <remarks>
/// <para>프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다. 앞으로 멤버가 추가될 수 있다.</para>
/// <para>
/// 등록 주체: 모듈 컨텍스트에서 얻은 레지스트리는 그 모듈 ID 를, 앱 컨텍스트에서 얻은 레지스트리는 <see cref="UiRegistryOwners.App"/> 를
/// 등록 주체(<see cref="UiWindowDefinition.Owner"/>)로 기록한다. 셸 기본 창의 주체는 <see cref="UiRegistryOwners.Shell"/> 이다.
/// 같은 저장소를 보므로 어느 쪽에서 등록해도 모두에게 보인다.
/// </para>
/// <para>
/// ID 와 별칭: ID 는 Ordinal(대소문자 구분)로 비교한다. 별칭(<see cref="UiWindowOptions.Aliases"/>)은 대소문자를 무시하며
/// 창을 찾을 때는 <see cref="TryResolve"/> 와 열기(<see cref="Open(string)"/>)만 본다.
/// ID 와 별칭은 한 이름공간이라 새 ID 는 다른 창의 ID·별칭과 대소문자 무시로 겹칠 수 없다(XKUI0502).
/// </para>
/// <para>
/// 예약 접두어: <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 ID 와 별칭은 프레임워크가 쓴다.
/// 프레임워크가 아닌 주체가 아직 없는 예약 ID 를 새로 만들면 등록하지 않고, 예약 별칭을 더하면 그 별칭만 버리고 등록한다
/// (둘 다 진단 XKUI0503). 이미 있는 예약 ID 를 <see cref="UiWindowOptions.Replace"/> 로 바꾸는 것(재정의)은 허용한다.
/// </para>
/// <para>
/// 옛 이름·숫자: Grid·Gantt·Form(대소문자 무시)과 "0"·"1"·"2" 는 셸 기본 창 3종의 영구 예약어다.
/// 셸(<see cref="UiRegistryOwners.Shell"/>) 밖의 등록은 이를 창 ID 로 쓸 수 없고(XKUI0502, 등록 안 함),
/// 별칭으로 쓰면 그 별칭만 버린다(XKUI0503, 창은 등록).
/// </para>
/// <para>
/// 등록·교체·제거 정책: Replace=true 인데 기존 창이 없으면 새로 등록한다(P1).
/// 제거는 그 창을 등록한 주체만 할 수 있다(P2, 다른 주체면 XKUI0508).
/// 교체는 주체와 무관하게 허용하며 <see cref="Items"/> 안의 자리를 유지한다(P3). 제거 뒤 다시 등록하면 맨 뒤에 붙는다.
/// 빌드가 끝난 뒤 모듈이 붙잡아 둔 레지스트리로 등록하면 실행 중 등록과 같다(P4 — 즉시 검사, 진단은 <see cref="IUiAppContext.Diagnostics"/>, 예외 없음).
/// </para>
/// <para>
/// 열린 문서: 교체·제거는 이미 열린 문서를 닫거나 바꾸지 않는다. 그 뒤의 새 열기와 레이아웃 복원부터
/// 교체는 새 정의를, 제거는 자리표시(XKUI0505)를 쓴다.
/// </para>
/// <para>
/// 규칙 위반은 예외 대신 진단(XKUI05xx)을 남기고 false 를 돌려준다. 다만 별칭만 버리는 위반은 창을 등록하고 true 다.
/// 인자가 null 이면 <see cref="ArgumentNullException"/> 이다.
/// </para>
/// <para>
/// 스레드: UI 스레드에서 부른다. 레지스트리는 대리자 실행 스레드를 옮기지 않는다.
/// <see cref="Changed"/> 는 내부 잠금 밖에서 발생하며 구독자 예외는 삼키지 않는다.
/// </para>
/// <para>
/// 확장 규칙(UADR-0009): 앞으로의 기능(예 강타입 등록·창 문맥)은 멤버 추가와
/// 옵션 객체(<see cref="UiWindowOptions"/>·<see cref="UiWindowOpenOptions"/>) 속성 추가로만 연다.
/// 기존 멤버의 서명·의미는 바꾸지 않는다.
/// </para>
/// </remarks>
public interface IUiWindowRegistry
{
    /// <summary>창 종류를 등록한다(또는 <see cref="UiWindowOptions.Replace"/>=true 로 교체한다).</summary>
    /// <param name="id">창 ID(대소문자 구분). null 이면 <see cref="ArgumentNullException"/>.
    /// 빈·공백이거나 형식(명령 ID 와 같은 규칙)을 어긴 ID 는 진단(XKUI0501) 후 false.</param>
    /// <param name="configure">옵션을 채우는 대리자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 등록(또는 교체)했으면 true. 다음은 등록하지 않고 진단을 남기며 false 다 — 무효 ID(XKUI0501),
    /// 같은 ID 를 Replace 없이 다시 등록(XKUI0502, 첫 등록 유지), 새 ID 가 다른 창의 ID·별칭과 대소문자 무시로 겹침(XKUI0502),
    /// 셸 밖의 등록이 예약어 Grid·Gantt·Form·0·1·2(대소문자 무시)를 ID 로 씀(XKUI0502),
    /// 프레임워크가 아닌 주체가 새 예약 접두어 ID 를 만듦(XKUI0503).
    /// 예약 별칭(XKUI0503)과 겹치는 별칭(XKUI0504)은 그 별칭만 버리고 등록하며 true 다.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Add(string id, Action<UiWindowOptions> configure);

    /// <summary>창 종류를 제거한다. 그 창의 별칭도 함께 없어진다. 이미 열린 문서는 그대로 둔다.</summary>
    /// <param name="id">창 ID(별칭은 받지 않는다). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>제거했으면 true. 없으면 false(진단 없음). 다른 주체가 등록한 창이면 진단(XKUI0508)을 남기고 지우지 않으며 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Remove(string id);

    /// <summary>창 종류가 등록돼 있는지 확인한다.</summary>
    /// <param name="id">창 ID(별칭은 보지 않는다). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>등록돼 있으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Contains(string id);

    /// <summary>ID 로 창 정의를 찾는다. 별칭은 보지 않는다.</summary>
    /// <param name="id">창 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="window">찾은 정의. 없으면 null.</param>
    /// <returns>찾았으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool TryGet(string id, [NotNullWhen(true)] out UiWindowDefinition? window);

    /// <summary>ID·별칭·옛 이름·숫자로 창 정의를 찾는다. ID(대소문자 구분) → 별칭(대소문자 무시) → 옛 이름·숫자 순서로 본다.</summary>
    /// <remarks>
    /// 옛 이름 Grid·Gantt·Form(대소문자 무시)과 숫자 "0"·"1"·"2" 는 언제나 셸 기본 창 3종
    /// (xkxk.demo.grid·xkxk.demo.gantt·xkxk.demo.form)의 ID 로 풀며, 정의의 별칭 목록과 무관하다.
    /// 셸 기본 창을 교체하면 새 정의를 찾고, 제거하면 찾지 못한다.
    /// </remarks>
    /// <param name="idOrAlias">창 ID, 별칭, 옛 이름 또는 숫자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="window">찾은 정의. 없으면 null.</param>
    /// <returns>찾았으면 true(진단 없음).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="idOrAlias"/> 가 null 일 때.</exception>
    bool TryResolve(string idOrAlias, [NotNullWhen(true)] out UiWindowDefinition? window);

    /// <summary>등록된 창 정의 전체의 사본. 등록 순서이며 교체는 자리를 유지한다(P3).</summary>
    /// <remarks>부를 때마다 그 시점의 사본을 돌려준다. 이후 등록·제거는 이미 받은 목록에 반영되지 않는다.</remarks>
    IReadOnlyList<UiWindowDefinition> Items { get; }

    /// <summary>창을 정의의 기본값으로 연다.</summary>
    /// <param name="id">창 ID·별칭·옛 이름·숫자(<see cref="TryResolve"/> 와 같게 찾는다). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 셸에 열기를 요청했으면 true. 중복을 허용하지 않고 제목을 지정해 열 때 같은 창·같은 제목의 문서가 이미 있으면
    /// 새로 만들지 않고 그 문서를 활성화한다(이때도 true).
    /// 등록되지 않은 창이면 진단(XKUI0505)을, 셸이 아직 준비되지 않았으면 진단(XKUI0506)을 남기고 false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Open(string id);

    /// <summary>열기 옵션을 정해 창을 연다.</summary>
    /// <param name="id">창 ID·별칭·옛 이름·숫자(<see cref="TryResolve"/> 와 같게 찾는다). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="configure">열기 옵션을 채우는 대리자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns><see cref="Open(string)"/> 과 같다.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Open(string id, Action<UiWindowOpenOptions> configure);

    /// <summary>창 종류가 등록·교체·제거되면 발생한다. <see cref="UiRegistryChangedEventArgs.Id"/> 는 창 ID 다.</summary>
    /// <remarks>
    /// 종류는 <see cref="UiRegistryChangeKind.Added"/>·<see cref="UiRegistryChangeKind.Replaced"/>·<see cref="UiRegistryChangeKind.Removed"/> 다.
    /// 모르는 종류를 받으면 전체가 바뀐 것으로 보고 다시 읽는다.
    /// </remarks>
    event EventHandler<UiRegistryChangedEventArgs>? Changed;
}
