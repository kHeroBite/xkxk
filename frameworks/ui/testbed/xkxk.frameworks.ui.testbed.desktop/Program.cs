// 시험대 데스크톱 실행 진입점 — 진단 스위치·프로세스 시작 시각을 먼저 잡고 공개 DesktopHost.Run 에 시험대 모듈을 넘긴다(B11, UADR-0018)
using System.Diagnostics;
using Avalonia;
using xkxk.frameworks.ui.hosting.desktop;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.testbed;

namespace xkxk.frameworks.ui.testbed.desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if PD_DIAGNOSTICS // --drag-probe on|full: 끌기 단계별 할당·Measure 수 진단. full 의 Avalonia 진단 스위치는 Avalonia 형식 초기화 전이어야 해서 첫 줄에 둔다
        MdiDragProbe.InstallFromArgs(args);
#endif
        PerfProbe.Current.MarkProcessStart(Process.GetCurrentProcess().StartTime.ToUniversalTime());
        return DesktopHost.Run(args, new DesktopHostOptions
        {
            ConfigureUi = b => b.AddModule<TestbedModule>(),
            DataFolderSegments = new[] { "xkxk", "frameworks", "ui", "testbed" },
            InstanceName = "xkxk.frameworks.ui.testbed",
        });
    }

    // Avalonia 디자이너도 이 메서드를 사용한다.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<xkxk.frameworks.ui.views.PdApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
