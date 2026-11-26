// Android 호스트의 별별UI 빌더 어댑터(내부) — 공개 계약만 써서 빌더를 구성하고 결과 1줄을 logcat 에 남긴다
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.android;

/// <summary>
/// Android 진입부(<c>AndroidApp.CustomizeAppBuilder</c>)가 <see cref="XkxkUi.CreateBuilder(XkxkUiBuilderOptions)"/> 를 거치게 하는 내부 어댑터다.
/// 모듈은 등록하지 않는다(B02 동작 불변). 공개 호스트 확장(UseAndroidHost 등)은 B11 범위다.
/// </summary>
internal static class AndroidUiHost
{
    /// <summary>호스트 인자로 빌더를 만들고 구성 결과를 로그에 남긴다.</summary>
    /// <param name="args">기동 시점의 앱 인자(App.StartupArgs).</param>
    /// <param name="log">호스트 로그 출력(logcat).</param>
    internal static XkxkUiApplication Build(IReadOnlyList<string> args, Action<string> log)
    {
        // Options.Args 는 기동 시점 사본이다(--gpu 제거 전, 인텐트 인자는 뒤에 MainActivity 가 넣음). B02 에는 소비자가 없고 정확한 값은 B03 에서 맞춘다.
        var app = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions
        {
            Args = [.. args],
            HostKind = UiHostKinds.Android,
        }).Build();
        log($"ui builder host={app.Options.HostKind} modules={app.ModuleIds.Count} diagnostics={app.Diagnostics.Entries.Count}");
        return app;
    }
}
