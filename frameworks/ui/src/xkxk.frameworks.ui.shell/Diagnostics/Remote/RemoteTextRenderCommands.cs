// 원격 text.*(진단) — 글자 흐림 원인 A/B(8번): 폰트·굵기·TextOptions(렌더링 모드·힌팅·기준선)를 바꿔 같은 글자를 PNG 로 그리고 실재 굵기·합성 굵게 여부를 조회
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 계획서 §6.4 가설 H1~H6 판별용이다. 제품 동작에는 들어가지 않는다.
/// text.render 는 화면과 떨어진 비트맵(RenderTargetBitmap)에 시험 문자열 1줄을 그린다(폰트·굵기·TextOptions 를 인자로).
/// text.options 는 실행 중 모든 최상위 창 루트의 TextOptions 를 바꾼다(화면 합성 경로 A/B, 재기동 전까지 유지).
/// text.query 는 굵기별로 실제 고른 서체·합성 굵게(FontSimulations.Bold) 여부와 창별 TextOptions·투명도를 낸다.
/// </summary>
internal static class RemoteTextRenderCommands
{
    private static readonly int[] QueryWeights = [400, 500, 600, 700];

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("text.query", "글꼴 굵기별 실재 서체·합성 굵게 조회 [font=이름(생략 시 현재 PdFontFamily)] → {font, weights:[{req, simulations, faceWeight, face}], tops:[{index, textOptions, transparency, scaling}]}",
            a => Query(a.Str("font")));
        r.Register("text.render", "시험 문자열을 비트맵에 그려 PNG 저장 [font] [weight=Normal|SemiBold|Bold|숫자] [size=14] [text] [mode=unspecified|subpixel|antialias|alias] [hinting=unspecified|none|light|strong] [baseline=unspecified|aligned|unaligned] [real=true(합성 대신 실재 굵기)] [scale] [path] [inline=true] → {path, w, h, typeface, options}",
            Render);
        r.Register("text.options", "모든 최상위 창 루트 TextOptions 변경(화면 A/B, 재기동 전까지) [mode] [hinting] [baseline] — 생략한 값은 unspecified → tops", a =>
        {
            var opt = Options(a);
            foreach (var t in VisualTreeInspector.TopLevels())
            {
                TextOptions.SetTextOptions(t, opt);
            }

            return Tops();
        });
    }

    private static FontFamily Family(string? name) =>
        name is { Length: > 0 } ? UiSettings.FontResolver(name)
        : Application.Current?.Resources.TryGetResource("PdFontFamily", null, out var v) == true && v is FontFamily f ? f : FontFamily.Default;

    private static FontWeight Weight(string? s) =>
        s is null ? FontWeight.Normal
        : int.TryParse(s, out var n) ? (FontWeight)n
        : Enum.TryParse<FontWeight>(s, true, out var w) ? w
        : throw new RemoteCommandException("weight 는 Normal|Medium|SemiBold|Bold 또는 숫자입니다.");

    private static TextOptions Options(JsonObject a) => new()
    {
        TextRenderingMode = (a.Str("mode") ?? "unspecified").ToLowerInvariant() switch
        {
            "unspecified" => TextRenderingMode.Unspecified,
            "subpixel" or "subpixelantialias" => TextRenderingMode.SubpixelAntialias,
            "antialias" => TextRenderingMode.Antialias,
            "alias" => TextRenderingMode.Alias,
            _ => throw new RemoteCommandException("mode=unspecified|subpixel|antialias|alias"),
        },
        TextHintingMode = (a.Str("hinting") ?? "unspecified").ToLowerInvariant() switch
        {
            "unspecified" => TextHintingMode.Unspecified,
            "none" => TextHintingMode.None,
            "light" => TextHintingMode.Light,
            "strong" => TextHintingMode.Strong,
            _ => throw new RemoteCommandException("hinting=unspecified|none|light|strong"),
        },
        BaselinePixelAlignment = (a.Str("baseline") ?? "unspecified").ToLowerInvariant() switch
        {
            "unspecified" => BaselinePixelAlignment.Unspecified,
            "aligned" => BaselinePixelAlignment.Aligned,
            "unaligned" => BaselinePixelAlignment.Unaligned,
            _ => throw new RemoteCommandException("baseline=unspecified|aligned|unaligned"),
        },
    };

    private static JsonObject OptionsJson(TextOptions o) => new()
    {
        ["mode"] = o.TextRenderingMode.ToString(), ["hinting"] = o.TextHintingMode.ToString(), ["baseline"] = o.BaselinePixelAlignment.ToString(),
    };

    /// <summary>요청 굵기에서 실제로 고른 서체. 합성 굵게면 서체의 실재 굵기를 함께 낸다(H1 판별).</summary>
    private static JsonObject Face(FontFamily family, FontWeight weight)
    {
        var o = new JsonObject { ["req"] = (int)weight };
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var gt))
        {
            o["face"] = null;
            return o;
        }

        o["face"] = gt.FamilyName;
        o["faceWeight"] = (int)gt.Weight;
        o["simulations"] = gt.FontSimulations.ToString();
        return o;
    }

    private static JsonNode Query(string? name)
    {
        var family = Family(name);
        var weights = new JsonArray(QueryWeights.Select(w => (JsonNode?)Face(family, (FontWeight)w)).ToArray());
        return new JsonObject
        {
            ["font"] = family.Name, ["weights"] = weights, ["tops"] = Tops(),
            ["strong"] = Application.Current?.Resources.TryGetResource("PdFontWeightStrong", null, out var s) == true ? s?.ToString() : null,
            ["bold"] = Application.Current?.Resources.TryGetResource("PdFontWeightBold", null, out var b) == true ? b?.ToString() : null,
        };
    }

    private static JsonArray Tops()
    {
        var arr = new JsonArray();
        var tops = VisualTreeInspector.TopLevels();
        for (var i = 0; i < tops.Count; i++)
        {
            var t = tops[i];
            arr.Add(new JsonObject
            {
                ["index"] = i, ["type"] = t.GetType().Name, ["textOptions"] = OptionsJson(TextOptions.GetTextOptions(t)),
                ["transparency"] = t.ActualTransparencyLevel.ToString(), ["scaling"] = t.RenderScaling,
            });
        }

        return arr;
    }

    private static JsonNode Render(JsonObject a)
    {
        var family = Family(a.Str("font"));
        var weight = Weight(a.Str("weight"));
        var used = weight;
        if (a.Bool("real"))
        {
            // 제품과 같은 규칙(UiSettingsApplier.RealWeight). 합성 굵게는 600 이상에서만 생기므로 그 아래는 요청 그대로
            used = weight < FontWeight.SemiBold ? weight
                : weight >= FontWeight.Bold ? UiSettingsApplier.RealWeight(family, weight, FontWeight.SemiBold)
                : UiSettingsApplier.RealWeight(family, weight, FontWeight.Bold);
        }

        var opt = Options(a);
        var scale = a.Num("scale") ?? VisualTreeInspector.Main().RenderScaling;
        var text = new TextBlock
        {
            Text = a.Str("text") ?? "새 문서(MDI) New document",
            FontFamily = family, FontWeight = used, FontSize = a.Num("size") ?? 14,
            Foreground = Brushes.Black, Margin = new Thickness(6, 4), VerticalAlignment = VerticalAlignment.Top,
        };
        TextOptions.SetTextOptions(text, opt);
        var host = new Border { Background = Brushes.White, Child = text };
        host.Measure(Size.Infinity);
        var size = host.DesiredSize;
        host.Arrange(new Rect(size));
        var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
        using var rtb = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        rtb.Render(host);
        using var ms = new MemoryStream();
        rtb.Save(ms, PngBitmapEncoderOptions.Default);
        var png = ms.ToArray();

        var result = new JsonObject
        {
            ["w"] = px.Width, ["h"] = px.Height, ["scale"] = scale, ["usedWeight"] = (int)used,
            ["typeface"] = Face(family, used), ["requested"] = Face(family, weight), ["options"] = OptionsJson(opt),
        };
        if (a.Str("path") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, png);
            result["path"] = Path.GetFullPath(path);
        }

        if (a.Bool("inline") || a.Str("path") is not { Length: > 0 })
        {
            result["base64"] = Convert.ToBase64String(png);
        }

        return result;
    }
}
#endif
