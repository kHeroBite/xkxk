// 윈도우 메뉴 ④~⑩·⑬·U20 의 호스트 계약 — 메인 창·모니터·SDI(분리 창) 일괄 동작과 화면 캡처 방지는 플랫폼 호스트가 실행한다
namespace Pd.Poc.Shell.Docking;

/// <summary>윈도우 메뉴에서 호스트에 넘기는 명령. 셸은 메뉴·설정·저장만 맡고 실행 본체는 호스트가 맡는다.</summary>
public enum WindowHostCommand
{
    /// <summary>④ 메인 윈도우 초기화: 메인 창을 주 모니터 작업 영역 안 기본 위치·크기로 되돌린다(대응 근거 Mars Fn메인윈도우초기화).</summary>
    ResetMainWindow,

    /// <summary>⑤ 싱글 모니터: 메인 창을 Normal 상태로 둔 채 지금 있는 모니터 Bounds 에 맞춘다(OS 최대화 아님, 대응 근거 Mars Fn싱글모니터).</summary>
    FitSingleMonitor,

    /// <summary>⑥ 멀티 모니터: 메인 창을 Normal 상태로 둔 채 모든 모니터 Bounds 의 합집합에 맞춘다(대응 근거 Mars Fn멀티모니터).</summary>
    FitMultiMonitor,

    /// <summary>⑦ 독립모드 위치 초기화: SDI 창 전부를 계단식 기본 위치로 옮긴다(크기 유지·화면 밖 보정, 대응 근거 Mars Fn독립모두위치초기화).</summary>
    ResetSdiPositions,

    /// <summary>⑧ 독립모드 최소화: SDI 창 전부 최소화(대응 근거 Mars Fn독립모두최소화).</summary>
    MinimizeAllSdi,

    /// <summary>⑨ 독립모드 이전 크기: 최소·최대화된 SDI 창 전부를 보통 상태로(대응 근거 Mars Fn독립모두이전크기로).</summary>
    RestoreAllSdi,

    /// <summary>⑩ 독립모드 모두 닫기: SDI 창 전부 닫기, MDI 창은 유지(대응 근거 Mars Fn독립모두닫기).</summary>
    CloseAllSdi,

    /// <summary>U20 선택 모니터로 최대화: '창 ▸ 멀티 모니터' 에서 고른 모니터(저장값, 없으면 주 모니터)의 작업 영역 전체로 메인 창을 OS 최대화한다. 최대화 버튼·더블클릭도 같은 모니터로 간다(호스트 MonitorFitService).</summary>
    MaximizeOnSelectedMonitor,
}

/// <summary>
/// 플랫폼 호스트가 구현해 App.WindowHost 에 넣는다(데스크톱 = odev-4b, 브라우저 관리형 창 = odev-7c).
/// 모든 메서드는 UI 스레드에서 불린다. 예외를 던지지 말고 실패는 false 로 돌려준다(셸이 상태바·로그에 남긴다).
/// </summary>
public interface IWindowHostCommands
{
    /// <summary>이 플랫폼에서 명령을 실행할 수 있는가. false 면 메뉴 항목을 비활성으로 표시한다(예: 브라우저의 멀티 모니터).</summary>
    bool CanExecute(WindowHostCommand command);

    /// <summary>명령 실행. 처리했으면 true. CanExecute=false 인 명령은 호출되지 않는다.</summary>
    bool Execute(WindowHostCommand command);

    /// <summary>⑬ 화면 캡처 방지를 메인 창과 모든 SDI 창에 적용한다. 이후 새로 열리는 SDI 창에도 같은 값을 적용하는 것은 호스트 책임이다. 적용했으면 true.</summary>
    bool SetCaptureProtection(bool enabled);

    /// <summary>캡처 방지의 실제 적용 상태(조회 수단이 없으면 null). 원격 판정용.</summary>
    bool? GetCaptureProtection();

    /// <summary>창 메뉴 '기본 위치로 이동': 메인 창을 복원해 소속 모니터 기본 위치로 옮긴다. 메인 창을 옮길 수 없는 플랫폼은 false.</summary>
    bool MoveMainToDefault();

    /// <summary>창 메뉴 '기본 위치로 이동': SDI 창 하나만 기본 위치로 옮긴다(크기 유지).</summary>
    bool MoveSdiToDefault(Dock.Model.Core.IDockWindow window);
}
