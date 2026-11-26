// 메뉴 항목의 모바일형 투영 힌트 옵션 — 하단 탭·삼선 서랍에 보일 방식을 정한다(UADR-0009 원칙 1·3, UADR-0014)
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 메뉴 항목이 모바일형 화면(하단 탭·삼선 서랍)에 어떻게 보일지 정하는 힌트다.
/// <see cref="UiMenuItemOptions.Mobile"/> 로 얻어 속성을 바꾼다.
/// </summary>
/// <remarks>
/// 데스크톱 메뉴 동작에는 영향이 없다. 등록 시점 값이 <see cref="UiMenuMobileDefinition"/> 으로 복사되며 이후 변경은 반영되지 않는다.
/// 확장 규칙(UADR-0009): 새 힌트는 기본값이 이전 동작과 같은 속성을 추가하는 방식으로만 연다. 기존 속성의 의미·기본값은 바꾸지 않는다.
/// </remarks>
public sealed class UiMenuMobileOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiMenuMobileOptions()
    {
    }

    /// <summary>모바일 서랍에 넣을지 여부. 기본값 true.</summary>
    /// <remarks>구분선과 섹션은 true 이고 <see cref="EntryId"/> 가 있을 때만 서랍 항목이 된다.</remarks>
    public bool InDrawer { get; set; } = true;

    /// <summary>모바일에서 쓸 수 있는지 여부. 기본값 true.</summary>
    /// <remarks>false 면 서랍에 비활성으로 보이고 사유(<see cref="UnavailableReasonKey"/>)를 함께 보인다.</remarks>
    public bool Available { get; set; } = true;

    /// <summary>쓸 수 없는 사유 문구 키. 기본값 null(<see cref="Available"/>=false 면 "nav.reason.pcTablet").</summary>
    public string? UnavailableReasonKey { get; set; }

    /// <summary>모바일 항목 ID. 기본값 null.</summary>
    /// <remarks>null 이면 자동화 이름이 "shell.mnu" 로 시작할 때 그 뒤 이름, 아니면 메뉴 ID 를 쓴다.</remarks>
    public string? EntryId { get; set; }

    /// <summary>모바일에서 쓸 제목 문구 키. 기본값 null(항목의 제목 문구를 쓴다).</summary>
    public string? TitleKey { get; set; }

    /// <summary>모바일에서 대신 실행할 명령 ID. 기본값 null(<see cref="UiMenuItemOptions.CommandId"/> 를 실행한다).</summary>
    public string? CommandId { get; set; }

    /// <summary><see cref="CommandId"/> 를 실행할 때 넘길 값. 기본값 null.</summary>
    public object? CommandParameter { get; set; }

    /// <summary>하단 탭 순서(0 부터). 기본값 -1(하단 탭에 두지 않음).</summary>
    public int TabOrder { get; set; } = -1;

    /// <summary>하단 탭 제목 문구 키. 기본값 null.</summary>
    public string? TabTitleKey { get; set; }

    /// <summary>하단 탭 아이콘 키. 기본값 null.</summary>
    public string? TabIcon { get; set; }

    /// <summary>이 모바일 항목이 대신하는 데스크톱 화면 요소의 자동화 이름 목록. 기본값 빈 목록(null 아님).</summary>
    /// <remarks>화면에는 보이지 않으며 데스크톱과 모바일의 메뉴 도달 대응을 확인하는 데 쓴다.</remarks>
    public IList<string> CoveredAutomationIds { get; } = new List<string>();
}
