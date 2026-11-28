// 데스크톱 호스트의 별별UI 빌더 어댑터(내부) — 공개 계약만 써서 빌더를 구성하고 결과 1줄을 호스트 로그에 남긴다
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 데스크톱 진입부가 <see cref="XkxkUi.CreateBuilder(XkxkUiBuilderOptions)"/> 를 거치게 하는 내부 어댑터다.
/// 모듈 등록은 소비자가 <see cref="DesktopHostOptions.ConfigureUi"/> 로 넘긴다(null 이면 모듈 0, B11 UADR-0018). 호스트 값은 App 정적 대입 대신 <see cref="XkxkUiBuilderOptions.Host"/> 로 넘기고 UseXkxkUi 가 App 에 연결한다(B03, UADR-0010).
/// </summary>
internal static class DesktopUiHost
{
    /// <summary>호스트 인자와 호스트 값으로 빌더를 만들고 구성 결과를 로그에 남긴다.</summary>
    /// <param name="args">셸에 넘기는 앱 인자(호스트 전용 인자를 걷어낸 뒤 값).</param>
    /// <param name="configureHost">호스트 값(<see cref="UiHostOptions"/>)을 채우는 대리자.</param>
    /// <param name="configureUi">소비자 빌더 구성(모듈 등록 등). null 이면 아무것도 등록하지 않는다.</param>
    /// <param name="log">호스트 로그 출력.</param>
    internal static XkxkUiApplication Build(IReadOnlyList<string> args, Action<UiHostOptions> configureHost, Action<XkxkUiBuilder>? configureUi, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(configureHost);
        var options = new XkxkUiBuilderOptions
        {
            Args = [.. args],
            ApplicationId = "xkxk.frameworks.ui",
            HostKind = UiHostKinds.Desktop,
        };
        configureHost(options.Host);
        var builder = XkxkUi.CreateBuilder(options);
        configureUi?.Invoke(builder);
        var app = builder.Build();
        log($"ui builder host={app.Options.HostKind} modules={app.ModuleIds.Count} diagnostics={app.Diagnostics.Entries.Count} app={app.Options.ApplicationId} host-options={HostOptionKeys(app.Options.Host)}");
        return app;
    }

    /// <summary>값이 있는(null 이 아닌) 호스트 값 이름을 고정 순서로 쉼표 연결한다.</summary>
    private static string HostOptionKeys(UiHostOptions h)
    {
        (string Key, object? Value)[] pairs =
        [
            ("layout", h.LayoutStore),
            ("settings", h.SettingsStore),
            ("library", h.LayoutLibraryStore),
            ("mainwindow", h.ConfigureMainWindow),
            ("visibleframe", h.VisibleFrameOf),
            ("sdihost", h.CreateHostWindow),
            ("windowhost", h.WindowHost),
        ];
        return string.Join(',', pairs.Where(p => p.Value is not null).Select(p => p.Key));
    }
}
