// 알림 패널 화면 코드 — Application 수준 DataTemplate 1회 등록, Dock 밖에 놓이면 자체 머리줄을 보인다
using Pd.Poc.Shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.ViewModels;
#if PD_DIAGNOSTICS
using Pd.Poc.Shell.Diagnostics;
using Pd.Ui.Views.Diagnostics; // NotificationPanelDiag(진단 빌드 전용)
#endif

namespace Pd.Ui.Views;

public partial class NotificationPanelView : UserControl
{
    private static bool s_registered;
    private bool _dockHosted;

#if PD_DIAGNOSTICS
    // 진단 계측(NotificationPanelDiag) — 필드 초기화자로 만든다(base 생성자보다 먼저 실행되어 어느 시점에도 null 이 아니다)
    private readonly NotificationPanelDiag _diag = new();
#endif

    public NotificationPanelView()
    {
        InitializeComponent();
#if PD_DIAGNOSTICS
        NotificationPanelDiag.OnCreated(); // 진단 빌드 전용: 생성 추적
        _diag.TraceHost(this); // 진단 빌드 전용: 부착·분리 때 조상 사슬 기록(고아 화면 출처 추적)
#endif
        AttachedToVisualTree += (_, _) =>
        {
            // Dock 밖(단일 문서 모드 전체 화면 알림 등)에 놓이면 패널 제목 줄이 없어 자체 머리줄을 보인다.
            _dockHosted = this.FindAncestorOfType<DockControl>() is not null;
            HostlessHeader.IsVisible = !_dockHosted;
#if PD_DIAGNOSTICS
            if (_dockHosted)
            {
                NotificationPanelDiag.ViewAttached(attached: true);
            }
#endif
        };
#if PD_DIAGNOSTICS
        DetachedFromVisualTree += (_, _) =>
        {
            if (_dockHosted)
            {
                NotificationPanelDiag.ViewAttached(attached: false);
            }
        };
#endif
    }

    /// <summary>Dock 레이아웃(알림 패널) 안에 놓인 화면인지. false 면 단일 문서 모드 등 다른 호스트다.</summary>
    public bool IsDockHosted => _dockHosted;

    /// <summary>NotificationPanelViewModel → 이 화면 DataTemplate 을 Application 에 한 번만 추가한다. 앱이 없으면(단위 시험) 건너뛴다.</summary>
    public static void EnsureTemplate()
    {
        if (s_registered || Application.Current is not { } app)
        {
            return;
        }

        var empty = false;
#if PD_DIAGNOSTICS
        empty = NotificationPanelDiag.EmptyContent; // 진단 빌드 전용: --notify-panel empty
#endif
        // 일치 조건을 '실제 NotificationPanelViewModel 객체'로 좁힌다. build 만 주는 생성자는 TypeUtilities.CanCast<T> 로 고르는데,
        // 참조 형식이면 null 에도 true 라서(Avalonia 12.1.4 소스) 내용이 null 인 모든 ContentControl·ComboBox 선택 상자에서
        // 이 화면이 만들어졌다(i1006 attach-chain: 문서 영역 래퍼 3개·ComboBox 1개, dc=null — D-11 빈칸·panel.bounds 오보고 원인 후보).
        app.DataTemplates.Add(new FuncDataTemplate<NotificationPanelViewModel>(
            vm => vm is not null,
            (vm, _) => empty ? new Border() : new NotificationPanelView { DataContext = vm }, supportsRecycling: true));
        s_registered = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        var t = _diag.LayoutBegin();
#endif
        var size = base.MeasureOverride(availableSize);
#if PD_DIAGNOSTICS
        _diag.LayoutEnd(this, "measure", t, availableSize);
#endif
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
#if PD_DIAGNOSTICS
        var t = _diag.LayoutBegin();
#endif
        var size = base.ArrangeOverride(finalSize);
#if PD_DIAGNOSTICS
        _diag.LayoutEnd(this, "arrange", t, finalSize);
#endif
        return size;
    }
}
