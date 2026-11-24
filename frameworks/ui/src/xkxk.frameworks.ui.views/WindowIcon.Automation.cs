// WindowIcon 자동화 피어 — 불러오는 중일 때만 이름('불러오는 중'·'오래 걸리는 중')을 가진 그림 요소로 UIA 트리에 드러낸다(K8·AC-020, plan/09_화면설계/23 §3)
using Avalonia.Automation.Peers;

namespace xkxk.frameworks.ui.views;

public sealed partial class WindowIcon
{
    /// <summary>
    /// 기본 Control 피어는 NoneAutomationPeer(형식 None, 컨트롤·내용 요소 아님 — Avalonia 12.1.4 디컴파일 확인)라
    /// UIA 트리에 나오지 않아 Apply 가 건 AutomationProperties.Name 을 화면 읽기 프로그램이 읽지 못했다.
    /// 불러오는 중(IsBusyShown)일 때만 Image 형 컨트롤 요소로 드러내고, 평소에는 예전처럼 숨긴다(이름 없는 아이콘을 늘리지 않는다).
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new WindowIconAutomationPeer(this);

    private sealed class WindowIconAutomationPeer : ControlAutomationPeer
    {
        public WindowIconAutomationPeer(WindowIcon owner)
            : base(owner)
        {
        }

        private bool Busy => Owner is WindowIcon { IsBusyShown: true };

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override bool IsControlElementCore() => Busy;

        protected override bool IsContentElementCore() => Busy;
    }
}
