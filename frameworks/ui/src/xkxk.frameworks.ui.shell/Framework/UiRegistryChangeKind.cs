// 레지스트리 변경 종류 열거형 — 값은 끝에만 추가한다(UADR-0009 원칙 4, UADR-0013)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>아이콘·명령 레지스트리의 변경 종류.</summary>
/// <remarks>
/// <para>값이 추가될 수 있다. 구독자는 모르는 값을 받으면 전체가 바뀐 것으로 보고 다시 읽는다.</para>
/// <para>값은 끝에만 추가하고 기존 숫자는 바꾸지 않는다.</para>
/// </remarks>
public enum UiRegistryChangeKind
{
    /// <summary>항목이 새로 등록됐다(Replace=true 인데 기존 등록이 없던 경우 포함).</summary>
    Added = 0,

    /// <summary>항목이 제거됐다.</summary>
    Removed = 1,

    /// <summary>같은 키·ID 의 항목이 Replace=true 로 교체됐다.</summary>
    Replaced = 2,

    /// <summary>아이콘 공급자가 추가됐다(<see cref="UiRegistryChangedEventArgs.Id"/> 는 공급자 Id).</summary>
    ProviderAdded = 3,

    /// <summary>아이콘 공급자가 제거됐다(<see cref="UiRegistryChangedEventArgs.Id"/> 는 공급자 Id).</summary>
    ProviderRemoved = 4,
}
