// 데모 열기 명령(xkxk.demo.open·openSdi)과 벤치 열기 명령(xkxk.bench.open)을 공개 경로로 등록한다(B10 M6)
using System;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.testbed;

/// <summary>
/// 셸에서 옮겨 온 데모·벤치 명령 3개를 같은 ID 로 등록한다. 처리기는 창 레지스트리 공개 경로만 쓴다.
/// </summary>
/// <remarks>
/// <para>파라미터는 창 ID·별칭·옛 이름(Grid·Gantt·Form)·숫자 문자열이다. 옛 이름은 레지스트리 해석의 마지막 단계가 데모 ID 로 푼다.</para>
/// <para>벤치 열기는 진단+벤치 빌드에서만 등록한다. 처리기는 <see cref="TestbedBench"/> 를 직접 부른다
/// (보기 레지스트리의 벤치 열기는 이 명령 실행에 위임하므로 그쪽을 부르면 무한 재귀가 된다).</para>
/// </remarks>
public static class TestbedCommands
{
    private const string FileCategory = "file";

    /// <summary>명령을 등록한다.</summary>
    /// <param name="context">모듈 구성 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static void Register(IUiModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var r = context.Commands;
        Add(r, ShellCommandIds.DemoOpen, "menu.file.openGrid", MenuIconCatalog.GridIconKey,
            c => { if (c.Parameter is string s) { c.Windows.Open(s); } }, CanOpen);
        Add(r, ShellCommandIds.DemoOpenSdi, "menu.file.openGridSdi", CmdIconKeys.OpenSdi,
            c => { if (c.Parameter is string s) { c.Windows.Open(s, o => o.Placement = UiWindowPlacement.Sdi); } }, CanOpen);
#if PD_DIAGNOSTICS && PD_BENCH
        Add(r, ShellCommandIds.BenchOpen, "menu.file.openBench", CmdIconKeys.OpenBench,
            c => TestbedBench.Open(c.Parameter as string ?? "grid"), c => c.App.Shell is not null);
#endif
    }

    /// <summary>셸이 있고 파라미터 문자열이 등록된 창으로 풀리면 열 수 있다.</summary>
    private static bool CanOpen(UiCommandContext c) =>
        c.App.Shell is not null && c.Parameter is string s && c.Windows.TryResolve(s, out _);

    private static void Add(IUiCommandRegistry r, string id, string titleKey, string icon,
        Action<UiCommandContext> execute, Func<UiCommandContext, bool> canExecute) =>
        r.Add(id, o =>
        {
            o.TitleKey = titleKey;
            o.Icon = icon;
            o.Category = FileCategory;
            o.Execute = execute;
            o.CanExecute = canExecute;
        });
}
