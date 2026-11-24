// 벤치 원격 명령(진단 빌드) — bench.env·bench.open·bench.grid·bench.chart·bench.result. otest 가 화면 클릭 없이 시나리오를 돌리고 JSON 을 받는다
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public static class BenchRemote
{
    public static void Register(RemoteCommandRouter r)
    {
        r.Register("bench.env", "벤치 환경: 플랫폼·문화권·한국어 collation·그리드 구현(Builtin|Pro)·포함 차트 라이브러리·한글 글꼴", _ => BenchProbe.Environment());
        r.Register("bench.open", "벤치 문서 열기 target=grid|chart|dashboard → {title, env}", async a =>
        {
            var target = GridBenchView.Str(a, "target", "grid");
            var doc = BenchEntry.Open(target) ?? throw new RemoteCommandException("셸이 준비되지 않았습니다.");
            if (target == "dashboard")
            {
                await Task.Delay(300); // 대시보드 문서는 탭이 없어 화면 생성까지만 기다린다
                return new JsonObject { ["title"] = doc.Title, ["env"] = BenchProbe.Environment() };
            }

            var view = await WaitViewAsync();
            view.Select(target);
            return new JsonObject { ["title"] = doc.Title, ["env"] = BenchProbe.Environment() };
        });
        r.Register("bench.grid",
            "그리드 벤치 scenario=load|scroll|realtime|sort|filter|xlsx|bands|frozen|pro_features|all [frozen: left=2 right=-1(Pro 전용), pro_features: mode=off|on(Pro 전용 — 조건부 서식·우측 고정 끄기/켜기) — rows=100000 seconds=5 step=3 rate=3000 focus=60 statusPct=0(realtime: 갱신 행 중 Status 변경 %, >0 이면 응답에 highlight 대조) property=Name culture=ko-KR|app|ordinal|(빈 값=현재 문화권) desc text=김 latin xlsxRows=10000] — sort 응답의 head_distinct 로 순서를 판정",
            async a =>
            {
                var view = await WaitViewAsync();
                view.Select("grid");
                await Task.Delay(50); // 탭 전환 뒤 화면 붙기
                return await view.Grid.RunAsync(GridBenchView.Str(a, "scenario", "load"), a);
            });
        r.Register("bench.chart",
            "차트 벤치 scenario=static|stream|all lib=scottplot|procharts|livecharts [points=100000 redrawSeconds=2 seconds=5 rate=1000 window=10000 animate maxPoints=2000 downsample=Lttb]",
            async a =>
            {
                var view = await WaitViewAsync();
                view.Select("chart");
                await Task.Delay(50);
                return await view.Chart.RunAsync(GridBenchView.Str(a, "scenario", "static"), a);
            });
        DashboardRemote.Register(r); // U4~U6 대시보드 벤치(bench.dashboard) — 새 파일
        r.Register("bench.result", "마지막 벤치 결과 target=grid|chart (대시보드는 bench.dashboard.result)", async a =>
        {
            var view = await WaitViewAsync();
            return (GridBenchView.Str(a, "target", "grid") == "chart" ? view.Chart.LastResult : view.Grid.LastResult)?.DeepClone();
        });
    }

    /// <summary>열린 벤치 문서 화면(최대 3초 대기 — bench.open 직후 화면 생성까지). 없으면 오류.</summary>
    private static async Task<BenchDocumentView> WaitViewAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            if (BenchDocumentView.Latest is { } v) { return v; }
            await Task.Delay(50);
        }

        throw new RemoteCommandException("열린 벤치 문서가 없습니다. 먼저 bench.open 을 실행하세요.");
    }
}
#endif
