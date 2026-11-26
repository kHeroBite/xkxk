// 명령 등록 옵션 객체 — 새 입력은 기본값 있는 속성 추가로만 넓힌다(UADR-0009 원칙 1·3, T-POLICY, T-GESTURE)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiCommandRegistry.Add(string, Action{UiCommandOptions})"/> 의 구성 대리자가 채우는 명령 옵션이다.
/// </summary>
/// <remarks>
/// 모든 속성의 기본값은 null(또는 false)이다. 대리자가 끝난 시점의 값을 레지스트리가 복사해 두므로,
/// 등록 뒤에 이 객체를 고쳐도 등록된 명령은 바뀌지 않는다. 바꾸려면 <see cref="Replace"/> 로 다시 등록한다.
/// 확장 규칙(UADR-0009): 새 기능(예 토글 상태·비동기 실행·여러 단축키)은 기본값이 있는 속성을 추가하는 방식으로만 연다.
/// 기존 속성의 의미·기본값은 바꾸지 않는다.
/// </remarks>
public sealed class UiCommandOptions
{
    /// <summary>기본값으로 옵션을 만든다.</summary>
    public UiCommandOptions()
    {
    }

    /// <summary>제목 문구 키. 기본값 null. 있으면 <see cref="Title"/> 보다 먼저 쓴다.</summary>
    public string? TitleKey { get; set; }

    /// <summary>제목. 기본값 null. 문구 키가 없을 때 그대로 표시한다.</summary>
    public string? Title { get; set; }

    /// <summary>아이콘 키(<see cref="IUiIconRegistry"/> 의 키). 기본값 null(아이콘 없음).</summary>
    /// <remarks>
    /// 해석할 수 없는 키는 등록을 막지 않고 진단(XKUI0304)으로 남긴다.
    /// 빌드 도중 등록한 모듈 명령은 빌드 끝에 모아서, 빌드 뒤·실행 중 등록은 즉시 검사한다.
    /// </remarks>
    public string? Icon { get; set; }

    /// <summary>단축키. 기본값 null(단축키 없음).</summary>
    /// <remarks>
    /// <para>별별UI 단축키 문법으로 적는다. 예 "Ctrl+Shift+N", "Ctrl+F4", "Alt+Enter".</para>
    /// <para>
    /// 문법: 앞뒤 공백을 뺀 문자열을 '+' 로 나눈 구간들이다. 마지막 구간은 키 1개, 앞 구간들은 수정자 0개 이상이다.
    /// 수정자는 Ctrl·Shift·Alt·Meta 이고(대소문자 무시, 각각 한 번만, 순서 무관),
    /// 키는 A~Z · D0~D9 · F1~F24 · NumPad0~NumPad9 · Tab · Enter · Escape · Space · Back · Delete · Insert · Home · End ·
    /// PageUp · PageDown · Left · Up · Right · Down · OemPlus · OemMinus · OemComma · OemPeriod · Add · Subtract · Multiply · Divide 이다(대소문자 무시).
    /// </para>
    /// <para>
    /// 무효인 예: 빈 문자열, 빈 구간("Ctrl++"·"+A"), 수정자만("Ctrl"·"Ctrl+Shift"), 목록 밖 키("Nope"·"999"),
    /// 키 2개("A+B"), 수정자 중복("Ctrl+Ctrl+A"), 구간 안 공백.
    /// 무효인 단축키는 등록을 막지 않고 진단(XKUI0305)으로 남기며, 등록된 <see cref="UiCommandDefinition.Gesture"/> 는 null 이 된다.
    /// </para>
    /// <para>
    /// 이 판에서는 메타데이터로만 보관한다(키 입력 연결은 뒤 판).
    /// 문법 확장은 키를 더하는 추가만 한다. 지금 유효한 문자열이 나중에 무효가 되지 않는다.
    /// </para>
    /// </remarks>
    public string? Gesture { get; set; }

    /// <summary>분류(예 "window"·"layout"). 기본값 null.</summary>
    public string? Category { get; set; }

    /// <summary>설명 문구 키. 기본값 null.</summary>
    public string? DescriptionKey { get; set; }

    /// <summary>실행 대리자. 기본값 null.</summary>
    /// <remarks>
    /// null 이면 등록하지 않고 진단(XKUI0303)으로 남긴다.
    /// 대리자가 던진 예외는 레지스트리가 받아 진단(XKUI0306)으로 남기고 실행 결과를 false 로 돌려준다.
    /// </remarks>
    public Action<UiCommandContext>? Execute { get; set; }

    /// <summary>실행 가능 여부 대리자. 기본값 null(항상 실행 가능).</summary>
    /// <remarks>대리자가 던진 예외는 진단(XKUI0306)으로 남기고 실행할 수 없음(false)으로 본다.</remarks>
    public Func<UiCommandContext, bool>? CanExecute { get; set; }

    /// <summary>같은 ID 의 기존 명령을 교체할지 여부. 기본값 false.</summary>
    /// <remarks>
    /// <para>false(기본): 같은 ID 가 이미 있으면 등록하지 않고 진단(XKUI0302)으로 남긴다.</para>
    /// <para>
    /// true: 같은 ID 가 있으면 교체한다. 교체한 명령은 <see cref="IUiCommandRegistry.Commands"/> 안의 원래 자리를 그대로 지키고(P3),
    /// 등록 주체(<see cref="UiCommandDefinition.Owner"/>)는 교체한 주체로 바뀌어 이후 제거 권한도 그 주체로 옮긴다.
    /// 셸 기본 명령(<see cref="ShellCommandIds"/>)도 이 방식으로 바꿀 수 있다.
    /// </para>
    /// <para>
    /// true 인데 같은 ID 가 없으면 새로 등록한다(P1 — 호출자가 먼저 <see cref="IUiCommandRegistry.Contains(string)"/> 를 볼 필요가 없다).
    /// 다만 프레임워크가 아닌 주체는 예약 접두어 <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작하는 새 ID 를 만들 수 없다(XKUI0308).
    /// </para>
    /// </remarks>
    public bool Replace { get; set; }
}
