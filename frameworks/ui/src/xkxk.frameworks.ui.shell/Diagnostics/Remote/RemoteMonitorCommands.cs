// 원격 명령(U20 모니터 선택 — 진단 빌드 전용) — 팝업을 열지 않고 '창 ▸ 멀티 모니터' 목록 조회·라디오 선택(같은 VM 명령)·최대화(제목줄 버튼과 같은 상태 변경 또는 메뉴 명령)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>모니터 메뉴 원격 명령 군. RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteMonitorCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("monitor.list", "U20 '창 ▸ 멀티 모니터' 항목(메뉴 VM 그대로 — id·머리글·선택·AutomationId)·저장값(SelectedMonitorId)·메인 창 상태 → 판정용", _ => ListJson());
        r.Register("monitor.select", "U20 모니터 선택 id=<monitor.list 의 id> — 메뉴 라디오와 같은 SelectCommand 실행(저장·최대화 중이면 즉시 이동) → monitor.list", a =>
        {
            var id = a.Str("id") ?? throw new RemoteCommandException("인자 'id'가 필요합니다.");
            var item = MonitorMenuViewModel.Instance.Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new RemoteCommandException($"메뉴에 없는 모니터 id: {id}");
            item.SelectCommand.Execute(null);
            return ListJson();
        });
        r.Register("monitor.pick", "소속 모니터 판정(순수 함수, 창을 움직이지 않음) monitors=id:x,y,w,h[;...] rect=x,y,w,h → owner·work(작업 영역은 Bounds 와 같게 둔다)", a =>
        {
            var list = a.ReqStr("monitors").Split(';').Select((s, i) =>
            {
                var p = s.Split(':');
                var b = ParseRect(p[1]);
                return new MonitorInfo(p[0], p[0], b, b, i == 0);
            }).ToArray();
            var owner = MonitorSelection.LargestOverlap(list, ParseRect(a.ReqStr("rect")));
            return new JsonObject { ["owner"] = owner?.Id, ["workX"] = owner?.WorkingArea.X, ["workY"] = owner?.WorkingArea.Y, ["workW"] = owner?.WorkingArea.Width, ["workH"] = owner?.WorkingArea.Height };
        });
        r.Register("monitor.recoveryPick", "SDI 복구 판정(순수 함수, 창을 움직이지 않음) work=x,y,w,h rect=x,y,w,h [maximized=true] → moved", a =>
        {
            var w = ParseRect(a.ReqStr("work"));
            var b = ParseRect(a.ReqStr("rect"));
            var input = new RecoveryWindow(new Avalonia.Rect(b.X, b.Y, b.Width, b.Height), false, false, a.Bool("maximized"));
            var plan = PlacementRecovery.Evaluate([new Avalonia.Rect(w.X, w.Y, w.Width, w.Height)], null, [input], new Avalonia.Point(w.X + 40, w.Y + 40));
            return new JsonObject { ["moved"] = plan.Sdi[0] is not null };
        });
        r.Register("monitor.maximize", "U20 메인 창 최대화 via=button(기본 — 제목줄 최대화 버튼과 같은 WindowState=Maximized)|command(메뉴 '선택한 모니터로 최대화' = 호스트 명령)|restore(이전 크기) → monitor.list", a =>
        {
            var main = MainWindow() ?? throw new RemoteCommandException("메인 창이 없습니다(데스크톱 전용).");
            switch ((a.Str("via") ?? "button").ToLowerInvariant())
            {
                case "command":
                    var host = App.WindowHost;
                    var ok = host is not null && host.CanExecute(WindowHostCommand.MaximizeOnSelectedMonitor) && host.Execute(WindowHostCommand.MaximizeOnSelectedMonitor);
                    var res = ListJson();
                    res["commandOk"] = ok;
                    return res;
                case "restore":
                    main.WindowState = WindowState.Normal;
                    break;
                default:
                    main.WindowState = WindowState.Maximized;
                    break;
            }

            return ListJson();
        });
    }

    private static Avalonia.PixelRect ParseRect(string text)
    {
        var v = text.Split(',').Select(int.Parse).ToArray();
        return new Avalonia.PixelRect(v[0], v[1], v[2], v[3]);
    }

    private static Window? MainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static JsonObject ListJson()
    {
        var vm = MonitorMenuViewModel.Instance;
        var main = MainWindow();
        return new JsonObject
        {
            ["provider"] = MonitorMenuViewModel.Provider is not null,
            ["hasMonitors"] = vm.HasMonitors,
            ["count"] = vm.Items.Count,
            ["selectedSetting"] = UiSettings.Current.SelectedMonitorId,
            ["items"] = new JsonArray(vm.Items.Select(x => (JsonNode?)new JsonObject
            {
                ["id"] = x.Id,
                ["header"] = x.Header,
                ["selected"] = x.IsSelected,
                ["automationId"] = x.AutomationId,
            }).ToArray()),
            ["main"] = main is null ? null : new JsonObject
            {
                ["state"] = main.WindowState.ToString(),
                ["x"] = main.Position.X,
                ["y"] = main.Position.Y,
                ["clientW"] = main.ClientSize.Width,
                ["clientH"] = main.ClientSize.Height,
                ["scale"] = main.DesktopScaling,
            },
        };
    }
}
#endif
