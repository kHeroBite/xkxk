// 등록된 명령 1건의 읽기 전용 정의 — 프레임워크만 만든다(UADR-0009 원칙 1·5, T-POLICY)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiCommandRegistry"/> 에 등록된 명령 1건의 정의다. 등록 시점 옵션의 사본이며 바뀌지 않는다.
/// </summary>
/// <remarks>
/// 공개 생성자가 없다. 프레임워크(레지스트리)만 만든다.
/// 확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.
/// </remarks>
public sealed class UiCommandDefinition
{
    /// <summary>등록 옵션을 복사해 정의를 만든다(레지스트리 전용).</summary>
    /// <param name="id">명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="options">구성 대리자가 채운 옵션. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="gesture">검증을 통과한 단축키. 검증에 실패했거나 없으면 null.</param>
    /// <param name="owner">등록 주체(<see cref="UiRegistryOwners"/> 상수 또는 모듈 ID). null 이면 <see cref="ArgumentNullException"/>.</param>
    internal UiCommandDefinition(string id, UiCommandOptions options, string? gesture, string owner)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(owner);
        Id = id;
        TitleKey = options.TitleKey;
        Title = options.Title;
        Icon = options.Icon;
        Gesture = gesture;
        Category = options.Category;
        DescriptionKey = options.DescriptionKey;
        Owner = owner;
        ExecuteHandler = options.Execute;
        CanExecuteHandler = options.CanExecute;
    }

    /// <summary>명령 ID.</summary>
    public string Id { get; }

    /// <summary>제목 문구 키. 없으면 null.</summary>
    public string? TitleKey { get; }

    /// <summary>제목(문구 키가 없을 때 그대로 표시). 없으면 null.</summary>
    public string? Title { get; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 없으면 null.</summary>
    /// <remarks>해석할 수 없는 키여도 그대로 보관한다(진단 XKUI0304). 표시할 때 해석하지 못하면 아이콘 없이 보인다.</remarks>
    public string? Icon { get; }

    /// <summary>단축키(별별UI 단축키 문법 — <see cref="UiCommandOptions.Gesture"/>). 없거나 검증에 실패했으면 null.</summary>
    public string? Gesture { get; }

    /// <summary>분류. 없으면 null.</summary>
    public string? Category { get; }

    /// <summary>설명 문구 키. 없으면 null.</summary>
    public string? DescriptionKey { get; }

    /// <summary>등록 주체. 셸 기본 명령은 <see cref="UiRegistryOwners.Shell"/>, 모듈이 등록하면 모듈 ID, 실행 중 앱이 등록하면 <see cref="UiRegistryOwners.App"/>.</summary>
    /// <remarks><see cref="UiCommandOptions.Replace"/> 로 교체하면 교체한 주체가 된다. 제거는 이 주체만 할 수 있다(XKUI0309).</remarks>
    public string Owner { get; }

    /// <summary>실행 대리자(레지스트리 전용 — 등록 시점 사본).</summary>
    internal Action<UiCommandContext>? ExecuteHandler { get; }

    /// <summary>실행 가능 여부 대리자(레지스트리 전용 — null 이면 항상 가능).</summary>
    internal Func<UiCommandContext, bool>? CanExecuteHandler { get; }
}
