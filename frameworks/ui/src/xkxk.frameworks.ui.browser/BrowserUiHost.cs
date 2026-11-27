// 웹(브라우저) 호스트의 별별UI 빌더 어댑터(내부) — 공개 계약만 써서 빌더를 구성하고 결과 1줄을 콘솔에 남긴다
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.testbed;

namespace xkxk.frameworks.ui.browser;

/// <summary>
/// 웹 진입부가 <see cref="XkxkUi.CreateBuilder(XkxkUiBuilderOptions)"/> 를 거치게 하는 내부 어댑터다.
/// 셸은 창·명령을 등록하지 않으며, 웹 호스트는 시험대 모듈(<see cref="TestbedModule"/>)만 추가한다(B10). 호스트 값(저장소·윈도우 메뉴 호스트)은 App 에 직접 대입하지 않고
/// <see cref="XkxkUiBuilderOptions.Host"/> 로 넘기며, UseXkxkUi 가 App 에 1회 연결한다(B03, UADR-0010).
/// 공개 호스트 확장(UseBrowserHost 등)은 B11 범위다.
/// </summary>
internal static class BrowserUiHost
{
    /// <summary>호스트 인자와 호스트 값으로 빌더를 만들고 구성 결과를 로그에 남긴다.</summary>
    /// <param name="args">셸에 넘기는 앱 인자(--gpu 를 걷어낸 뒤 값).</param>
    /// <param name="configureHost">호스트 값 묶음(<see cref="UiHostOptions"/>)을 채우는 대리자.</param>
    /// <param name="log">호스트 로그 출력(브라우저 console).</param>
    internal static XkxkUiApplication Build(IReadOnlyList<string> args, Action<UiHostOptions> configureHost, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(configureHost);
        var options = new XkxkUiBuilderOptions
        {
            Args = [.. args],
            ApplicationId = "xkxk.frameworks.ui",
            HostKind = UiHostKinds.Browser,
        };
        configureHost(options.Host);
        var app = XkxkUi.CreateBuilder(options).AddModule<TestbedModule>().Build();
        log($"ui builder host={app.Options.HostKind} modules={app.ModuleIds.Count} diagnostics={app.Diagnostics.Entries.Count} app={app.Options.ApplicationId} host-options={HostOptionKeys(app.Options.Host)}");
        return app;
    }

    // 데스크톱·Android 어댑터와 같은 규칙의 사본이다. B11 hosting 분리 때 한 곳으로 합친다.
    /// <summary>값이 있는 호스트 값의 키를 고정 순서로 쉼표 연결한다(없으면 빈 문자열).</summary>
    private static string HostOptionKeys(UiHostOptions h)
    {
        var keys = new List<string>(7);
        if (h.LayoutStore is not null) keys.Add("layout");
        if (h.SettingsStore is not null) keys.Add("settings");
        if (h.LayoutLibraryStore is not null) keys.Add("library");
        if (h.ConfigureMainWindow is not null) keys.Add("mainwindow");
        if (h.VisibleFrameOf is not null) keys.Add("visibleframe");
        if (h.CreateHostWindow is not null) keys.Add("sdihost");
        if (h.WindowHost is not null) keys.Add("windowhost");
        return string.Join(',', keys);
    }
}
