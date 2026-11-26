// 등록된 메뉴 항목 1건의 읽기 전용 정의 — 프레임워크만 만든다(UADR-0009 원칙 1·5, UADR-0014)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiMenuRegistry"/> 에 등록된 메뉴 항목 1건의 정의다. 등록 시점 옵션의 사본이며 바뀌지 않는다.
/// </summary>
/// <remarks>
/// <para>공개 생성자가 없다. 프레임워크(레지스트리)만 만든다.</para>
/// <para>
/// 이동·숨김은 레지스트리 안의 정의를 새 사본으로 바꾼다. 이미 받은 정의는 바뀌지 않으므로
/// 최신 값은 <see cref="IUiMenuRegistry.TryGet"/> 으로 다시 얻는다.
/// </para>
/// <para>확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.</para>
/// </remarks>
public sealed class UiMenuItemDefinition
{
    private const string AutomationIdPrefix = "menu.";

    /// <summary>등록 옵션을 복사해 정의를 만든다(레지스트리 전용). 새 정의는 숨김이 아니다.</summary>
    /// <param name="id">메뉴 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="options">구성 대리자가 채운 옵션. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="owner">등록 주체(<see cref="UiRegistryOwners"/> 상수 또는 모듈 ID). null 이면 <see cref="ArgumentNullException"/>.</param>
    internal UiMenuItemDefinition(string id, UiMenuItemOptions options, string owner)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(owner);
        Id = id;
        Parent = options.Parent;
        Kind = options.Kind;
        CommandId = options.CommandId;
        CommandParameter = options.CommandParameter;
        TitleKey = options.TitleKey;
        Title = options.Title;
        Icon = options.Icon;
        AutomationId = string.IsNullOrWhiteSpace(options.AutomationId) ? AutomationIdPrefix + id : options.AutomationId;
        Section = options.Section;
        Owner = owner;
        VisibilityHandler = options.IsVisible;
        ToolTipHandler = options.ToolTip;
        Mobile = new UiMenuMobileDefinition(options.Mobile);
    }

    private UiMenuItemDefinition(UiMenuItemDefinition source, string? parent, bool isHidden)
    {
        Id = source.Id;
        Parent = parent;
        Kind = source.Kind;
        CommandId = source.CommandId;
        CommandParameter = source.CommandParameter;
        TitleKey = source.TitleKey;
        Title = source.Title;
        Icon = source.Icon;
        AutomationId = source.AutomationId;
        Section = source.Section;
        IsHidden = isHidden;
        Owner = source.Owner;
        VisibilityHandler = source.VisibilityHandler;
        ToolTipHandler = source.ToolTipHandler;
        Mobile = source.Mobile;
    }

    /// <summary>메뉴 ID.</summary>
    public string Id { get; }

    /// <summary>부모 메뉴 ID. 최상위면 null.</summary>
    public string? Parent { get; }

    /// <summary>항목 종류. 화면은 모르는 값을 그리지 않는다.</summary>
    public UiMenuItemKind Kind { get; }

    /// <summary>실행할 명령 ID(<see cref="IUiCommandRegistry"/> 의 ID). 없으면 null.</summary>
    public string? CommandId { get; }

    /// <summary>명령에 넘길 값. 없으면 null. 객체 자체는 복사하지 않고 참조를 보관한다.</summary>
    public object? CommandParameter { get; }

    /// <summary>제목 문구 키. 없으면 null.</summary>
    public string? TitleKey { get; }

    /// <summary>제목(있으면 문구 키보다 먼저 쓴다). 없으면 null.</summary>
    public string? Title { get; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 없으면 null(명령 아이콘을 쓴다).</summary>
    public string? Icon { get; }

    /// <summary>화면 항목·모바일 서랍 항목의 자동화 이름. 옵션에 주지 않았으면 "menu." 뒤에 메뉴 ID 를 붙인 값이다.</summary>
    public string AutomationId { get; }

    /// <summary>화면이 동적으로 채우는 섹션 키. 없으면 null.</summary>
    public string? Section { get; }

    /// <summary><see cref="IUiMenuRegistry.SetHidden"/> 으로 숨긴 항목인지.</summary>
    public bool IsHidden { get; }

    /// <summary>등록 주체. 셸 기본 메뉴는 <see cref="UiRegistryOwners.Shell"/>, 모듈이 등록하면 모듈 ID, 실행 중 앱이 등록하면 <see cref="UiRegistryOwners.App"/>.</summary>
    /// <remarks>
    /// <see cref="UiMenuItemOptions.Replace"/> 로 교체하면 교체한 주체가 된다. 제거는 이 주체만 할 수 있다(XKUI0408).
    /// 이동·숨김은 주체와 무관하게 할 수 있고 이 값을 바꾸지 않는다.
    /// </remarks>
    public string Owner { get; }

    /// <summary>표시 조건 대리자가 있는지.</summary>
    public bool HasVisibilityCondition => VisibilityHandler is not null;

    /// <summary>툴팁 대리자가 있는지.</summary>
    public bool HasToolTip => ToolTipHandler is not null;

    /// <summary>모바일형 투영 힌트. null 이 아니다.</summary>
    public UiMenuMobileDefinition Mobile { get; }

    /// <summary>표시 조건 대리자(레지스트리 전용 — null 이면 항상 보임).</summary>
    internal Func<UiMenuContext, bool>? VisibilityHandler { get; }

    /// <summary>툴팁 대리자(레지스트리 전용 — null 이면 툴팁 없음).</summary>
    internal Func<UiMenuContext, string?>? ToolTipHandler { get; }

    /// <summary>숨김 값만 바꾼 사본을 만든다(레지스트리 전용). 값이 같으면 자신을 돌려준다.</summary>
    /// <param name="isHidden">숨길지.</param>
    internal UiMenuItemDefinition WithHidden(bool isHidden) =>
        isHidden == IsHidden ? this : new UiMenuItemDefinition(this, Parent, isHidden);

    /// <summary>부모만 바꾼 사본을 만든다(레지스트리 전용). 값이 같으면 자신을 돌려준다.</summary>
    /// <param name="parent">새 부모 메뉴 ID. 최상위면 null.</param>
    internal UiMenuItemDefinition WithParent(string? parent) =>
        string.Equals(parent, Parent, StringComparison.Ordinal) ? this : new UiMenuItemDefinition(this, parent, IsHidden);
}
