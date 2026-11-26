// Avalonia AppBuilder 연결 확장 — 호스트 값(옵션 Args·Host)을 App 에 연결하고 연결 표시를 남긴다
using System;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>Avalonia <see cref="AppBuilder"/> 에 별별UI 앱을 연결한다.</summary>
public static class XkxkUiAppBuilderExtensions
{
    /// <summary>
    /// 호스트 값(<see cref="XkxkUiBuilderOptions.Args"/>·<see cref="XkxkUiBuilderOptions.Host"/>)을 App 정적 주입점에 즉시 연결하고,
    /// 앱 설정이 끝난 뒤 <see cref="XkxkUiApplication.IsAttached"/> 를 true 로 표시한다(B03, UADR-0010).
    /// </summary>
    /// <remarks>
    /// 연결은 이 호출 안에서 즉시(Avalonia Setup 전) 한다 — App.Initialize(XAML·TrExtension)가 StartupArgs 를 읽기 때문이다.
    /// 비 null 값(Args 는 1개 이상)만 연결하고 정적 setter 로 먼저 넣은 값보다 옵션이 우선한다. null 옵션은 기존 정적 값을 바꾸지 않는다.
    /// 두 번 호출하면 뒤 호출의 비 null 값이 이긴다. 연결 뒤 옵션 변경은 반영하지 않는다.
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
        App.BindHost(application); // Setup 전에 연결 — App.Initialize(XAML·TrExtension)가 StartupArgs 를 읽는다
        return builder.AfterSetup(_ => application.IsAttached = true);
    }
}
