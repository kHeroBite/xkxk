// 데스크톱 원격 판정 훅(캡처 방지 실제 값·SDI OS Z 순서) 연결 — 진단 빌드 전용, Program.ConfigureApp 에서 분리
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 훅을 읽는 원격 명령도 진단 빌드에만 있다
using Pd.Poc.Shell.Diagnostics.Remote;

namespace Pd.Poc.Desktop;

/// <summary>원격 명령(window.captureProtect.get·docs.list)이 읽는 플랫폼 훅을 데스크톱 구현으로 채운다. 창 목록은 DesktopWindowHost 에서 받는다.</summary>
internal static class DesktopDiagHooks
{
    /// <summary>App 주입점을 채운 뒤 한 번 부른다.</summary>
    public static void Install()
    {
        // ⑬ 원격 판정 훅: 메인+SDI 전체 affinity
        RemoteCommands.CaptureProtectProbe = () => CaptureProtection.AllProtected(DesktopWindowHost.MainWindow, DesktopWindowHost.SdiWindows);
        RemoteCommands.CaptureAffinityProbe = () => CaptureProtection.Affinities(DesktopWindowHost.MainWindow, DesktopWindowHost.SdiWindows); // 창별 affinity(perWindow)
        RemoteCommands.SdiZOrderProbe = () => SdiZOrderProbe.Read(DesktopWindowHost.SdiWindows); // Q12 docs.list SDI zIndex(실제 OS Z 순서, 아래→위)
    }
}
#endif
