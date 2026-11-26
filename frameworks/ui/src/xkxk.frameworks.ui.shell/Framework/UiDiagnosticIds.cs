// 진단 코드 문자열 상수 — 00xx 모듈·01xx 서비스, 한 번 쓴 번호는 재사용하지 않는다(UADR-0009 원칙 5)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>프레임워크 진단 코드.</summary>
/// <remarks>번호 대역: XKUI00xx 모듈, XKUI01xx 서비스. 한 번 쓴 번호는 폐기되어도 재사용하지 않는다.</remarks>
public static class UiDiagnosticIds
{
    /// <summary>모듈 ID 가 null·공백(Error, 그 모듈은 건너뜀).</summary>
    public const string ModuleIdEmpty = "XKUI0001";

    /// <summary>같은 모듈 ID 가 두 번 등록됨(Error, 뒤 것은 건너뜀).</summary>
    public const string ModuleDuplicate = "XKUI0002";

    /// <summary>모듈 Configure 중 예외(Error, 다음 모듈은 계속).</summary>
    public const string ModuleConfigureFailed = "XKUI0003";

    /// <summary>같은 서비스 형식이 두 번 Add 됨(Warning, 첫 등록 유지).</summary>
    public const string ServiceDuplicate = "XKUI0101";
}
