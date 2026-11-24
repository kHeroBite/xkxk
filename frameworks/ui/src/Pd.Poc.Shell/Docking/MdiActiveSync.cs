// 분리 창 활성 표시를 활성 문서에 맞춘다 — 분리 창 문서가 활성이면 메인 MDI 제목줄을 끄고, 웹 관리형 분리 창은 활성 문서가 든 창만 켠다(w12 Z4, w13 U1·U2)
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 근거(디컴파일 MdiDocumentWindow.UpdateIsActive): IsActive 는 '소유 dock 의 ActiveDockable == 이 문서' 다. 분리 창이 활성이어도 중앙 문서 영역의 활성 항목은 그대로라 제목줄이 활성색으로 남는다.
/// 웹 관리형 분리 창도 MdiDocumentWindow 이고 DataContext 는 ManagedDockWindowDocument, 소유 dock 은 관리형 창 dock 이다. 메인 MDI 문서를 활성으로 해도 그 dock 의 활성 항목은 그대로라 분리 창이 활성색으로 남는다.
/// 그래서 ① 메인 MDI 창(PocDocument)은 활성 문서가 분리 창에 있으면 끈다 ② 관리형 창(ManagedDockWindowDocument)은 활성 문서가 그 창 안에 있을 때만 켠다.
/// 표시만 바꾸며 Dock 의 활성 항목은 건드리지 않는다. 분리 창이 없으면 트리를 훑지 않는다(PERF-UI). 생성자는 상태만 저장한다.
/// </summary>
internal sealed class MdiActiveSync
{
    private readonly WindowSession _s;
    private bool _suppressed;

    internal MdiActiveSync(WindowSession s) => _s = s;

    internal void Install()
    {
        _s.Owner.ActiveChanged += _ => Sync();
        MdiDocumentWindow.IsActiveProperty.Changed.AddClassHandler<MdiDocumentWindow>((window, _) =>
        {
            // Dock 이 나중에 다시 켜는 경우(문서 창 재부착·활성 항목 변경)도 규칙에 맞지 않으면 끈다
            if (window.IsActive && !DiagDisabled && MustBeInactive(window, _s.Owner.ActiveDocument))
            {
                window.SetCurrentValue(MdiDocumentWindow.IsActiveProperty, false);
            }
        });
    }

    /// <summary>진단 스위치(w13 U4 원인 가르기): true 면 이 서비스가 아무것도 하지 않는다. 제품판에서는 늘 false 다.</summary>
    internal static bool DiagDisabled { get; set; }

    /// <summary>① 메인 MDI 창: 분리 창 문서가 활성인 동안 활성 문서가 아니면 끈다. ② 관리형 분리 창: 활성 문서가 그 창 안에 없으면 끈다. 그 밖은 건드리지 않는다.</summary>
    private bool MustBeInactive(MdiDocumentWindow window, PocDocument? active) => window.DataContext switch
    {
        PocDocument doc => _suppressed && !ReferenceEquals(doc, active),
        ManagedDockWindowDocument managed => active is null || managed.Window?.Layout is not { } layout || !LayoutSerializer.Contains(layout, active),
        _ => false,
    };

    private void Sync()
    {
        if (DiagDisabled)
        {
            return;
        }

        var active = _s.Owner.ActiveDocument;
        var suppress = active is not null && _s.FindWindow(active) is not null;
        if (!suppress && !_suppressed && _s.Root.Windows is not { Count: > 0 })
        {
            return;
        }

        _suppressed = suppress;
        // 숨은 탭 본문 칸 하위는 건너뛴다(TabSlotMarker) — 문서 전환 가속 이후 분리 창이 있으면 탭 전환마다 숨은 문서 전체를 훑었다
        foreach (var window in _s.Factory.DockControls.OfType<Control>().SelectMany(c => TabSlotMarker.Descendants(c).OfType<MdiDocumentWindow>()))
        {
            if (window.DataContext is PocDocument { Owner: IDock owner } doc)
            {
                window.SetCurrentValue(MdiDocumentWindow.IsActiveProperty, ReferenceEquals(owner.ActiveDockable, doc) && (!suppress || ReferenceEquals(doc, active)));
            }
            else if (window.DataContext is ManagedDockWindowDocument)
            {
                // 끈 뒤에는 Dock 이 다시 켜지 않는다(관리형 dock 의 활성 항목이 그대로라 변경 알림이 없다). 그래서 활성 문서가 든 창은 여기서 켠다
                window.SetCurrentValue(MdiDocumentWindow.IsActiveProperty, !MustBeInactive(window, active));
            }
        }
    }
}
