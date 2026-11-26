// 명령 레지스트리 공개 계약 — 솔루션이 명령을 등록·교체·실행하는 단일 표면(UADR-0013, T-POLICY, T-RESERVED)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 명령을 ID 로 등록·교체·제거·실행하는 레지스트리다.
/// 앱 전역은 <see cref="IUiAppContext.Commands"/>, 모듈 구성 중에는 <see cref="IUiModuleContext.Commands"/> 로 얻는다.
/// </summary>
/// <remarks>
/// <para>프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다.</para>
/// <para>
/// 등록 주체: 모듈 컨텍스트에서 얻은 레지스트리는 그 모듈 ID 를, 앱 컨텍스트에서 얻은 레지스트리는 <see cref="UiRegistryOwners.App"/> 를
/// 등록 주체(<see cref="UiCommandDefinition.Owner"/>)로 기록한다. 같은 저장소를 보므로 어느 쪽에서 등록해도 모두에게 보인다.
/// </para>
/// <para>
/// 예약 접두어: <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 ID 는 프레임워크가 쓴다.
/// 프레임워크가 아닌 주체가 아직 없는 예약 ID 를 새로 만들면 등록하지 않고 진단(XKUI0308)을 남긴다.
/// 이미 있는 예약 ID 를 <see cref="UiCommandOptions.Replace"/> 로 바꾸는 것(재정의)은 허용한다.
/// 그래서 프레임워크가 다음 판에 기본 명령을 더해도 소비자 등록과 충돌하지 않는다.
/// </para>
/// <para>
/// 등록·교체·제거 정책: Replace=true 인데 기존 명령이 없으면 새로 등록한다(P1).
/// 제거는 그 명령을 등록한 주체만 할 수 있다(P2, 다른 주체면 XKUI0309).
/// 교체는 주체와 무관하게 허용하며 <see cref="Commands"/> 안의 자리를 유지한다(P3). 제거 뒤 다시 등록하면 맨 뒤에 붙는다.
/// 빌드가 끝난 뒤 모듈이 붙잡아 둔 레지스트리로 등록하면 실행 중 등록과 같다(P4 — 즉시 검사, 진단은 <see cref="IUiAppContext.Diagnostics"/>, 예외 없음).
/// </para>
/// <para>
/// 스레드: UI 스레드에서 부른다. 레지스트리는 대리자 실행 스레드를 옮기지 않는다.
/// <see cref="Changed"/> 는 내부 잠금 밖에서 발생하며 구독자 예외는 삼키지 않는다.
/// </para>
/// <para>
/// 확장 규칙(UADR-0009): 앞으로의 기능(실행 가능 여부 변경 알림·토글 상태·비동기 실행·여러/화음 단축키)은
/// 멤버 추가와 <see cref="UiCommandOptions"/> 속성 추가로만 연다. 기존 멤버의 서명·의미는 바꾸지 않는다.
/// </para>
/// </remarks>
public interface IUiCommandRegistry
{
    /// <summary>명령을 등록한다.</summary>
    /// <param name="id">명령 ID(대소문자 구분). null 이면 <see cref="ArgumentNullException"/>. 빈·공백 ID 는 진단(XKUI0301) 후 false.</param>
    /// <param name="configure">옵션을 채우는 대리자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 등록(또는 교체)했으면 true. 규칙 위반(빈 ID·중복·실행 대리자 없음·예약 ID)이면 진단을 남기고 false.
    /// 아이콘 키를 해석하지 못했거나(XKUI0304) 단축키가 무효여도(XKUI0305) 명령은 등록되며 true 다.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Add(string id, Action<UiCommandOptions> configure);

    /// <summary>명령을 제거한다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>제거했으면 true. 없으면 false. 다른 주체가 등록한 명령이면 진단(XKUI0309)을 남기고 지우지 않으며 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Remove(string id);

    /// <summary>명령이 등록돼 있는지 확인한다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>등록돼 있으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Contains(string id);

    /// <summary>명령 정의를 찾는다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="command">찾은 정의. 없으면 null.</param>
    /// <returns>찾았으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool TryGet(string id, [NotNullWhen(true)] out UiCommandDefinition? command);

    /// <summary>등록된 명령 전체의 사본. 등록 순서이며 교체는 자리를 유지한다(P3).</summary>
    /// <remarks>부를 때마다 그 시점의 사본을 돌려준다. 이후 등록·제거는 이미 받은 목록에 반영되지 않는다.</remarks>
    IReadOnlyList<UiCommandDefinition> Commands { get; }

    /// <summary>명령을 지금 실행할 수 있는지 확인한다. <see cref="CanExecute(string, object?)"/> 에 parameter=null 을 넘긴 것과 같다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>실행할 수 있으면 true. 없는 ID 면 false(진단 없음).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool CanExecute(string id);

    /// <summary>값을 넘겨 명령을 지금 실행할 수 있는지 확인한다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="parameter">대리자에 넘길 값(<see cref="UiCommandContext.Parameter"/>). null 허용.</param>
    /// <returns>
    /// 실행할 수 있으면 true. 실행 가능 대리자가 없으면 true. 없는 ID 면 false(진단 없음).
    /// 대리자가 예외를 던지면 진단(XKUI0306)을 남기고 false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool CanExecute(string id, object? parameter);

    /// <summary>명령을 실행한다. <see cref="Execute(string, object?)"/> 에 parameter=null 을 넘긴 것과 같다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>실행했으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Execute(string id);

    /// <summary>값을 넘겨 명령을 실행한다.</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="parameter">대리자에 넘길 값(<see cref="UiCommandContext.Parameter"/>). null 허용.</param>
    /// <returns>
    /// 실행 대리자를 끝까지 불렀으면 true.
    /// 없는 ID 면 진단(XKUI0307, Warning)을 남기고 false. 실행할 수 없는 상태면 진단 없이 false.
    /// 대리자가 예외를 던지면 진단(XKUI0306)을 남기고 false(예외를 호출자에게 다시 던지지 않는다).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Execute(string id, object? parameter);

    /// <summary>명령이 등록·교체·제거되면 발생한다. <see cref="UiRegistryChangedEventArgs.Id"/> 는 명령 ID 다.</summary>
    /// <remarks>
    /// 종류는 <see cref="UiRegistryChangeKind.Added"/>·<see cref="UiRegistryChangeKind.Replaced"/>·<see cref="UiRegistryChangeKind.Removed"/> 다.
    /// 모르는 종류를 받으면 전체가 바뀐 것으로 보고 다시 읽는다.
    /// </remarks>
    event EventHandler<UiRegistryChangedEventArgs>? Changed;
}
