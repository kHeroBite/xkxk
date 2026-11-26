// 진단 코드 문자열 상수 — 00xx 모듈·01xx 서비스·02xx 아이콘·03xx 명령, 한 번 쓴 번호는 재사용하지 않는다(UADR-0009 원칙 5)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>프레임워크 진단 코드.</summary>
/// <remarks>번호 대역: XKUI00xx 모듈, XKUI01xx 서비스, XKUI02xx 아이콘, XKUI03xx 명령. 한 번 쓴 번호는 폐기되어도 재사용하지 않는다.</remarks>
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

    /// <summary>아이콘 키가 null·빈 값·공백(Error, 그 등록은 건너뜀).</summary>
    public const string IconKeyEmpty = "XKUI0201";

    /// <summary>같은 키의 아이콘을 Replace 없이 다시 명시 등록함(Error, 첫 등록 유지).</summary>
    public const string IconDuplicate = "XKUI0202";

    /// <summary>아이콘 옵션의 PathData·ResourceKey 가 0개 또는 2개(Error, 등록 안 함).</summary>
    public const string IconSourceInvalid = "XKUI0203";

    /// <summary>해석 시 PathData 그리기 실패(Warning, 키당 1회, 실행 중 진단).</summary>
    public const string IconRenderFailed = "XKUI0204";

    /// <summary>아이콘 공급자 Id 가 빈 값이거나 이미 같은 Id 공급자가 있음(Error, 등록 안 함).</summary>
    public const string IconProviderRejected = "XKUI0205";

    /// <summary>프레임워크가 아닌 주체가 예약 접두어(xkxk.) 공급자 Id 를 등록함(Error, 등록 안 함).</summary>
    public const string IconProviderIdReserved = "XKUI0206";

    /// <summary>다른 주체가 등록한 명시 아이콘·공급자를 지우려 함(Error, 지우지 않음).</summary>
    public const string IconRemoveDenied = "XKUI0207";

    /// <summary>명령 ID 가 null·빈 값·공백(Error, 그 등록은 건너뜀).</summary>
    public const string CommandIdEmpty = "XKUI0301";

    /// <summary>같은 ID 의 명령을 Replace 없이 다시 등록함(Error, 첫 등록 유지).</summary>
    public const string CommandDuplicate = "XKUI0302";

    /// <summary>명령 옵션에 Execute 가 없음(Error, 등록 안 함).</summary>
    public const string CommandExecuteMissing = "XKUI0303";

    /// <summary>명령의 아이콘 키를 해석하지 못함(Error, 명령은 유지하고 아이콘 없이 표시).</summary>
    public const string CommandIconMissing = "XKUI0304";

    /// <summary>명령 단축키가 별별UI 단축키 문법에 맞지 않음(Error, 단축키만 버리고 명령 유지).</summary>
    public const string CommandGestureInvalid = "XKUI0305";

    /// <summary>명령 실행 중 Execute·CanExecute 대리자 예외(Error, 실행 결과 false).</summary>
    public const string CommandFailed = "XKUI0306";

    /// <summary>없는 ID 의 명령을 실행하려 함(Warning, 실행 중 진단).</summary>
    public const string CommandNotFound = "XKUI0307";

    /// <summary>프레임워크가 아닌 주체가 아직 없는 예약 접두어(xkxk.) 명령 ID 를 새로 만들려 함(Error, 등록 안 함).</summary>
    public const string CommandIdReserved = "XKUI0308";

    /// <summary>다른 주체가 등록한 명령을 지우려 함(Error, 지우지 않음).</summary>
    public const string CommandRemoveDenied = "XKUI0309";
}
