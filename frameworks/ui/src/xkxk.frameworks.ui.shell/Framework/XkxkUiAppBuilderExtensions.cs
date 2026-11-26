// Avalonia AppBuilder 연결 확장 — Framework 안에서 유일하게 Avalonia 에 의존하는 파일
using System;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>Avalonia <see cref="AppBuilder"/> 에 별별UI 앱을 연결한다.</summary>
public static class XkxkUiAppBuilderExtensions
{
    /// <summary>
    /// 앱 설정이 끝난 뒤 <see cref="XkxkUiApplication.IsAttached"/> 를 true 로 표시한다. 그 밖의 기존 동작은 바꾸지 않는다.
    /// </summary>
    /// <remarks>
    /// AfterSetup 누적 확인(2026-11-26 odev-3 실측): Avalonia 12.1.4 net10.0 <c>Avalonia.Controls.dll</c> 을
    /// <c>dotnet fsi</c> 로 불러 <c>AppBuilder.Configure&lt;Application&gt;()</c> 에 AfterSetup 을 두 번 건 뒤
    /// <c>AfterSetupCallback.GetInvocationList().Length</c> = 3(기본 빈 대리자 + 2), 호출 시 두 콜백이 모두 실행됐다.
    /// 즉 AfterSetup 은 콜백을 누적(Delegate.Combine)하므로 호스트의 기존 AfterSetup 을 덮지 않는다.
    /// </remarks>
    /// <param name="builder">Avalonia 앱 빌더. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="application">연결할 별별UI 앱. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static AppBuilder UseXkxkUi(this AppBuilder builder, XkxkUiApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);
        return builder.AfterSetup(_ => application.IsAttached = true);
    }
}
