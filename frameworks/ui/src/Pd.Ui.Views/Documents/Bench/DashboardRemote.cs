// 대시보드 벤치 원격 명령 등록(진단+벤치 빌드 전용) — bench.dashboard 시나리오(load·drag·dragsdi·resize·relayout·clear)
#if PD_DIAGNOSTICS
using Pd.Poc.Shell;
using System.Threading.Tasks;
using Pd.Poc.Shell.Diagnostics.Remote;

namespace Pd.Ui.Views.Documents.Bench;

public static class DashboardRemote
{
    public static void Register(RemoteCommandRouter r)
    {
        r.Register("bench.dashboard",
            "대시보드 벤치(U4~U6) scenario=load|drag|dragsdi|resize|relayout|clear lib=scottplot|livecharts|theone [n=6|12|24 points=500 style=default|pd anim=true|false dragfreeze=true(L2)|dragsnap=true(L3, 데스크톱 전용) — livecharts 빌드에서 load 호출에 줘야 drag 때 적용, 기본 꺼짐 frames=60 dx=400 dy=250 seconds=20(relayout)] — 먼저 bench.open target=dashboard",
            async a =>
            {
                var view = DashboardBenchView.Latest ?? throw new RemoteCommandException("열린 대시보드 벤치 문서가 없습니다. 먼저 bench.open target=dashboard 를 실행하세요.");
                return await view.RunAsync(GridBenchView.Str(a, "scenario", "load"), a);
            });
        r.Register("bench.dashboard.result", "마지막 대시보드 벤치 결과", _ => Task.FromResult(DashboardBenchView.Latest?.LastResult?.DeepClone()));
    }
}
#endif
