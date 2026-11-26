// 메뉴 항목 이동 옵션 객체 — 새 부모와 끼워 넣을 자리를 정한다(UADR-0009 원칙 1·3, UADR-0014)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiMenuRegistry.Move(string, System.Action{UiMenuPlacementOptions})"/> 의 구성 대리자가 채우는 이동 옵션이다.
/// </summary>
/// <remarks>
/// 모든 속성의 기본값은 null(또는 false)이며 지금 부모를 유지한다.
/// 확장 규칙(UADR-0009): 새 배치 방식은 기본값이 이전 동작과 같은 속성을 추가하는 방식으로만 연다.
/// </remarks>
public sealed class UiMenuPlacementOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiMenuPlacementOptions()
    {
    }

    /// <summary>새 부모 메뉴 ID. 기본값 null(지금 부모 유지).</summary>
    /// <remarks>
    /// 새 부모는 등록된 <see cref="UiMenuItemKind.Submenu"/> 항목이어야 하며 옮기는 항목 자신이나 그 자손일 수 없다.
    /// 어긋나면 옮기지 않고 진단(XKUI0410)을 남긴다. 최상위로 옮기려면 <see cref="ToRoot"/> 를 쓴다.
    /// </remarks>
    public string? Parent { get; set; }

    /// <summary>최상위로 옮길지 여부. 기본값 false. true 면 <see cref="Parent"/> 를 무시한다.</summary>
    public bool ToRoot { get; set; }

    /// <summary>이 항목을 바로 뒤에 둘, 새 부모 아래의 메뉴 ID. 기본값 null.</summary>
    /// <remarks>
    /// <see cref="InsertAfter"/> 와 <see cref="InsertBefore"/> 가 둘 다 있으면 <see cref="InsertAfter"/> 를 쓴다.
    /// 둘 다 없으면 새 부모 아래 맨 뒤에 둔다. 대상이 새 부모 아래에 없으면 맨 뒤에 두고 진단(XKUI0404)을 남긴다.
    /// </remarks>
    public string? InsertAfter { get; set; }

    /// <summary>이 항목을 바로 앞에 둘, 새 부모 아래의 메뉴 ID. 기본값 null.</summary>
    /// <remarks>규칙은 <see cref="InsertAfter"/> 와 같다. <see cref="InsertAfter"/> 가 있으면 무시한다.</remarks>
    public string? InsertBefore { get; set; }
}
