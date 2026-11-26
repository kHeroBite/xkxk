// 메뉴 항목의 모바일형 투영 힌트 읽기 사본 — 프레임워크만 만든다(UADR-0009 원칙 1·5, UADR-0014)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="UiMenuMobileOptions"/> 의 등록 시점 사본이다. 모바일형(하단 탭·삼선 서랍)이 이 값으로 항목을 만든다.
/// </summary>
/// <remarks>
/// <para>공개 생성자가 없다. 프레임워크(레지스트리)만 만든다. 등록 뒤에 옵션 객체를 바꿔도 이 값은 바뀌지 않는다.</para>
/// <para>값은 옵션에 넣은 그대로다. 비어 있는 값의 대체 규칙(항목 ID·사유 문구 등)은 투영하는 쪽이 적용한다.</para>
/// <para>확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.</para>
/// </remarks>
public sealed class UiMenuMobileDefinition
{
    /// <summary>옵션을 복사해 정의를 만든다(레지스트리 전용).</summary>
    /// <param name="options">모바일 옵션. null 이면 <see cref="ArgumentNullException"/>.</param>
    internal UiMenuMobileDefinition(UiMenuMobileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        InDrawer = options.InDrawer;
        Available = options.Available;
        UnavailableReasonKey = options.UnavailableReasonKey;
        EntryId = options.EntryId;
        TitleKey = options.TitleKey;
        CommandId = options.CommandId;
        CommandParameter = options.CommandParameter;
        TabOrder = options.TabOrder;
        TabTitleKey = options.TabTitleKey;
        TabIcon = options.TabIcon;
        CoveredAutomationIds = new List<string>(options.CoveredAutomationIds).AsReadOnly();
    }

    /// <summary>삼선 서랍에 넣는지.</summary>
    public bool InDrawer { get; }

    /// <summary>모바일형에서 쓸 수 있는지. false 면 서랍에 비활성과 사유로 보인다.</summary>
    public bool Available { get; }

    /// <summary>쓸 수 없는 사유 문구 키. 없으면 null(투영하는 쪽의 기본 사유를 쓴다).</summary>
    public string? UnavailableReasonKey { get; }

    /// <summary>모바일 항목 ID. 없으면 null(<see cref="UiMenuItemDefinition.AutomationId"/> 또는 메뉴 ID 에서 정한다).</summary>
    public string? EntryId { get; }

    /// <summary>모바일형 전용 제목 문구 키. 없으면 null(메뉴 항목의 제목을 쓴다).</summary>
    public string? TitleKey { get; }

    /// <summary>모바일형에서 대신 실행할 명령 ID. 없으면 null(<see cref="UiMenuItemDefinition.CommandId"/> 를 실행한다).</summary>
    public string? CommandId { get; }

    /// <summary>모바일형 전용 명령에 넘길 값. 없으면 null.</summary>
    public object? CommandParameter { get; }

    /// <summary>하단 탭 순서. 음수(기본 -1)면 탭에 넣지 않는다.</summary>
    public int TabOrder { get; }

    /// <summary>하단 탭 제목 문구 키. 없으면 null.</summary>
    public string? TabTitleKey { get; }

    /// <summary>하단 탭 아이콘 키. 없으면 null.</summary>
    public string? TabIcon { get; }

    /// <summary>이 항목이 대신하는 데스크톱 화면 자동화 이름 목록. 없으면 빈 목록.</summary>
    public IReadOnlyList<string> CoveredAutomationIds { get; }
}
