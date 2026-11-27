// 진단 코드 문자열 상수 — 00xx 모듈·01xx 서비스·02xx 아이콘·03xx 명령·04xx 메뉴·05xx 창, 한 번 쓴 번호는 재사용하지 않는다(UADR-0009 원칙 5)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>프레임워크 진단 코드.</summary>
/// <remarks>번호 대역: XKUI00xx 모듈, XKUI01xx 서비스, XKUI02xx 아이콘, XKUI03xx 명령, XKUI04xx 메뉴, XKUI05xx 창. 한 번 쓴 번호는 폐기되어도 재사용하지 않는다.</remarks>
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

    /// <summary>프레임워크가 아닌 주체 명령의 단축키가 이미 있는 명령의 단축키와 같음(Warning, 명령은 등록하고 먼저 등록한 쪽이 키를 받음).</summary>
    public const string CommandGestureConflict = "XKUI0310";

    /// <summary>메뉴 ID 가 null·빈 값·공백(Error, 그 등록은 건너뜀).</summary>
    public const string MenuIdEmpty = "XKUI0401";

    /// <summary>같은 ID 의 메뉴 항목을 Replace 없이 다시 등록함(Error, 첫 등록 유지).</summary>
    public const string MenuDuplicate = "XKUI0402";

    /// <summary>부모 메뉴가 없거나 하위 메뉴 종류가 아님(Error, 등록 안 함).</summary>
    public const string MenuParentMissing = "XKUI0403";

    /// <summary>앞·뒤 삽입 기준 항목이 같은 부모 아래에 없음(Warning, 맨 뒤에 붙임).</summary>
    public const string MenuAnchorMissing = "XKUI0404";

    /// <summary>명령 메뉴 항목의 명령 ID 가 명령 레지스트리에 없음(Error, 항목은 유지하고 비활성으로 표시).</summary>
    public const string MenuCommandMissing = "XKUI0405";

    /// <summary>항목 종류와 옵션이 맞지 않음 — 명령 항목에 명령 ID 없음·섹션에 섹션 키 없음·구분선에 명령 ID 있음 등(Error, 등록 안 함).</summary>
    public const string MenuKindInvalid = "XKUI0406";

    /// <summary>프레임워크가 아닌 주체가 아직 없는 예약 접두어(xkxk.) 메뉴 ID 를 새로 만들려 함(Error, 등록 안 함).</summary>
    public const string MenuIdReserved = "XKUI0407";

    /// <summary>다른 주체의 메뉴 항목(또는 다른 주체의 자식이 있는 하위 메뉴)을 지우려 함(Error, 지우지 않음).</summary>
    public const string MenuRemoveDenied = "XKUI0408";

    /// <summary>화면이 모르는 섹션 키(Warning, 화면당 키별 1회, 그 자리는 비움).</summary>
    public const string MenuSectionUnknown = "XKUI0409";

    /// <summary>메뉴 이동 대상이 없거나 순환이 생기거나 새 부모가 하위 메뉴가 아님(Error, 옮기지 않음).</summary>
    public const string MenuMoveInvalid = "XKUI0410";

    /// <summary>메뉴의 표시 조건·툴팁 대리자 예외(Warning, 항목·키당 1회, 조건 예외는 숨김·툴팁 예외는 툴팁 없음).</summary>
    public const string MenuConditionFailed = "XKUI0411";

    /// <summary>창 ID 가 null·빈 값·공백이거나 명령 ID 와 같은 형식 규칙에 맞지 않음(Error, 등록 안 함).</summary>
    public const string WindowIdInvalid = "XKUI0501";

    /// <summary>같은 ID 의 창을 Replace 없이 다시 등록함(첫 등록 유지), 새 창 ID 가 다른 창의 ID·별칭과 대소문자 무시로 겹침, 또는 셸 밖의 등록이 예약어 Grid·Gantt·Form·0·1·2(대소문자 무시)를 창 ID 로 씀(Error, 등록 안 함).</summary>
    public const string WindowDuplicate = "XKUI0502";

    /// <summary>프레임워크가 아닌 주체가 아직 없는 예약 접두어(xkxk.) 창 ID 를 새로 만들려 함(Error, 등록 안 함). 또는 프레임워크가 아닌 주체의 xkxk. 별칭이나 셸 밖 등록의 옛 이름·숫자(Grid·Gantt·Form·0·1·2) 별칭(Error, 그 별칭만 버리고 창은 등록).</summary>
    public const string WindowReservedId = "XKUI0503";

    /// <summary>창 별칭이 다른 창의 ID·별칭과 겹침(Warning, 그 별칭만 버리고 창은 등록).</summary>
    public const string WindowAliasConflict = "XKUI0504";

    /// <summary>등록되지 않은 창을 열거나 복원하려 함(Warning, 자리표시 창으로 대신 표시하고 Open 은 false). 셸의 열기·복원 입구만 보고하며(열기마다 1건, 복원·적용마다 창 ID 당 1건) 화면 층은 보고하지 않음.</summary>
    public const string WindowNotRegistered = "XKUI0505";

    /// <summary>창을 열 셸(앱 화면)이 아직 없음(Warning, 열지 않고 false).</summary>
    public const string WindowShellUnavailable = "XKUI0506";

    /// <summary>창 CreateContent 가 예외를 던지거나 null·Control 이 아닌 값을 돌려줌, 또는 창에 CreateContent 가 없음(B10 이후 셸 기본 창은 없음)(Error, 자리표시 내용으로 대신 표시).</summary>
    public const string WindowContentFailed = "XKUI0507";

    /// <summary>다른 주체가 등록한 창을 지우려 함(Error, 지우지 않음).</summary>
    public const string WindowRemoveDenied = "XKUI0508";

    /// <summary>강타입 창의 열 때 넘긴 인자가 뷰모델이 기대하는 형식이 아님(Error, 자리표시 내용으로 대신 표시).</summary>
    public const string WindowArgumentMismatch = "XKUI0509";

    /// <summary>강타입 창 뷰모델의 OnOpenedAsync 가 예외를 던짐(Error, 내용은 유지).</summary>
    public const string WindowViewModelFailed = "XKUI0510";

    /// <summary>강타입 창 등록에 예열(Prewarm)을 지정함(Warning, Prewarm 을 None 으로 바꿔 등록).</summary>
    public const string WindowPrewarmIgnored = "XKUI0511";
}
