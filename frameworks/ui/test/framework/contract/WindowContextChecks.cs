// 창 문맥·강타입 뷰모델 계약 시험 C74~C81 — IUiWindowContext·UiWindowViewModel 표면, 시험대 이관 뒤 셸 기본 창 0개, 모듈 등록 해석, 진단 XKUI0509·0510(B10, red 먼저)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class WindowContextChecks
{
    /// <summary>시험 모듈 ID — 시험대 모듈(B10)이 데모 창을 같은 ID 로 등록하는 형태를 시험이 흉내 낸다.</summary>
    internal const string TestModuleId = "xkxk.test.contract";

    private const string Grid = "xkxk.demo.grid", Gantt = "xkxk.demo.gantt", Form = "xkxk.demo.form";

    private static readonly (string Name, string Id)[] Legacy = { ("Grid", Grid), ("gantt", Gantt), ("FORM", Form), ("0", Grid), ("1", Gantt), ("2", Form) };

    private static readonly (string Id, string TitleKey, string Icon, string? Help)[] Demo =
    {
        (Grid, "shell.kind.grid", MenuIconCatalog.GridIconKey, "chrome.help.grid"),
        (Gantt, "shell.kind.gantt", MenuIconCatalog.GanttIconKey, "chrome.help.gantt"),
        (Form, "shell.kind.form", MenuIconCatalog.FormIconKey, null),
    };

    private static readonly string[] Moved = { ShellCommandIds.DemoOpen, ShellCommandIds.DemoOpenSdi, ShellCommandIds.BenchOpen };

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C74", "창 문맥 표면: IUiWindowContext·UiWindowViewModel(<TArgs>) 선택 파라미터 0·public 생성자 0·protected 1·전용 공개 진입점 3개 EditorBrowsable Never", C74);
        yield return new ContractCase("C75", "UiWindowContentContext.Window(IUiWindowContext)·UiCommandContext.Windows(IUiWindowRegistry) 읽기 전용 속성", C75);
        yield return new ContractCase("C76", "셸만 있는 앱: 창 목록에 xkxk.demo.* 0개, 옛 이름·숫자 Open 은 false+XKUI0505(맨 앞 실행)", C76);
        yield return new ContractCase("C77", "xkxk. 모듈이 xkxk.demo.grid 등을 별칭 없이 등록 → 옛 이름·숫자가 그 정의로 풀림·Owner=모듈", C77);
        yield return new ContractCase("C78", "ShellCommandIds.DemoOpen·DemoOpenSdi·BenchOpen 값 유지·셸 명령 레지스트리 미등록", C78);
        yield return new ContractCase("C79", "진단 ID XKUI0509 WindowArgumentMismatch·XKUI0510 WindowViewModelFailed 상수", C79);
        yield return new ContractCase("C80", "UiWindowViewModel<TArgs> AttachWindow 로 Args·Parameter 연결·AcceptParameter·2회 Attach 예외·Opened/Closed 각 1회", C80);
        yield return new ContractCase("C81", "형식 불일치 AttachWindow 는 ArgumentException·미연결(재연결 가능), 연결 전 NotifyOpenedAsync 는 InvalidOperationException·OnOpenedAsync 미호출", C81);
    }

    /// <summary>시험 모듈로 데모 창 3종을 등록한다(이미 있으면 그대로). 창 저장소가 프로세스 전역이라 한 번만 효과가 난다.</summary>
    internal static IUiWindowRegistry EnsureDemoWindows()
    {
        var reg = XkxkUi.Current.Windows;
        if (Demo.All(d => reg.Contains(d.Id)))
        {
            return reg;
        }

        XkxkUi.CreateBuilder().AddModule(new RecordingModule(TestModuleId, new List<string>(), c =>
        {
            foreach (var d in Demo)
            {
                c.Windows.Add(d.Id, o => (o.TitleKey, o.Icon, o.DescriptionKey) = (d.TitleKey, d.Icon, d.Help));
            }
        })).Build();
        return reg;
    }

    private static string? Resolve(IUiWindowRegistry reg, string idOrAlias) => reg.TryResolve(idOrAlias, out var w) ? w.Id : null;

    private sealed class ProbeViewModel : UiWindowViewModel
    {
    }

    private sealed class ProbeViewModel<TArgs> : UiWindowViewModel<TArgs>
    {
    }

    private static bool IsOptional(ParameterInfo p) => p.IsOptional || p.HasDefaultValue;

    private static void C74()
    {
        var types = new[] { typeof(IUiWindowContext), typeof(UiWindowViewModel), typeof(UiWindowViewModel<>) };
        var optional = new List<string>();
        foreach (var t in types)
        {
            Check.True(t.IsPublic && t.Namespace == typeof(XkxkUi).Namespace, $"{t.Name} 이 Framework 공개 형식이 아님");
            foreach (var m in t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
            {
                optional.AddRange(m.GetParameters().Where(IsOptional).Select(p => $"{t.Name}.{m.Name}({p.Name})"));
            }
        }

        Check.True(optional.Count == 0, $"선택 파라미터 {optional.Count}건: {string.Join("; ", optional)}");
        foreach (var t in types.Skip(1))
        {
            Check.True(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0, $"{t.Name} public 생성자가 있음");
        }

        // protected 매개변수 없는 생성자는 소비자와 같은 조건(공개 표면)으로 파생 형식 컴파일·인스턴스 생성으로 증명한다
        Check.True(new ProbeViewModel() is UiWindowViewModel && new ProbeViewModel<string>() is UiWindowViewModel<string>, "protected 매개변수 없는 생성자로 파생 인스턴스를 만들 수 없음");

        var browsable = typeof(UiWindowViewModel).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never).Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        Check.True(browsable.SequenceEqual(new[] { "AttachWindow", "NotifyClosed", "NotifyOpenedAsync" }), $"EditorBrowsable Never 메서드: {string.Join(",", browsable)}(AttachWindow·NotifyClosed·NotifyOpenedAsync 기대)");

        var ctx = typeof(IUiWindowContext);
        foreach (var (name, type) in new[] { ("WindowId", typeof(string)), ("DocumentId", typeof(string)), ("Title", typeof(string)), ("Parameter", typeof(object)), ("IsOpen", typeof(bool)) })
        {
            Check.True(ctx.GetProperty(name)?.PropertyType == type && ctx.GetProperty(name)!.SetMethod is null, $"IUiWindowContext.{name} 읽기 전용 {type.Name} 아님");
        }

        Check.True(ctx.GetMethod("SetTitle", new[] { typeof(string) })?.ReturnType == typeof(bool) && ctx.GetMethod("Close", Type.EmptyTypes)?.ReturnType == typeof(bool), "SetTitle·Close 가 bool 이 아님");
        Check.True(ctx.GetMethod("BeginBusy", Type.EmptyTypes)?.ReturnType == typeof(IDisposable) && ctx.GetMethod("BeginBusy", new[] { typeof(string) })?.ReturnType == typeof(IDisposable), "BeginBusy 2 오버로드가 IDisposable 이 아님");
        Check.True(ctx.GetEvent("Closed")?.EventHandlerType == typeof(EventHandler), "Closed 이벤트가 EventHandler 가 아님");
        var vm = typeof(UiWindowViewModel);
        Check.True(typeof(INotifyPropertyChanged).IsAssignableFrom(vm) && vm.IsAbstract && vm.GetProperty("Window")?.PropertyType == ctx && vm.GetProperty("Parameter")?.PropertyType == typeof(object), "UiWindowViewModel 속성·INotifyPropertyChanged 불일치");
        Check.True(typeof(UiWindowViewModel<>).BaseType == vm, "UiWindowViewModel<TArgs> 가 UiWindowViewModel 을 상속하지 않음");
    }

    private static void C75()
    {
        var window = typeof(UiWindowContentContext).GetProperty("Window");
        Check.True(window?.PropertyType == typeof(IUiWindowContext) && window.SetMethod is not { IsPublic: true }, "UiWindowContentContext.Window 가 읽기 전용 IUiWindowContext 가 아님");
        var windows = typeof(UiCommandContext).GetProperty("Windows");
        Check.True(windows?.PropertyType == typeof(IUiWindowRegistry) && windows.SetMethod is not { IsPublic: true }, "UiCommandContext.Windows 가 읽기 전용 IUiWindowRegistry 가 아님");
    }

    private static void C76()
    {
        var reg = XkxkUi.Current.Windows;
        var found = reg.Items.Where(w => w.Id.StartsWith("xkxk.demo.", StringComparison.Ordinal)).Select(w => w.Id).ToList();
        Check.True(found.Count == 0, $"셸만 있는 앱의 창 목록에 xkxk.demo.* {found.Count}개: {string.Join(", ", found)}");
        foreach (var (name, _) in Legacy)
        {
            var ok = true;
            var missing = RegistryProbe.Delta(UiDiagnosticIds.WindowNotRegistered, () => ok = reg.Open(name));
            Check.True(!ok && missing == 1, $"옛 이름 Open({name}): false·XKUI0505 1건 기대(결과 {ok}·{missing}건)");
        }
    }

    private static void C77()
    {
        EnsureDemoWindows();
        var reg = XkxkUi.Current.Windows;
        foreach (var d in Demo)
        {
            Check.True(reg.TryGet(d.Id, out var w) && w is not null && w.Owner == TestModuleId && w.Aliases.Count == 0 && w.TitleKey == d.TitleKey, $"{d.Id}: 모듈 소유·별칭 없음 등록이 아님");
        }

        var wrong = Legacy.Where(l => Resolve(reg, l.Name) != l.Id).Select(l => $"{l.Name}→{Resolve(reg, l.Name) ?? "(없음)"}").ToList();
        Check.True(wrong.Count == 0, $"옛 이름·숫자 해석 불일치: {string.Join(", ", wrong)}");
        var ok = true;
        var shellMissing = RegistryProbe.Delta(UiDiagnosticIds.WindowShellUnavailable, () => ok = reg.Open("Grid"));
        Check.True(!ok && shellMissing == 1, "등록된 Grid 를 셸 없이 Open 하면 false·XKUI0506 1건이어야 함");
        // 순서 의존: C76→C77 (창 저장소는 프로세스 전역이고 Remove 가 모듈 소유라 원복 불가 — 앞 시험이 빈 상태를 보도록 Program.cs 가 C76 을 맨 앞에 돌린다)
    }

    private static void C78()
    {
        Check.True(ShellCommandIds.DemoOpen == "xkxk.demo.open" && ShellCommandIds.DemoOpenSdi == "xkxk.demo.openSdi" && ShellCommandIds.BenchOpen == "xkxk.bench.open", "이관 명령 ID 상수 값이 바뀜");
        foreach (var id in Moved)
        {
            Check.True(!XkxkUi.Current.Commands.TryGet(id, out _) && !XkxkUi.Current.Commands.Contains(id), $"셸만 있는 앱에 {id} 가 등록됨");
        }
    }

    private static void C79()
    {
        Check.True(UiDiagnosticIds.WindowArgumentMismatch == "XKUI0509" && UiDiagnosticIds.WindowViewModelFailed == "XKUI0510", "XKUI0509·0510 상수(WindowArgumentMismatch·WindowViewModelFailed) 불일치");
        Check.True(typeof(UiDiagnosticIds).GetField("WindowArgumentMismatch")?.IsLiteral == true && typeof(UiDiagnosticIds).GetField("WindowViewModelFailed")?.IsLiteral == true, "진단 상수가 const 가 아님");
    }

    private sealed record ProjectArgs(int Id);

    private sealed class FakeWindow : IUiWindowContext
    {
        public string WindowId => "c80.win";

        public string DocumentId => "c80.doc";

        public string Title { get; private set; } = "";

        public object? Parameter => null;

        public bool IsOpen => true;

        public event EventHandler? Closed { add { } remove { } }

        public bool SetTitle(string title)
        {
            Title = title;
            return true;
        }

        public bool Close() => true;

        public IDisposable BeginBusy() => new Nop();

        public IDisposable BeginBusy(string reason) => new Nop();

        private sealed class Nop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class ArgsVm : UiWindowViewModel<ProjectArgs>
    {
        public int Opened { get; private set; }

        public int Closed { get; private set; }

        public bool Accepts(object? parameter) => AcceptParameter(parameter);

        protected override Task OnOpenedAsync(CancellationToken cancellationToken)
        {
            Opened++;
            return Task.CompletedTask;
        }

        protected override void OnClosed() => Closed++;
    }

    private static void C80()
    {
        var window = new FakeWindow();
        var vm = new ArgsVm();
        Check.Throws<InvalidOperationException>(() => _ = vm.Window, "연결 전 Window 읽기");
        Check.True(vm.Accepts(null) && vm.Accepts(new ProjectArgs(1)) && !vm.Accepts("x") && !vm.Accepts(5), "AcceptParameter: null·TArgs 는 true, 다른 형식은 false 여야 함");
        vm.AttachWindow(window, null);
        Check.True(ReferenceEquals(vm.Window, window) && vm.Parameter is null && vm.Args is null, "null 파라미터: Window 연결·Parameter null·Args default 가 아님");
        Check.Throws<InvalidOperationException>(() => vm.AttachWindow(window, null), "AttachWindow 2회");
        var given = new ProjectArgs(7);
        var typed = new ArgsVm();
        typed.AttachWindow(window, given);
        Check.True(ReferenceEquals(typed.Parameter, given) && ReferenceEquals(typed.Args, given), "TArgs 값: Parameter·Args 가 같은 인스턴스가 아님");
        typed.NotifyOpenedAsync(CancellationToken.None).GetAwaiter().GetResult();
        typed.NotifyOpenedAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check.True(typed.Opened == 1, $"NotifyOpenedAsync 효과 {typed.Opened}회(1 기대)");
        typed.NotifyClosed();
        typed.NotifyClosed();
        Check.True(typed.Closed == 1, $"NotifyClosed 효과 {typed.Closed}회(1 기대)");
    }

    private static void C81()
    {
        var window = new FakeWindow();
        var vm = new ArgsVm();
        Check.Throws<ArgumentException>(() => vm.AttachWindow(window, "형식 불일치"), "TArgs 가 아닌 값 AttachWindow");
        Check.Throws<InvalidOperationException>(() => _ = vm.Window, "거부된 AttachWindow 뒤 Window 읽기(미연결이어야 함)");
        Check.True(vm.Parameter is null, "거부된 AttachWindow 뒤 Parameter 가 남음");
        Check.Throws<InvalidOperationException>(() => vm.NotifyOpenedAsync(CancellationToken.None), "연결 전 NotifyOpenedAsync");
        Check.True(vm.Opened == 0, $"연결 전 NotifyOpenedAsync 가 OnOpenedAsync 를 불렀다({vm.Opened}회)");
        var given = new ProjectArgs(3);
        vm.AttachWindow(window, given); // 거부는 연결 상태를 소모하지 않아 올바른 값으로 다시 연결된다
        Check.True(ReferenceEquals(vm.Args, given) && ReferenceEquals(vm.Window, window), "거부 뒤 올바른 값 재연결이 되지 않음");
        vm.NotifyOpenedAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check.True(vm.Opened == 1, $"재연결 뒤 NotifyOpenedAsync 효과 {vm.Opened}회(1 기대)");
    }
}
