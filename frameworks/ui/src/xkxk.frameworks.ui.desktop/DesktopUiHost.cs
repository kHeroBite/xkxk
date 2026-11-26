// 데스크톱 호스트의 별별UI 빌더 어댑터(내부) — 공개 계약만 써서 빌더를 구성하고 결과 1줄을 호스트 로그에 남긴다
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 데스크톱 진입부가 <see cref="XkxkUi.CreateBuilder(XkxkUiBuilderOptions)"/> 를 거치게 하는 내부 어댑터다.
/// 모듈은 등록하지 않는다(B02 동작 불변). 공개 호스트 확장(UseDesktopHost 등)은 B11 범위다.
/// </summary>
internal static class DesktopUiHost
{
    /// <summary>호스트 인자로 빌더를 만들고 구성 결과를 로그에 남긴다.</summary>
    /// <param name="args">셸에 넘기는 앱 인자(호스트 전용 인자를 걷어낸 뒤 값).</param>
    /// <param name="log">호스트 로그 출력.</param>
    internal static XkxkUiApplication Build(IReadOnlyList<string> args, Action<string> log)
    {
        var app = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions
        {
            Args = [.. args],
            HostKind = UiHostKinds.Desktop,
        }).Build();
        log($"ui builder host={app.Options.HostKind} modules={app.ModuleIds.Count} diagnostics={app.Diagnostics.Entries.Count}");
        return app;
    }
}
