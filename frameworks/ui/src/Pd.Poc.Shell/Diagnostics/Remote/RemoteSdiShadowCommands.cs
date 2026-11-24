// 원격 명령(SDI 바깥 그림자 모양 리소스 PdSdiShadowActive/Inactive 조회·변경 — W1 측정 수단)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>
/// chrome.sdiShadow [active="진하기,τ,여백,아래이동"] [inactive="…"] → 현재 값. 값 형식은 ChromeResources.axaml 의 같은 키와 같다(96DPI px, 예 "0.26,15,40,3").
/// 값을 주면 앱 리소스를 바꾸고 WindowCommands.ApplyShadow 로 열린 SDI 그림자를 다시 맞춘다(저장하지 않음 — 실행 중 한정).
/// </summary>
internal static class RemoteSdiShadowCommands
{
    private static string Read(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var v) ? v?.ToString() ?? "" : "";

    /// <summary>"진하기,τ,여백,아래이동" 4개 숫자(음수 불가, 여백 1 이상)인지 본다.</summary>
    private static bool Valid(string text)
    {
        var p = text.Split(',', StringSplitOptions.TrimEntries);
        return p.Length == 4 && p.All(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0)
            && double.Parse(p[2], CultureInfo.InvariantCulture) >= 1;
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("chrome.sdiShadow", "SDI 바깥 그림자 모양 조회·변경 active=\"진하기,τ,여백,아래이동\" inactive=\"…\"(96DPI px, 생략=조회, 저장 안 함) → active·inactive 현재 값", a =>
        {
            var app = Application.Current ?? throw new RemoteCommandException("Application 이 없습니다.");
            foreach (var (arg, key) in new[] { ("active", "PdSdiShadowActive"), ("inactive", "PdSdiShadowInactive") })
            {
                if (a.Str(arg) is { } text)
                {
                    if (!Valid(text))
                    {
                        throw new RemoteCommandException($"{arg} 형식 오류 — \"진하기,τ,여백(1 이상),아래이동\" 숫자 4개: {text}");
                    }

                    app.Resources[key] = text;
                }
            }

            if (a.ContainsKey("active") || a.ContainsKey("inactive"))
            {
                WindowCommands.ApplyShadow(); // 크롬 적용 알림 → SdiShadowStack.SyncAll 이 열린 SDI 그림자를 다시 그린다
            }

            return new JsonObject { ["active"] = Read("PdSdiShadowActive"), ["inactive"] = Read("PdSdiShadowInactive") };
        });
    }
}
#endif
