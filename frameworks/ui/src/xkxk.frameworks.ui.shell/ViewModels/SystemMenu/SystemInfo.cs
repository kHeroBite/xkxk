// 앱·실행 환경 정보 모음(UI-17 프로그램 정보·업데이트 확인·문의 첨부) — 버전·플랫폼·런타임·UI 라이브러리 버전을 한곳에서 읽는다
using xkxk.frameworks.ui.data;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

/// <summary>
/// 읽기 전용 정보. 값은 기동 뒤 바뀌지 않으므로 처음 읽을 때 한 번 계산한다.
/// 채널(dev·staging·qa·prod)과 실제 렌더 모드는 다른 소유자의 코드(AppChannel·렌더 정보)에서 받는다 — 연결 전에는 Channel·Renderer 공급 함수가 null 을 돌려준다.
/// </summary>
public static class SystemInfo
{
    private static readonly Lazy<string> VersionLazy = new(() => Informational(typeof(App).Assembly));

    /// <summary>채널 공급 함수(앱 기동 때 연결). null 이면 알 수 없음.</summary>
    public static Func<string?> ChannelProvider { get; set; } = () => null;

    /// <summary>실제 채택 렌더 모드 공급 함수(앱 기동 때 연결). null 이면 알 수 없음.</summary>
    public static Func<string?> RendererProvider { get; set; } = () => null;

    /// <summary>제품 버전(InformationalVersion, 없으면 어셈블리 버전). 빌드 메타데이터(+커밋)는 떼지 않는다.</summary>
    public static string Version => VersionLazy.Value;

    public static string? Channel => ChannelProvider();

    public static string? Renderer => RendererProvider();

    /// <summary>실행 플랫폼 이름(사전 키 접미사로도 쓴다): windows·macos·linux·browser·android·ios·other.</summary>
    public static string Platform =>
        OperatingSystem.IsBrowser() ? "browser"
        : OperatingSystem.IsAndroid() ? "android"
        : OperatingSystem.IsIOS() ? "ios"
        : OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : "other";

    public static string OsDescription => RuntimeInformation.OSDescription;

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string AvaloniaVersion => AssemblyVersion(typeof(Avalonia.Application).Assembly);

    public static string DockVersion => AssemblyVersion(typeof(Dock.Avalonia.Controls.DockControl).Assembly);

    /// <summary>빌드 시각(어셈블리 파일이 있는 플랫폼만). 웹 등 파일이 없으면 null.</summary>
    public static DateTimeOffset? BuildTime
    {
        get
        {
            try
            {
                var path = typeof(App).Assembly.Location;
                return string.IsNullOrEmpty(path) ? null : new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToLocalTime();
            }
            catch (Exception)
            {
                return null; // 권한·경로 문제는 '알 수 없음'으로 표시한다
            }
        }
    }

    /// <summary>문의에 첨부하는 앱 정보(지원 담당자용, 언어와 무관한 고정 형식). 개인정보·업무 데이터는 넣지 않는다.</summary>
    public static string AppInfoBlock() =>
        $"version={Version}\nchannel={Channel ?? "?"}\nplatform={Platform}\nos={OsDescription}\nruntime={Runtime}\navalonia={AvaloniaVersion}\ndock={DockVersion}\nrenderer={Renderer ?? "?"}";

    private static string Informational(Assembly a) =>
        a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? AssemblyVersion(a);

    private static string AssemblyVersion(Assembly a) => a.GetName().Version?.ToString() ?? "?";
}
