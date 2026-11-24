// 모니터 구성·메인 창 크기 변경 감시(요건 5) — 400ms 디바운스 뒤 보이지 않는 메인·SDI 창을 기본 위치로 옮기는 데스크톱 복구 연결부
using xkxk.frameworks.ui.shell.Platform;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 이전 경로(WindowPlacement.EnsureVisible)는 창이 열릴 때만 돌고, 복구 위치도 기본 위치가 아니라 가까운 모니터 안이었다. 이 감시자가 실행 중 변경을 맡는다.
/// 판정은 PlacementRecovery.Evaluate 가 하고 여기서는 값을 모으고 옮기기만 한다. 메인은 ④ ResetMain, SDI 는 ⑦ 공식. 최대화 창은 옮기지 않는다.
/// 비전경 시험(--bg-pos)은 창을 일부러 화면 밖에 두므로 끈다. 모니터 목록이 비면(전환 중) 옮기지 않고 다음 이벤트를 기다린다.
/// </summary>
internal static class MonitorChangeWatcher
{
    private const int DebounceMs = 400; // 구성 변경은 연속으로 온다 — 마지막 이벤트 뒤 이만큼 조용하면 1회 평가

    private static Window? s_main;
    private static RecoveryDebounce? s_debounce;
    private static bool s_subscribed;
    private static bool s_monitorsChanged;

    /// <summary>DesktopWindowHost.AttachMain 에서 1회.</summary>
    public static void Attach(Window main)
    {
        s_main = main;
        s_debounce = new RecoveryDebounce(DebounceMs, new DispatcherBusyClock(), Evaluate);
        main.Opened += (_, _) => Subscribe(main); // 표시 전에는 Screens 가 비어 있을 수 있다. 트레이 복원으로 다시 와도 구독은 1번
        main.Resized += (_, _) => s_debounce.Trigger();
#if PD_DIAGNOSTICS
        PlacementDiagHooks.SetVirtualMonitors = DesktopMonitorProvider.Instance.SetVirtual;
        PlacementDiagHooks.MonitorsApplied = OnMonitorsChanged;
        PlacementDiagHooks.ResizeMain = ResizeMainPinned;
#endif
    }

#if PD_DIAGNOSTICS
    // 크기 변경(SetWindowPlacement)이 화면 밖 창을 화면 안으로 보정하지 않게 ④ 와 같이 위치를 고정한다(--primary-override 기동 필요).
    private static bool ResizeMainPinned(double width, double height)
    {
        if (s_main is not { WindowState: WindowState.Normal } main)
        {
            return false;
        }

        OffscreenGuardHook.PinBegin(main.Position);
        main.Width = width;
        main.Height = height;
        main.UpdateLayout();
        OffscreenGuardHook.PinEnd();
        return true;
    }
#endif

    private static void Subscribe(Window main)
    {
        if (!s_subscribed && main.Screens is not null)
        {
            s_subscribed = true;
            main.Screens.Changed += (_, _) => OnMonitorsChanged();
        }
    }

    private static void OnMonitorsChanged()
    {
        s_monitorsChanged = true;
        s_debounce?.Trigger();
    }

    private static void Evaluate()
    {
        var monitorsChanged = s_monitorsChanged;
        s_monitorsChanged = false;
        if (s_main is not { } main)
        {
            return;
        }

        if (PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode))
        {
            Program.Log($"placement-recovery off (background mode) monitorsChanged={monitorsChanged}");
            return;
        }

        var areas = DesktopMonitorProvider.Instance.GetMonitors().Select(m => ToRect(m.WorkingArea)).ToArray();
        if (areas.Length == 0)
        {
            Program.Log("placement-recovery skip: no monitor info");
            return;
        }

        var sdi = DesktopWindowHost.OpenSdi.Where(w => w.IsVisible).ToArray();
        var mainInput = main.IsVisible ? Input(main, false) : (RecoveryWindow?)null;
        var plan = PlacementRecovery.Evaluate(areas, mainInput, sdi.Select(w => Input(w, SdiWindowHost.IsLocked(w))).ToArray(), WindowCommands.SdiDefaultPosition(areas[0]));
        var moved = plan.MainHidden && DesktopWindowHost.ResetMainForRecovery(main) ? 1 : 0; // 최대화 창은 대상이 아니다(OS 가 남은 모니터에 둔다)
        for (var i = 0; i < sdi.Length; i++)
        {
            if (plan.Sdi[i] is { } p && sdi[i] is SdiWindowHost host)
            {
                host.RecoverTo(new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)));
                moved++;
            }
        }

        Program.Log($"placement-recovery areas={areas.Length} sdi={sdi.Length} mainHidden={plan.MainHidden} moved={moved} monitorsChanged={monitorsChanged}");
        if (moved > 0)
        {
            App.NotifyLayoutChanged(); // 끌기 없는 위치 변경 — 자동 저장 대상
        }
    }

    // 창 좌상단은 Position 그대로 쓴다(MDI-12·⑦ 과 같은 값 — ⑦ 공식의 같은 자리 비교가 이 값을 기준으로 한다).
    private static RecoveryWindow Input(Window w, bool locked)
    {
        var scale = w.DesktopScaling;
        var size = w.FrameSize ?? w.ClientSize;
        return new RecoveryWindow(new Rect(w.Position.X, w.Position.Y, size.Width * scale, size.Height * scale), locked,
            w.WindowState == WindowState.Minimized, w.WindowState is WindowState.Maximized or WindowState.FullScreen);
    }

    private static Rect ToRect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
