// 진단 심각도 열거형 — 정의 밖 값은 UiDiagnostic 생성자가 Warning 으로 정규화한다(UADR-0009 원칙 4)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>진단 심각도.</summary>
/// <remarks>값은 끝에만 추가하고 기존 숫자는 바꾸지 않는다. 정의 밖 값은 <see cref="Warning"/> 으로 취급한다.</remarks>
public enum UiDiagnosticSeverity
{
    /// <summary>정보.</summary>
    Info = 0,

    /// <summary>경고 — 동작은 계속된다.</summary>
    Warning = 1,

    /// <summary>오류 — 해당 항목은 건너뛰고, 정책이 Throw 면 Build 가 예외를 던진다.</summary>
    Error = 2,
}
