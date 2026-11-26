// 메뉴 항목의 표시 상태 — GetState 가 부를 때마다 평가해 돌려주는 읽기 전용 값(UADR-0009 원칙 1, UADR-0014)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiMenuRegistry.GetState(string, UiMenuSurface)"/> 의 결과다. 평가한 시점의 값이며 이후 바뀌지 않는다.
/// </summary>
/// <remarks>
/// <para>공개 생성자가 없다. 프레임워크(레지스트리)만 만든다.</para>
/// <para>등록되지 않은 ID 를 물으면 <see cref="IsVisible"/> 이 false 인 빈 상태를 돌려준다(예외 없음).</para>
/// <para>확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.</para>
/// </remarks>
public sealed class UiMenuItemState
{
    /// <summary>평가 결과로 상태를 만든다(레지스트리 전용).</summary>
    /// <param name="isVisible">보이는지.</param>
    /// <param name="isEnabled">누를 수 있는지.</param>
    /// <param name="title">표시 제목. 없으면 null.</param>
    /// <param name="icon">아이콘 키. 없으면 null.</param>
    /// <param name="gesture">단축키 표시 문자열. 없으면 null.</param>
    /// <param name="toolTip">툴팁. 없으면 null.</param>
    internal UiMenuItemState(bool isVisible, bool isEnabled, string? title, string? icon, string? gesture, string? toolTip)
    {
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        Title = title;
        Icon = icon;
        Gesture = gesture;
        ToolTip = toolTip;
    }

    /// <summary>보이는지. 숨김이 아니고 표시 조건이 참일 때 true 다. 표시 조건이 예외를 던지면 false 다.</summary>
    public bool IsVisible { get; }

    /// <summary>
    /// 누를 수 있는지. 명령 항목은 명령의 실행 가능 여부이고(명령이 없으면 false), 하위 메뉴·섹션 항목은 true 다.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>표시 제목. 항목 제목, 항목 문구 키의 문구, 명령 제목 순서에서 처음 있는 값이다. 없으면 null.</summary>
    public string? Title { get; }

    /// <summary>아이콘 키. 항목 아이콘이 없으면 명령 아이콘이다. 둘 다 없으면 null.</summary>
    public string? Icon { get; }

    /// <summary>단축키 표시 문자열(명령의 단축키). 없으면 null.</summary>
    public string? Gesture { get; }

    /// <summary>툴팁 대리자의 결과. 대리자가 없거나 예외를 던지면 null.</summary>
    public string? ToolTip { get; }
}
