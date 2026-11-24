// 원격 명령(시스템 트레이·앱 아이콘 판정용) — window.icon 창별 OS 아이콘·내장 자원 조회, tray.state 선언·플랫폼 구현·표시·메뉴 항목 조회, tray.invoke 메뉴 항목 실행(실입력 없이 바인딩된 Command 직접 실행), tray.enable 이번 실행 한정 표시 켜기·끄기
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다(Q9)
using Pd.Poc.Shell.Platform;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Dock.Avalonia.Controls;
using Avalonia.Threading;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteTrayCommands
{
    /// <summary>App.axaml 트레이 메뉴의 구분선을 뺀 항목 순서. 메뉴 순서를 바꾸면 이 배열도 함께 바꾼다.</summary>
    private static readonly string[] TrayItemNames = { "show", "hide", "resetMain", "singleMonitor", "multiMonitor", "help", "about", "exit" };

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("window.icon", "앱 아이콘 상태(AC-026): 내장 자원(embedded)·창별 kind(main|sdi|other)·WM_GETICON(big·small·small2)·GCLP_HICON(classBig·classSmall)·작업 표시줄 스타일(showInTaskbar·appWindow·toolWindow). ok=메인·SDI 전부 big·small 있음", _ => IconState());
        r.Register("tray.state", "트레이 상태: 선언(declared)·플랫폼 구현(implPresent — 데스크톱 true, Android·웹 false 예상)·실제 표시(visible)·저장값(trayEnabled)·시험 억제(suppressSaved)·원격 강제(remoteOverride)·툴팁·아이콘·메뉴 항목(name·header·enabled·명령·canExecute)·메인 창(visible·state)·마지막 실행 항목·pid", _ => TrayState());
        r.Register("tray.invoke", "트레이 메뉴 항목 실행(실입력 없음 — App.axaml NativeMenuItem 에 바인딩된 Command 를 직접 실행) item=show|hide|resetMain|singleMonitor|multiMonitor|help|about|exit. exit 는 응답 송신 뒤 실행(종료 자동 저장 경로) → 실행 뒤 tray.state", a =>
        {
            var name = a.ReqStr("item");
            var item = TrayItem(name) ?? throw new RemoteCommandException($"트레이 항목 '{name}' 이 없습니다. 가능: {string.Join("|", TrayItemNames)}");
            var cmd = item.Command ?? throw new RemoteCommandException($"트레이 항목 '{name}' 에 Command 바인딩이 없습니다(Application.DataContext 연결 확인).");
            if (!cmd.CanExecute(item.CommandParameter))
            {
                throw new RemoteCommandException($"트레이 항목 '{name}' 은 지금 실행할 수 없습니다(CanExecute=false).");
            }

            ShellPlatform.Menu?.Mark(name);
            if (name == "exit")
            {
                Dispatcher.UIThread.Post(() => cmd.Execute(item.CommandParameter), DispatcherPriority.Background);
                return new JsonObject { ["invoked"] = name, ["deferred"] = true, ["pid"] = Environment.ProcessId };
            }

            cmd.Execute(item.CommandParameter);
            var state = TrayState();
            state["invoked"] = name;
            return state;
        });
        r.Register("tray.enable", "트레이 표시를 이번 실행만 강제(저장하지 않음 — settings.json 무변경) on=true|false|auto(auto=강제 해제, 저장값·시험 억제 규칙으로) → tray.state. ⚠ 켜면 알림 영역에 아이콘이 보인다", a =>
        {
            var menu = ShellPlatform.Menu ?? throw new RemoteCommandException("트레이가 연결되지 않았습니다(데스크톱 수명 아님).");
            var on = a.ReqStr("on");
            menu.RemoteOverride = on.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : a.Bool("on");
            return TrayState();
        });
        r.Register("tray.saved", "'트레이 상주' 저장값 변경(윈도우 메뉴 shell.mnuTray 체크와 같은 MainViewModel.Extras.TrayEnabled → settings.json 저장) on=true|false → tray.state. 팝업 없이 메뉴 체크 경로를 대신한다. ⚠ 격리(--fresh·--profile-dir) 없이 부르면 사용자 settings.json 이 바뀐다", a =>
        {
            RemoteShellAccess.Vm.Extras.TrayEnabled = a.Bool("on");
            return TrayState();
        });
        r.Register("tray.minimizeMain", "메인 창 최소화(제목줄 '최소화' OnMinimize 와 같은 WindowState=Minimized — 팝업·실입력 없음) → tray.state(트레이가 보이면 mainVisible=false 로 숨는다)", _ =>
        {
            var main = VisualTreeInspector.Main() as Window ?? throw new RemoteCommandException("메인 창이 Window 가 아닙니다(브라우저·모바일).");
            main.WindowState = WindowState.Minimized;
            return TrayState();
        });
    }

    private static JsonObject IconState()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime
            ?? throw new RemoteCommandException("데스크톱 수명이 아닙니다(브라우저·모바일).");
        var probe = ShellPlatform.WindowIconProbe;
        var list = new JsonArray();
        var judged = 0;
        var ok = true;
        foreach (var window in lifetime.Windows)
        {
            var state = probe?.Invoke(window);
            var kind = ReferenceEquals(window, lifetime.MainWindow) ? "main" : window is HostWindow ? "sdi" : "other";
            if (kind != "other")
            {
                judged++;
                ok &= state is { Big: true, Small: true };
            }

            list.Add(new JsonObject
            {
                ["kind"] = kind,
                ["title"] = window.Title,
                ["explicit"] = window.Icon is not null,
                ["showInTaskbar"] = window.ShowInTaskbar,
                ["big"] = state?.Big,
                ["small"] = state?.Small,
                ["small2"] = state?.Small2,
                ["classBig"] = state?.ClassBig,
                ["classSmall"] = state?.ClassSmall,
                ["appWindow"] = state?.AppWindow,
                ["toolWindow"] = state?.ToolWindow,
            });
        }

        return new JsonObject
        {
            ["ok"] = ok && judged > 0,
            ["embedded"] = ShellPlatform.HasEmbeddedIcon,
            ["probe"] = probe is not null,
            ["windows"] = list,
            ["pid"] = Environment.ProcessId,
        };
    }

    private static NativeMenuItem? TrayItem(string name)
    {
        var idx = Array.IndexOf(TrayItemNames, name);
        var items = ShellPlatform.Tray?.Menu?.Items.OfType<NativeMenuItem>().Where(m => m is not NativeMenuItemSeparator).ToList();
        return idx < 0 || items is null || idx >= items.Count ? null : items[idx];
    }

    private static JsonObject TrayState()
    {
        var tray = ShellPlatform.Tray;
        var menu = ShellPlatform.Menu;
        var items = new JsonArray();
        var menuItems = tray?.Menu?.Items.OfType<NativeMenuItem>().Where(m => m is not NativeMenuItemSeparator).ToList() ?? new();
        for (var i = 0; i < menuItems.Count; i++)
        {
            var m = menuItems[i];
            items.Add(new JsonObject
            {
                ["name"] = i < TrayItemNames.Length ? TrayItemNames[i] : null,
                ["header"] = m.Header,
                ["enabled"] = m.IsEnabled,
                ["hasCommand"] = m.Command is not null,
                ["canExecute"] = m.Command?.CanExecute(m.CommandParameter),
                ["param"] = m.CommandParameter?.ToString(),
            });
        }

        var main = VisualTreeInspector.Main() as Window;
        return new JsonObject
        {
            ["declared"] = tray is not null,
            ["implPresent"] = tray is null ? null : TrayImplPresent(tray),
            ["visible"] = tray?.IsVisible,
            ["modelVisible"] = menu?.Visible,
            ["trayEnabled"] = App.MainViewModel?.Extras.TrayEnabled,
            ["suppressSaved"] = ShellPlatform.SuppressSavedTray,
            ["remoteOverride"] = menu?.RemoteOverride,
            ["toolTip"] = tray?.ToolTipText,
            ["hasIcon"] = tray?.Icon is not null,
            ["clickCommand"] = tray?.Command is not null,
            ["separators"] = tray?.Menu?.Items.OfType<NativeMenuItemSeparator>().Count(),
            ["items"] = items,
            ["mainVisible"] = main?.IsVisible,
            ["mainState"] = main?.WindowState.ToString(),
            ["lastInvoked"] = menu?.LastInvoked,
            ["pid"] = Environment.ProcessId,
        };
    }

    /// <summary>플랫폼 트레이 구현(ITrayIconImpl) 생성 여부 — Avalonia 비공개 필드 _impl 을 진단에서만 읽는다(이름이 바뀌면 null=모름).</summary>
    private static bool? TrayImplPresent(TrayIcon tray) =>
        typeof(TrayIcon).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic) is { } f ? f.GetValue(tray) is not null : null;
}
#endif
