// 호스트 옵션 계약 시험 C19~C23 — Host 묶음 기본값·Build 보존·대입 불가·ApplicationId 보존과 UseXkxkUi 의 App 연결 규칙을 본다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>App 공개 정적 호스트 값 8개의 사본 — C22·C23 이 바꾼 값을 시험 끝에 되돌린다(시험 간 격리).</summary>
internal sealed class AppStaticSnapshot
{
    private readonly IReadOnlyList<string> _startupArgs = App.StartupArgs;
    private readonly ILayoutStore _layoutStore = App.LayoutStore;
    private readonly ILayoutStore? _settingsStore = App.SettingsStore;
    private readonly IKeyValueStore? _layoutLibraryStore = App.LayoutLibraryStore;
    private readonly Action<Window>? _configureMainWindow = App.ConfigureMainWindow;
    private readonly Func<Window, PixelRect?>? _visibleFrameOf = App.VisibleFrameOf;
    private readonly Func<IHostWindow>? _createHostWindow = App.CreateHostWindow;
    private readonly IWindowHostCommands? _windowHost = App.WindowHost;

    /// <summary>찍어 둔 값을 공개 setter 로 되돌린다.</summary>
    public void Restore()
    {
        App.StartupArgs = _startupArgs;
        App.LayoutStore = _layoutStore;
        App.SettingsStore = _settingsStore;
        App.LayoutLibraryStore = _layoutLibraryStore;
        App.ConfigureMainWindow = _configureMainWindow;
        App.VisibleFrameOf = _visibleFrameOf;
        App.CreateHostWindow = _createHostWindow;
        App.WindowHost = _windowHost;
    }
}

internal static class HostOptionsChecks
{
    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C19", "Host 기본값(비 null·같은 인스턴스·7 속성 null), UiHostOptions sealed·공개 기본 생성자", C19);
        yield return new ContractCase("C20", "Host 값이 Build 뒤 app.Options.Host·모듈 컨텍스트에 같은 참조로 보존", C20);
        yield return new ContractCase("C21", "ApplicationId Build 뒤 보존, Host 공개 setter 없음", C21);
        yield return new ContractCase("C22", "UseXkxkUi 뒤 App 공개 getter 8개가 옵션 값과 같다(Args 순서·나머지 참조 동일)", C22);
        yield return new ContractCase("C23", "UseXkxkUi 는 비 null 옵션만 연결 — null 은 기존 정적 값 유지, 비 null 은 옵션 우선", C23);
    }

    private static void C19()
    {
        var o = new XkxkUiBuilderOptions();
        var h = o.Host;
        Check.True(h is not null, "Host 기본값이 null");
        Check.True(ReferenceEquals(h, o.Host), "Host 를 두 번 읽으면 다른 인스턴스");
        Check.True(h!.LayoutStore is null && h.SettingsStore is null && h.LayoutLibraryStore is null, "저장소 3개 중 null 이 아닌 것이 있음");
        Check.True(h.ConfigureMainWindow is null && h.VisibleFrameOf is null && h.CreateHostWindow is null && h.WindowHost is null,
            "창 관련 4개 중 null 이 아닌 것이 있음");
        Check.True(typeof(UiHostOptions).IsSealed, "UiHostOptions 가 sealed 가 아님");
        Check.True(typeof(UiHostOptions).GetConstructor(Type.EmptyTypes) is { IsPublic: true }, "UiHostOptions 공개 기본 생성자 없음");
    }

    private static void C20()
    {
        var layout = new MemoryLayoutStore();
        var library = new MemoryKeyValueStore();
        Action<Window> configure = _ => { };
        UiHostOptions? seen = null;
        var b = XkxkUi.CreateBuilder();
        var host = b.Options.Host;
        host.LayoutStore = layout;
        host.LayoutLibraryStore = library;
        host.ConfigureMainWindow = configure;
        b.AddModule(new RecordingModule("host.module", new List<string>(), c => seen = c.Options.Host));
        var app = b.Build();
        Check.True(ReferenceEquals(app.Options.Host, host), "app.Options.Host 가 빌더 옵션의 Host 와 다름");
        Check.True(ReferenceEquals(app.Options.Host.LayoutStore, layout), "LayoutStore 참조가 바뀜");
        Check.True(ReferenceEquals(app.Options.Host.LayoutLibraryStore, library), "LayoutLibraryStore 참조가 바뀜");
        Check.True(ReferenceEquals(app.Options.Host.ConfigureMainWindow, configure), "ConfigureMainWindow 참조가 바뀜");
        Check.True(app.Options.Host.SettingsStore is null && app.Options.Host.WindowHost is null, "넣지 않은 값이 채워짐");
        Check.True(ReferenceEquals(seen, host), "모듈 Configure 에서 본 Options.Host 가 다른 인스턴스");
    }

    private static void C21()
    {
        var app = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ApplicationId = "xkxk.frameworks.ui" }).Build();
        Check.True(app.Options.ApplicationId == "xkxk.frameworks.ui", $"ApplicationId={app.Options.ApplicationId}");
        var prop = typeof(XkxkUiBuilderOptions).GetProperty("Host");
        Check.True(prop is not null && prop.PropertyType == typeof(UiHostOptions), "공개 속성 Host(UiHostOptions) 없음");
        Check.True(prop!.SetMethod is null, "Host 에 공개 setter 가 있음");
    }

    private static void C22()
    {
        var snapshot = new AppStaticSnapshot();
        try
        {
            var layout = new MemoryLayoutStore();
            var settings = new MemoryLayoutStore();
            var library = new MemoryKeyValueStore();
            Action<Window> configure = _ => { };
            Func<Window, PixelRect?> frame = _ => null;
            Func<IHostWindow> create = () => null!;
            var windowHost = new ManagedWindowHostCommands();
            var o = new XkxkUiBuilderOptions { Args = new[] { "--b03-probe", "--b03-second" } };
            o.Host.LayoutStore = layout;
            o.Host.SettingsStore = settings;
            o.Host.LayoutLibraryStore = library;
            o.Host.ConfigureMainWindow = configure;
            o.Host.VisibleFrameOf = frame;
            o.Host.CreateHostWindow = create;
            o.Host.WindowHost = windowHost;
            var app = XkxkUi.CreateBuilder(o).Build();
            AppBuilder.Configure<Application>().UseXkxkUi(app); // Setup·Start 는 부르지 않는다(플랫폼 없음)
            Check.True(App.StartupArgs.SequenceEqual(new[] { "--b03-probe", "--b03-second" }),
                $"StartupArgs={string.Join(",", App.StartupArgs)}");
            Check.True(ReferenceEquals(App.LayoutStore, layout), "App.LayoutStore 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.SettingsStore, settings), "App.SettingsStore 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.LayoutLibraryStore, library), "App.LayoutLibraryStore 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.ConfigureMainWindow, configure), "App.ConfigureMainWindow 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.VisibleFrameOf, frame), "App.VisibleFrameOf 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.CreateHostWindow, create), "App.CreateHostWindow 가 옵션 값과 다름");
            Check.True(ReferenceEquals(App.WindowHost, windowHost), "App.WindowHost 가 옵션 값과 다름");
        }
        finally
        {
            snapshot.Restore();
        }
    }

    private static void C23()
    {
        var snapshot = new AppStaticSnapshot();
        try
        {
            var s1 = new MemoryLayoutStore();
            var s2 = new MemoryLayoutStore();
            Action<Window> a1 = _ => { };
            var w1 = new ManagedWindowHostCommands();
            App.LayoutStore = s1;
            App.ConfigureMainWindow = a1;
            App.WindowHost = w1;
            App.StartupArgs = new[] { "--keep" };

            var empty = XkxkUi.CreateBuilder().Build(); // Args 0개·Host 전부 null
            AppBuilder.Configure<Application>().UseXkxkUi(empty);
            Check.True(ReferenceEquals(App.LayoutStore, s1), "null 옵션이 정적 LayoutStore 를 바꿈");
            Check.True(ReferenceEquals(App.ConfigureMainWindow, a1), "null 옵션이 정적 ConfigureMainWindow 를 바꿈");
            Check.True(ReferenceEquals(App.WindowHost, w1), "null 옵션이 정적 WindowHost 를 바꿈");
            Check.True(App.StartupArgs.SequenceEqual(new[] { "--keep" }), $"빈 Args 가 정적 StartupArgs 를 바꿈: {string.Join(",", App.StartupArgs)}");

            var b = XkxkUi.CreateBuilder();
            b.Options.Host.LayoutStore = s2;
            AppBuilder.Configure<Application>().UseXkxkUi(b.Build());
            Check.True(ReferenceEquals(App.LayoutStore, s2), "비 null 옵션 LayoutStore 가 우선하지 않음");
            Check.True(ReferenceEquals(App.ConfigureMainWindow, a1), "두 번째 연결이 ConfigureMainWindow 를 바꿈");
            Check.True(ReferenceEquals(App.WindowHost, w1), "두 번째 연결이 WindowHost 를 바꿈");
            Check.True(App.StartupArgs.SequenceEqual(new[] { "--keep" }), "두 번째 연결이 StartupArgs 를 바꿈");
        }
        finally
        {
            snapshot.Restore();
        }
    }
}
