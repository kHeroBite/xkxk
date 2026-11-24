// 브라우저가 도는 OS 가 Apple(macOS·iOS·iPadOS)인지 navigator 로 판별 — 웹 창 크롬을 macOS 스타일로 그릴지 정한다(team-lead 결정 ②)
using System.Runtime.InteropServices.JavaScript;

namespace Pd.Poc.Browser;

/// <summary>
/// 판별 순서: navigator.userAgentData.platform(Chromium, 예 "macOS") → navigator.platform(예 "MacIntel", "iPhone", "iPad").
/// iPadOS Safari 는 기본으로 "MacIntel" 을 보고하므로 Apple 로 잡힌다(iPad 도 macOS 스타일 — 의도와 같다).
/// 실패·undefined 면 null → ChromePlatform 이 Windows 스타일로 둔다. 시험은 headless Chromium 의 platform 오버라이드로 mac 경로를 확인한다.
/// </summary>
internal static class BrowserHostOs
{
    public static bool? IsApple()
    {
        try
        {
            using var nav = JSHost.GlobalThis.GetPropertyAsJSObject("navigator");
            if (nav is null)
            {
                return null;
            }

            using var uaData = nav.GetTypeOfProperty("userAgentData") == "object" ? nav.GetPropertyAsJSObject("userAgentData") : null;
            var platform = uaData?.GetTypeOfProperty("platform") == "string" ? uaData.GetPropertyAsString("platform") : null;
            platform ??= nav.GetTypeOfProperty("platform") == "string" ? nav.GetPropertyAsString("platform") : null;
            Console.WriteLine($"[host] os-platform={platform ?? "(unknown)"}");
            return platform is null ? null : IsApplePlatform(platform);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private static bool IsApplePlatform(string platform) =>
        platform.Contains("mac", StringComparison.OrdinalIgnoreCase)
        || platform.StartsWith("iP", StringComparison.Ordinal); // iPhone·iPad·iPod
}
