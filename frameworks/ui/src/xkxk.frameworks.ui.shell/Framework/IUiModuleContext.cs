// 모듈 구성 컨텍스트 계약 — 프레임워크만 구현, 앞으로 기능 영역 속성을 추가한다(UADR-0009 원칙 3)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="IUiModule.Configure"/> 에 넘어오는 모듈 구성 컨텍스트다.
/// </summary>
/// <remarks>
/// 프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다(구현하면 이후 판에서 깨질 수 있다).
/// 확장 규칙: 앞으로 아이콘·메뉴·명령 등 기능 영역은 이 인터페이스에 속성을 추가하는 방식으로 연다(멤버 추가 허용).
/// </remarks>
public interface IUiModuleContext
{
    /// <summary>구성 중인 모듈의 ID.</summary>
    string ModuleId { get; }

    /// <summary>빌더 옵션(빌더와 같은 인스턴스).</summary>
    XkxkUiBuilderOptions Options { get; }

    /// <summary>서비스 등록부(빌더와 같은 인스턴스).</summary>
    UiServiceRegistry Services { get; }

    /// <summary>진단 모음(빌더와 같은 인스턴스).</summary>
    UiDiagnostics Diagnostics { get; }

    /// <summary>아이콘 레지스트리(앱 컨텍스트와 같은 저장소).</summary>
    /// <remarks>
    /// 여기서 등록한 항목의 등록 주체는 이 모듈의 ID 다.
    /// 등록 진단은 Build 중이면 빌더 <see cref="Diagnostics"/>, Build 가 끝난 뒤에는 앱 컨텍스트의 Diagnostics 로 간다(B06).
    /// </remarks>
    IUiIconRegistry Icons { get; }

    /// <summary>명령 레지스트리(앱 컨텍스트와 같은 저장소).</summary>
    /// <remarks>
    /// 여기서 등록한 항목의 등록 주체는 이 모듈의 ID 다.
    /// 등록 진단은 Build 중이면 빌더 <see cref="Diagnostics"/>, Build 가 끝난 뒤에는 앱 컨텍스트의 Diagnostics 로 간다(B06).
    /// </remarks>
    IUiCommandRegistry Commands { get; }

    /// <summary>메뉴 레지스트리(앱 컨텍스트와 같은 저장소).</summary>
    /// <remarks>
    /// 여기서 등록한 항목의 등록 주체는 이 모듈의 ID 다.
    /// 등록 진단은 Build 중이면 빌더 <see cref="Diagnostics"/>, Build 가 끝난 뒤에는 앱 컨텍스트의 Diagnostics 로 간다.
    /// 명령 ID 가 없는 메뉴는 모든 모듈 구성이 끝난 Build 끝에 검사한다(B07).
    /// </remarks>
    IUiMenuRegistry Menus { get; }
}
