// WindowCommands 활성화 출처 오버로드 — 화면은 출처(ActivationOrigin)만 넘기고 전환 효과 판정은 컨트롤 계층이 한다(21 설계 §3). 원본 파일 300줄 상한 때문에 나눈 partial
using System;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Docking;

public sealed partial class WindowCommands
{
    /// <summary>지금 진행 중인 활성화의 출처(ActiveChanged 처리 중에 읽는다). 출처 없는 활성화는 Program.</summary>
    public ActivationOrigin CurrentActivationOrigin => Activation.CurrentOrigin;

    /// <summary>출처를 붙인 활성화가 끝난 뒤 (활성 문서, 출처)로 발생한다.</summary>
    public event Action<PocDocument, ActivationOrigin>? ActivatedFrom
    {
        add => Activation.ActivatedFrom += value;
        remove => Activation.ActivatedFrom -= value;
    }

    /// <summary>입력 순간 출처 표식. 600ms 안의 다음 활성 변경이 이 출처를 가져간다(뷰모델·Dock 경유 활성화도 포함).</summary>
    public void MarkActivationOrigin(ActivationOrigin origin) => Activation.MarkOrigin(origin);

    /// <summary>출처를 붙여 활성화한다(제목 탭 → TitleTab, 창 목록 → WindowList, 본문 클릭 → BodyClick).</summary>
    public void Activate(PocDocument document, ActivationOrigin origin) => Activation.WithOrigin(origin, () => Activation.Activate(document));

    /// <summary>출처를 붙인 제목 탭 클릭(활성이면 최소화, 아니면 활성화). 명령은 놓는 순간 실행되므로 누른 시간과 무관하고, 앞선 입력 표식은 이 조작이 대신하므로 지운다.</summary>
    public void ActivateOrMinimize(PocDocument document, ActivationOrigin origin)
    {
        Activation.ClearOrigin();
        Activation.WithOrigin(origin, () => ActivateOrMinimize(document));
    }

    /// <summary>MRU 다음 창(Ctrl+Tab·Ctrl+F6 → Keyboard).</summary>
    public void ActivateNext(ActivationOrigin origin) => Activation.WithOrigin(origin, Activation.ActivateNext);

    /// <summary>조건에 맞는 열린 창 가운데 가장 최근에 활성이던 창(즐겨찾기 클릭 — 22 §4.3). 활성된 적 없으면 열린 순서.</summary>
    public PocDocument? MostRecentOpen(Func<PocDocument, bool> match) =>
        _s.Mru.Where(OpenDocuments.Contains).FirstOrDefault(match) ?? OpenDocuments.FirstOrDefault(match);

    /// <summary>MRU 이전 창.</summary>
    public void ActivatePrevious(ActivationOrigin origin) => Activation.WithOrigin(origin, Activation.ActivatePrevious);
}
