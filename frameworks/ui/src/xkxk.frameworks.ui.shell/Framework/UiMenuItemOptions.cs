// 메뉴 등록 옵션 객체 — 새 입력은 기본값 있는 속성 추가로만 넓힌다(UADR-0009 원칙 1·3, UADR-0014)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiMenuRegistry.Add(string, Action{UiMenuItemOptions})"/> 의 구성 대리자가 채우는 메뉴 항목 옵션이다.
/// </summary>
/// <remarks>
/// 대리자가 끝난 시점의 값(<see cref="Mobile"/> 포함)을 레지스트리가 복사해 두므로,
/// 등록 뒤에 이 객체를 고쳐도 등록된 항목은 바뀌지 않는다. 바꾸려면 <see cref="Replace"/> 로 다시 등록한다.
/// 확장 규칙(UADR-0009): 새 기능(예 토글 표시·배지)은 기본값이 이전 동작과 같은 속성을 추가하는 방식으로만 연다.
/// 기존 속성의 의미·기본값은 바꾸지 않는다.
/// </remarks>
public sealed class UiMenuItemOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiMenuItemOptions()
    {
    }

    /// <summary>부모 메뉴 ID. 기본값 null(최상위).</summary>
    /// <remarks>부모는 이미 등록된 <see cref="UiMenuItemKind.Submenu"/> 항목이어야 한다. 아니면 등록하지 않고 진단(XKUI0403)을 남긴다.</remarks>
    public string? Parent { get; set; }

    /// <summary>항목 종류. 기본값 <see cref="UiMenuItemKind.Command"/>.</summary>
    /// <remarks>
    /// 명령 항목은 <see cref="CommandId"/> 가, 섹션 항목은 <see cref="Section"/> 이 있어야 하고
    /// 명령이 아닌 항목에는 <see cref="CommandId"/> 를 둘 수 없다.
    /// 종류와 속성 조합이 맞지 않으면 등록하지 않고 진단(XKUI0406)을 남긴다.
    /// </remarks>
    public UiMenuItemKind Kind { get; set; }

    /// <summary>실행할 명령 ID(<see cref="IUiCommandRegistry"/>). 기본값 null.</summary>
    /// <remarks>
    /// 명령 레지스트리에 없는 ID 면 항목은 유지하되 비활성으로 보이고 진단(XKUI0405)을 남긴다.
    /// 빌드 도중 등록한 모듈 메뉴는 뒤 모듈이 등록하는 명령을 가리킬 수 있도록 빌드 끝에 모아서 검사한다.
    /// </remarks>
    public string? CommandId { get; set; }

    /// <summary>명령에 넘길 값(<see cref="UiCommandContext.Parameter"/>). 기본값 null.</summary>
    public object? CommandParameter { get; set; }

    /// <summary>제목 문구 키. 기본값 null. <see cref="Title"/> 이 없을 때 쓰며, 둘 다 없으면 명령 제목을 쓴다.</summary>
    public string? TitleKey { get; set; }

    /// <summary>제목. 기본값 null. 있으면 <see cref="TitleKey"/> 와 명령 제목보다 먼저 쓴다.</summary>
    public string? Title { get; set; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 기본값 null(명령 아이콘을 쓴다).</summary>
    public string? Icon { get; set; }

    /// <summary>화면 자동화 이름. 기본값 null("menu." + 메뉴 ID 를 쓴다).</summary>
    /// <remarks>화면의 메뉴 항목과 모바일 서랍 항목의 자동화 이름 근거다.</remarks>
    public string? AutomationId { get; set; }

    /// <summary>이 항목 바로 뒤에 끼워 넣을, 같은 부모 아래의 메뉴 ID. 기본값 null.</summary>
    /// <remarks>
    /// <see cref="InsertAfter"/> 와 <see cref="InsertBefore"/> 가 둘 다 있으면 <see cref="InsertAfter"/> 를 쓴다.
    /// 둘 다 없으면 부모 아래 맨 뒤에 붙는다. 대상이 같은 부모 아래에 없으면 맨 뒤에 붙이고 진단(XKUI0404)을 남긴다.
    /// </remarks>
    public string? InsertAfter { get; set; }

    /// <summary>이 항목 바로 앞에 끼워 넣을, 같은 부모 아래의 메뉴 ID. 기본값 null.</summary>
    /// <remarks>규칙은 <see cref="InsertAfter"/> 와 같다. <see cref="InsertAfter"/> 가 있으면 무시한다.</remarks>
    public string? InsertBefore { get; set; }

    /// <summary>표시 조건 대리자. 기본값 null(항상 보임).</summary>
    /// <remarks>
    /// <see cref="IUiMenuRegistry.GetState(string, UiMenuSurface)"/> 를 부를 때(메뉴를 열 때) 평가한다.
    /// 대리자가 예외를 던지면 숨김으로 보고 진단(XKUI0411, 항목·키당 1회)을 남긴다.
    /// 숨김(<see cref="IUiMenuRegistry.SetHidden(string, bool)"/>)한 항목은 조건과 무관하게 보이지 않는다.
    /// </remarks>
    public Func<UiMenuContext, bool>? IsVisible { get; set; }

    /// <summary>툴팁 대리자. 기본값 null(툴팁 없음).</summary>
    /// <remarks>
    /// 표시 상태를 계산할 때 평가하며 null 을 돌려주면 툴팁이 없다.
    /// 대리자가 예외를 던지면 툴팁 없음으로 보고 진단(XKUI0411, 항목·키당 1회)을 남긴다.
    /// </remarks>
    public Func<UiMenuContext, string?>? ToolTip { get; set; }

    /// <summary>섹션 키(<see cref="UiMenuItemKind.Section"/> 전용). 기본값 null.</summary>
    /// <remarks>
    /// 화면이 이 키에 해당하는 내용을 채운다. 화면이 모르는 키면 그 자리는 비고 진단(XKUI0409)을 남긴다.
    /// 셸 섹션 키는 <see cref="ShellMenuIds"/> 의 Section 상수다.
    /// </remarks>
    public string? Section { get; set; }

    /// <summary>모바일형(하단 탭·삼선 서랍)에 어떻게 보일지 정하는 힌트. 항상 null 이 아니며 속성을 바꿔 쓴다.</summary>
    public UiMenuMobileOptions Mobile { get; } = new UiMenuMobileOptions();

    /// <summary>같은 ID 의 기존 항목을 교체할지 여부. 기본값 false.</summary>
    /// <remarks>
    /// <para>false(기본): 같은 ID 가 이미 있으면 등록하지 않고 진단(XKUI0402)을 남긴다(첫 등록 유지).</para>
    /// <para>
    /// true: 같은 ID 가 있으면 교체한다. <see cref="Parent"/> 가 지금과 같으면 <see cref="InsertAfter"/>·<see cref="InsertBefore"/> 를
    /// 무시하고 원래 자리를 지킨다(P3). <see cref="Parent"/> 가 다르면(null 은 최상위) 새 부모 아래로 옮기며 자리 규칙은 새 등록과 같다.
    /// 새 부모가 없거나 하위 메뉴가 아니거나 자기 자신·자손이면 교체하지 않고 진단(XKUI0403)을 남긴다.
    /// 자식이 있는 하위 메뉴는 다른 종류로 교체할 수 없다(XKUI0406).
    /// </para>
    /// <para>
    /// 교체는 이전 값을 이어받지 않는다. 숨김 상태도 풀리므로 계속 숨기려면 <see cref="IUiMenuRegistry.SetHidden(string, bool)"/> 를 다시 부른다.
    /// 등록 주체(<see cref="UiMenuItemDefinition.Owner"/>)는 교체한 주체로 바뀌어 이후 제거 권한도 그 주체로 옮긴다.
    /// 셸 기본 메뉴(<see cref="ShellMenuIds"/>)도 이 방식으로 바꿀 수 있다.
    /// </para>
    /// <para>
    /// true 인데 같은 ID 가 없으면 새로 등록한다(P1).
    /// 다만 프레임워크가 아닌 주체는 예약 접두어 <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 새 ID 를 만들 수 없다(XKUI0407).
    /// </para>
    /// </remarks>
    public bool Replace { get; set; }
}
