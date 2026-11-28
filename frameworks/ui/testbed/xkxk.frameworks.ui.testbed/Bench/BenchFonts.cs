// Skia 로 직접 글자를 그리는 차트 라이브러리용 한글 SKTypeface — 세 플랫폼·세 라이브러리가 같은 임베드 글꼴 파일을 쓰게 한다(공정 비교)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia.Platform;
using SkiaSharp;

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>
/// 글꼴 키:
/// - "noto"(기본): PD 앱 임베드 Noto Sans KR 부분집합(웹·Android 호스트 자산). 데스크톱에는 이 자산이 없어 OS 한글 글꼴로 넘어간다.
///   (Shell 벤치 리소스 경로 후보는 링크가 있을 때만 쓰인다 — 웹 다운로드 중복을 피하려고 기본 빌드에서는 링크하지 않는다.)
/// - "nanum": 벤치 전용 나눔고딕(OFL, testbed/xkxk.frameworks.ui.testbed/Bench/Assets/Fonts). ScottPlot 차트 전용 글꼴(team-lead 승인, 벤치 한정) — Noto KR 계열에서 회전 축 이름·범례가 깨지는 문제(logs/odevgc/spfont 재현) 대응.
/// - "os": OS 한글 글꼴(맑은 고딕 등) — 데스크톱 참고용.
/// 찾지 못하면 다음 후보로 넘어가고, 결과 출처는 Describe() 로 보고한다.
/// </summary>
public static class BenchFonts
{
    public const string DefaultKey = "noto";

    private static readonly Dictionary<string, string[]> AssetCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noto"] = new[]
        {
            "avares://xkxk.frameworks.ui.testbed/Bench/Assets/Fonts/NotoSansKR-Regular-subset.ttf",
            "avares://xkxk.frameworks.ui.fonts/Assets/Fonts/NotoSansKR-Regular-subset.ttf", // 웹 기본 빌드(PdFontAssembly=true): 글꼴 전용 어셈블리
            "avares://xkxk.frameworks.ui.testbed.browser/Assets/Fonts/NotoSansKR-Regular-subset.ttf", // -p:PdFontAssembly=false 판
            "avares://xkxk.frameworks.ui.testbed.android/Assets/Fonts/NotoSansKR-Regular-subset.ttf",
        },
        ["nanum"] = new[] { "avares://xkxk.frameworks.ui.testbed/Bench/Assets/Fonts/NanumGothic-Regular.ttf" },
    };

    private static readonly string[] OsFamilies = { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Noto Sans CJK KR", "Apple SD Gothic Neo", "NanumGothic" };

    private static readonly Dictionary<string, (SKTypeface Tf, string Source)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>기본 키(noto) 글꼴.</summary>
    public static SKTypeface Korean => Get(DefaultKey);

    public static SKTypeface Get(string? key) => Resolve(key ?? DefaultKey).Tf;

    public static string SourceOf(string? key) => Resolve(key ?? DefaultKey).Source;

    private static (SKTypeface Tf, string Source) Resolve(string key)
    {
        if (Cache.TryGetValue(key, out var hit)) { return hit; }
        var r = LoadAsset(key) ?? LoadOs() ?? (SKTypeface.Default, "default(한글 없음)");
        Cache[key] = r;
        return r;
    }

    private static (SKTypeface, string)? LoadAsset(string key)
    {
        if (!AssetCandidates.TryGetValue(key, out var list)) { return null; }
        foreach (var a in list)
        {
            try
            {
                var uri = new Uri(a);
                if (!AssetLoader.Exists(uri)) { continue; }
                using var s = AssetLoader.Open(uri);
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var tf = SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()));
                if (tf is not null && tf.ContainsGlyph('가')) { return (tf, key + ":" + a); }
            }
            catch (Exception) { /* 다음 후보 */ }
        }

        return null;
    }

    private static (SKTypeface, string)? LoadOs()
    {
        foreach (var f in OsFamilies)
        {
            var tf = SKFontManager.Default.MatchFamily(f);
            if (tf is not null && tf.ContainsGlyph('가')) { return (tf, "os:" + f); }
        }

        var any = SKFontManager.Default.MatchCharacter('가');
        return any is null ? null : (any, "os-match:" + any.FamilyName);
    }

    public static JsonObject Describe()
    {
        var o = new JsonObject();
        foreach (var key in new[] { "noto", "nanum" })
        {
            var (tf, src) = Resolve(key);
            o[key] = new JsonObject { ["source"] = src, ["family"] = tf.FamilyName, ["has_hangul"] = tf.ContainsGlyph('한') };
        }

        return o;
    }
}
