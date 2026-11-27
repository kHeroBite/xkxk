// 창 종류 등록 옵션 객체 — 새 입력은 기본값 있는 속성 추가로만 넓힌다(UADR-0009 원칙 1·3, UADR-0016)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiWindowRegistry.Add(string, Action{UiWindowOptions})"/> 의 구성 대리자가 채우는 창 종류 옵션이다.
/// </summary>
/// <remarks>
/// 기본값은 문자열·대리자가 null, <see cref="Placement"/> 가 <see cref="UiWindowPlacement.Default"/>, 참·거짓 속성이 false,
/// <see cref="Aliases"/> 가 빈 목록이다. 대리자가 끝난 시점의 값을 레지스트리가 복사해 두므로,
/// 등록 뒤에 이 객체를 고쳐도 등록된 창은 바뀌지 않는다. 바꾸려면 <see cref="Replace"/> 로 다시 등록한다.
/// 확장 규칙(UADR-0009): 새 기능(예 기본 크기·열기 가능 조건)은 기본값이 있는 속성을 추가하는 방식으로만 연다.
/// 기존 속성의 의미·기본값은 바꾸지 않는다.
/// </remarks>
public sealed class UiWindowOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiWindowOptions()
    {
    }

    /// <summary>제목 문구 키. 기본값 null. 있으면 <see cref="Title"/> 보다 먼저 쓴다.</summary>
    public string? TitleKey { get; set; }

    /// <summary>제목. 기본값 null. 문구 키가 없을 때 그대로 표시한다. 둘 다 없으면 창 ID 를 표시한다.</summary>
    public string? Title { get; set; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 기본값 null. 연 문서에 따로 정한 아이콘이 없을 때 이 키를 쓴다.</summary>
    public string? Icon { get; set; }

    /// <summary>분류. 기본값 null.</summary>
    public string? Category { get; set; }

    /// <summary>설명 문구 키. 기본값 null. 있으면 창 도움말 문구로 쓴다.</summary>
    public string? DescriptionKey { get; set; }

    /// <summary>이 창을 열 때의 기본 배치. 기본값 <see cref="UiWindowPlacement.Default"/>.</summary>
    /// <remarks>열 때 <see cref="UiWindowOpenOptions.Placement"/> 가 <see cref="UiWindowPlacement.Default"/> 가 아니면 그 값이 우선한다.</remarks>
    public UiWindowPlacement Placement { get; set; }

    /// <summary>이 창의 문서를 같은 제목으로 중복해 열 수 있는지의 처음 값. 기본값 false.</summary>
    /// <remarks>
    /// 실행 중 설정(사용자의 '중복 허용' 전환, 저장된 레이아웃)이 이 값을 바꿀 수 있다.
    /// 한 번의 열기에서만 달리 하려면 <see cref="UiWindowOpenOptions.AllowDuplicate"/> 를 쓴다.
    /// </remarks>
    public bool AllowDuplicate { get; set; }

    /// <summary>창 ID 대신 쓸 수 있는 별칭. 기본값 빈 목록. 항목을 더해 쓴다.</summary>
    /// <remarks>
    /// <para>별칭은 <see cref="IUiWindowRegistry.TryResolve"/>·<see cref="IUiWindowRegistry.Open(string)"/> 에서 대소문자를 무시하고 찾는다.</para>
    /// <para>
    /// 예약어는 별칭으로 쓸 수 없다. 프레임워크가 아닌 주체의 <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로
    /// 시작하는 별칭(대소문자 무시)과, 셸(<see cref="UiRegistryOwners.Shell"/>) 밖 등록의 옛 이름·숫자 Grid·Gantt·Form·0·1·2
    /// (대소문자 무시) 별칭은 그 별칭만 버리고 창은 등록하며 진단(XKUI0503)을 남긴다.
    /// </para>
    /// <para>
    /// 그 밖에 다른 창의 ID·별칭과 겹치는(대소문자 무시) 별칭은 그 별칭만 버리고 창은 등록하며 진단(XKUI0504)을 남긴다.
    /// 빈 값, 자기 ID 와 같은 별칭, 목록 안의 중복은 진단 없이 버린다.
    /// </para>
    /// </remarks>
    public IList<string> Aliases { get; } = new List<string>();

    /// <summary>창 내용을 만드는 함수. 기본값 null.</summary>
    /// <remarks>
    /// <para>반환값은 Avalonia <c>Control</c> 이어야 한다. 이 계약은 UI 라이브러리를 참조하지 않으므로 <c>object</c> 로 받고 화면 층이 검사한다.</para>
    /// <para>예외를 던지거나 null·<c>Control</c> 이 아닌 값을 돌려주면 자리표시 내용을 보이고 진단(XKUI0507)을 남긴다.</para>
    /// <para>
    /// null 로 두면 자리표시 내용을 보이고 진단(XKUI0507)을 남긴다. 셸은 창을 등록하지 않으며 데모 3종의 내용은 시험대 모듈이 이 함수로 채운다(B10).
    /// 솔루션이 등록하는 창은 이 함수를 채운다.
    /// </para>
    /// <para>저장된 레이아웃을 복원한 문서는 <see cref="UiWindowContentContext.Parameter"/> 가 null 이다. null 을 처리해야 한다.</para>
    /// </remarks>
    public Func<UiWindowContentContext, object?>? CreateContent { get; set; }

    /// <summary>같은 ID 의 기존 창을 교체할지 여부. 기본값 false.</summary>
    /// <remarks>
    /// <para>false(기본): 같은 ID 가 이미 있으면 등록하지 않고 진단(XKUI0502)으로 남긴다.</para>
    /// <para>
    /// true: 같은 ID 가 있으면 교체한다. 교체한 창은 <see cref="IUiWindowRegistry.Items"/> 안의 원래 자리를 그대로 지키고(P3),
    /// 등록 주체(<see cref="UiWindowDefinition.Owner"/>)는 교체한 주체로 바뀌어 이후 제거 권한도 그 주체로 옮긴다.
    /// 이미 열린 문서는 그대로이고, 그 뒤의 새 열기와 레이아웃 복원부터 새 정의를 쓴다.
    /// </para>
    /// <para>
    /// true 인데 같은 ID 가 없으면 새로 등록한다(P1). 다만 프레임워크가 아닌 주체는 예약 접두어
    /// <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 새 ID 를 만들 수 없고(XKUI0503),
    /// ID 충돌·예약어 ID 규칙(XKUI0502, <see cref="IUiWindowRegistry.Add(string, Action{UiWindowOptions})"/>)도 그대로 적용한다.
    /// </para>
    /// </remarks>
    public bool Replace { get; set; }
}
