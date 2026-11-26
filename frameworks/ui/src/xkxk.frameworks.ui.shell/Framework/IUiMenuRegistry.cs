// 메뉴 레지스트리 공개 계약 — 솔루션이 메뉴를 추가·교체·이동·숨김·조회하는 단일 표면(UADR-0014)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 메뉴 항목을 ID 로 등록·교체·제거·이동·숨김하고 표시 상태를 계산하는 레지스트리다.
/// 앱 전역은 <see cref="IUiAppContext.Menus"/>, 모듈 구성 중에는 <see cref="IUiModuleContext.Menus"/> 로 얻는다.
/// </summary>
/// <remarks>
/// <para>프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다.</para>
/// <para>
/// 등록 주체: 모듈 컨텍스트에서 얻은 레지스트리는 그 모듈 ID 를, 앱 컨텍스트에서 얻은 레지스트리는 <see cref="UiRegistryOwners.App"/> 를
/// 등록 주체(<see cref="UiMenuItemDefinition.Owner"/>)로 기록한다. 셸 기본 메뉴의 주체는 <see cref="UiRegistryOwners.Shell"/> 이다.
/// 같은 저장소를 보므로 어느 쪽에서 등록해도 모두에게 보인다.
/// </para>
/// <para>
/// 예약 접두어: <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 ID 는 프레임워크가 쓴다.
/// 프레임워크가 아닌 주체가 아직 없는 예약 ID 를 새로 만들면 등록하지 않고 진단(XKUI0407)을 남긴다.
/// 이미 있는 예약 ID(<see cref="ShellMenuIds"/>)를 <see cref="UiMenuItemOptions.Replace"/> 로 바꾸는 것(재정의)은 허용한다.
/// </para>
/// <para>
/// 등록·교체·제거 정책: Replace=true 인데 기존 항목이 없으면 새로 등록한다(P1).
/// 제거는 그 항목을 등록한 주체만 할 수 있다(P2, 다른 주체면 XKUI0408).
/// 교체는 주체와 무관하게 허용하며, 부모가 같으면 항목의 자리를 유지한다(P3, 부모를 바꾼 교체는 <see cref="UiMenuItemOptions.Replace"/> 참고).
/// 빌드가 끝난 뒤 모듈이 붙잡아 둔 레지스트리로 등록하면 실행 중 등록과 같다(P4 — 즉시 검사, 진단은 <see cref="IUiAppContext.Diagnostics"/>, 예외 없음).
/// 이동(<see cref="Move"/>)과 숨김(<see cref="SetHidden"/>)은 항목을 지우지 않으므로 주체와 무관하게 허용하며 등록 주체를 바꾸지 않는다.
/// </para>
/// <para>
/// 규칙 위반은 예외 대신 진단(XKUI04xx)을 남기고 false 를 돌려준다. 빌드 도중의 위반은 빌더의 <see cref="UiErrorPolicy"/> 를 따른다.
/// 인자가 null 이면 <see cref="ArgumentNullException"/>(<see cref="GetChildren"/> 의 null 은 최상위를 뜻한다).
/// </para>
/// <para>
/// 스레드: UI 스레드에서 부른다. 레지스트리는 대리자 실행 스레드를 옮기지 않는다.
/// <see cref="Changed"/> 는 내부 잠금 밖에서 발생하며 구독자 예외는 삼키지 않는다.
/// </para>
/// <para>
/// 확장 규칙(UADR-0009): 앞으로의 기능(표시 상태 재평가 요청·메뉴 묶음 이동 등)은
/// 멤버 추가와 옵션 객체(<see cref="UiMenuItemOptions"/>·<see cref="UiMenuPlacementOptions"/>) 속성 추가로만 연다.
/// 기존 멤버의 서명·의미는 바꾸지 않는다.
/// </para>
/// </remarks>
public interface IUiMenuRegistry
{
    /// <summary>메뉴 항목을 등록한다(또는 <see cref="UiMenuItemOptions.Replace"/>=true 로 교체한다).</summary>
    /// <param name="id">메뉴 ID(대소문자 구분). null 이면 <see cref="ArgumentNullException"/>. 빈·공백 ID 는 진단(XKUI0401) 후 false.
    /// 명령 ID 와 같은 문자열이어도 레지스트리가 달라 충돌이 아니다.</param>
    /// <param name="configure">옵션을 채우는 대리자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 등록(또는 교체)했으면 true. 규칙 위반 — 빈 ID(XKUI0401)·중복(XKUI0402, 첫 등록 유지)·부모 없음 또는
    /// 하위 메뉴가 아닌 부모(XKUI0403)·종류와 속성 조합 위반 또는 자식이 있는 하위 메뉴를 다른 종류로 교체(XKUI0406)·
    /// 새 예약 ID(XKUI0407) — 이면 진단을 남기고 false(기존 항목은 그대로다).
    /// 끼워 넣을 기준 항목이 없으면(XKUI0404) 맨 뒤에 붙이고 true 다.
    /// 명령이 아직 없으면(XKUI0405) 항목은 유지하고 true 다. 빌드 중 모듈이 등록한 항목의 명령 검사는 빌드 끝에 한다.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Add(string id, Action<UiMenuItemOptions> configure);

    /// <summary>메뉴 항목을 제거한다. 하위 메뉴는 자식 전원이 같은 주체일 때만 자식째 제거한다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 제거했으면 true. 없으면 false.
    /// 다른 주체가 등록한 항목이거나 다른 주체의 자식이 하나라도 있는 하위 메뉴면 진단(XKUI0408)을 남기고 아무것도 지우지 않으며 false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Remove(string id);

    /// <summary>메뉴 항목이 등록돼 있는지 확인한다. 숨긴 항목도 등록돼 있으면 true 다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>등록돼 있으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool Contains(string id);

    /// <summary>메뉴 항목 정의를 찾는다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="item">찾은 정의. 없으면 null.</param>
    /// <returns>찾았으면 true.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool TryGet(string id, [NotNullWhen(true)] out UiMenuItemDefinition? item);

    /// <summary>등록된 항목 전체의 사본. 트리 깊이 우선 순서(부모 다음에 그 자식들)이며 숨긴 항목도 들어 있다.</summary>
    /// <remarks>부를 때마다 그 시점의 사본을 돌려준다. 이후 변경은 이미 받은 목록에 반영되지 않는다.</remarks>
    IReadOnlyList<UiMenuItemDefinition> Items { get; }

    /// <summary>부모 바로 아래 항목의 사본을 보이는 순서로 돌려준다. 숨긴 항목도 들어 있다.</summary>
    /// <param name="parentId">부모 메뉴 ID. null 이면 최상위 목록.</param>
    /// <returns>자식 목록. 자식이 없거나 없는 ID 면 빈 목록(진단 없음).</returns>
    IReadOnlyList<UiMenuItemDefinition> GetChildren(string? parentId);

    /// <summary>항목의 부모나 자리를 바꾼다. 하위 메뉴는 자식과 함께 옮긴다. 등록 주체와 무관하게 허용한다.</summary>
    /// <param name="id">옮길 메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="configure">이동 옵션을 채우는 대리자. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>
    /// 옮겼으면 true. 대상이 없거나, 자기 자신·자기 자손 아래로 옮기는 순환이거나, 새 부모가 하위 메뉴가 아니면
    /// 진단(XKUI0410)을 남기고 옮기지 않으며 false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 또는 <paramref name="configure"/> 가 null 일 때.</exception>
    bool Move(string id, Action<UiMenuPlacementOptions> configure);

    /// <summary>항목을 숨기거나 다시 보인다. 항목은 지우지 않으며 등록 주체와 무관하게 허용한다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="hidden">true 면 숨김, false 면 숨김 해제. 해제해도 표시 조건(<see cref="UiMenuItemOptions.IsVisible"/>)은 따로 적용된다.</param>
    /// <returns>항목이 있어 숨김 상태를 맞췄으면 true. 없는 ID 면 false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    bool SetHidden(string id, bool hidden);

    /// <summary>항목의 지금 표시 상태를 계산한다. 화면은 메뉴를 열 때마다 부른다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="surface">보는 화면. 표시 조건·툴팁 대리자에 <see cref="UiMenuContext.Surface"/> 로 전달한다.</param>
    /// <returns>
    /// <para>IsVisible: 숨김이 아니고 표시 조건이 true. 표시 조건이 예외를 던지면 진단(XKUI0411, 항목·키당 1회)을 남기고 false.</para>
    /// <para>IsEnabled: 명령 항목은 명령의 실행 가능 여부(<see cref="IUiCommandRegistry.CanExecute(string, object?)"/>, 명령이 없으면 false),
    /// 명령이 아닌 항목(하위 메뉴·구분선·섹션)은 true.</para>
    /// <para>Title: 항목의 제목, 없으면 항목의 문구 키 문구, 없으면 명령 제목. Icon: 항목의 아이콘, 없으면 명령 아이콘.
    /// Gesture: 명령의 단축키. ToolTip: 툴팁 대리자 결과(예외면 null, XKUI0411).</para>
    /// <para>없는 ID 면 예외 없이 IsVisible=false 인 빈 상태다.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> 가 null 일 때.</exception>
    UiMenuItemState GetState(string id, UiMenuSurface surface);

    /// <summary>항목이 등록·교체·제거·이동되거나 숨김 상태가 바뀌면 발생한다. <see cref="UiRegistryChangedEventArgs.Id"/> 는 메뉴 ID 다.</summary>
    /// <remarks>
    /// 종류는 <see cref="UiRegistryChangeKind.Added"/>·<see cref="UiRegistryChangeKind.Replaced"/>·<see cref="UiRegistryChangeKind.Removed"/>·
    /// <see cref="UiRegistryChangeKind.Moved"/>·<see cref="UiRegistryChangeKind.VisibilityChanged"/> 다.
    /// 하위 메뉴를 자식째 제거하거나 옮기면 알림은 그 하위 메뉴 ID 로 1건이다.
    /// 모르는 종류를 받으면 전체가 바뀐 것으로 보고 다시 읽는다.
    /// </remarks>
    event EventHandler<UiRegistryChangedEventArgs>? Changed;
}
