// 등록된 창 종류 1건의 읽기 전용 정의 — 프레임워크만 만든다(UADR-0009 원칙 1·5, UADR-0016)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiWindowRegistry"/> 에 등록된 창 종류 1건의 정의다. 등록 시점 옵션의 사본이며 바뀌지 않는다.
/// </summary>
/// <remarks>
/// 공개 생성자가 없다. 프레임워크(레지스트리)만 만든다.
/// 확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.
/// </remarks>
public sealed class UiWindowDefinition
{
    /// <summary>등록 옵션과 받아들인 별칭으로 정의를 만든다(레지스트리 전용).</summary>
    /// <param name="id">창 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="owner">등록 주체(<see cref="UiRegistryOwners"/> 상수 또는 모듈 ID). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="options">구성 대리자가 채운 옵션. null 이면 <see cref="ArgumentNullException"/>. 이 생성자는 옵션의 별칭을 읽지 않는다.</param>
    /// <param name="aliases">등록이 받아들인 별칭. null 이면 <see cref="ArgumentNullException"/>. 사본을 보관한다.</param>
    internal UiWindowDefinition(string id, string owner, UiWindowOptions options, IEnumerable<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(aliases);
        Id = id;
        Owner = owner;
        TitleKey = options.TitleKey;
        Title = options.Title;
        Icon = options.Icon;
        Category = options.Category;
        DescriptionKey = options.DescriptionKey;
        Placement = options.Placement;
        AllowDuplicate = options.AllowDuplicate;
        Aliases = new List<string>(aliases).AsReadOnly();
        CreateContent = options.CreateContent;
    }

    /// <summary>창 ID(대소문자 구분).</summary>
    public string Id { get; }

    /// <summary>제목 문구 키. 없으면 null.</summary>
    public string? TitleKey { get; }

    /// <summary>제목(문구 키가 없을 때 그대로 표시). 없으면 null.</summary>
    public string? Title { get; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 없으면 null.</summary>
    public string? Icon { get; }

    /// <summary>분류. 없으면 null.</summary>
    public string? Category { get; }

    /// <summary>설명(도움말) 문구 키. 없으면 null.</summary>
    public string? DescriptionKey { get; }

    /// <summary>이 창을 열 때의 기본 배치.</summary>
    public UiWindowPlacement Placement { get; }

    /// <summary>등록 때 정한 중복 허용 처음 값. 실행 중에 바뀐 현재 설정은 이 값에 반영되지 않는다.</summary>
    public bool AllowDuplicate { get; }

    /// <summary>등록이 받아들인 별칭(읽기 전용 사본). 없으면 빈 목록.</summary>
    /// <remarks>
    /// <see cref="IUiWindowRegistry.TryResolve"/> 가 대소문자를 무시하고 찾는 이름이다.
    /// 충돌(XKUI0504)·예약(XKUI0503)으로 버려진 별칭은 들어 있지 않다.
    /// 옛 이름·숫자 해석은 이 목록과 무관하게 동작한다.
    /// </remarks>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>창 내용 생성 함수(<see cref="UiWindowOptions.CreateContent"/>). 없으면 null.</summary>
    public Func<UiWindowContentContext, object?>? CreateContent { get; }

    /// <summary>등록 주체. 셸 기본 창은 <see cref="UiRegistryOwners.Shell"/>, 모듈이 등록하면 모듈 ID, 실행 중 앱이 등록하면 <see cref="UiRegistryOwners.App"/>.</summary>
    /// <remarks><see cref="UiWindowOptions.Replace"/> 로 교체하면 교체한 주체가 된다. 제거는 이 주체만 할 수 있다(XKUI0508).</remarks>
    public string Owner { get; }
}
