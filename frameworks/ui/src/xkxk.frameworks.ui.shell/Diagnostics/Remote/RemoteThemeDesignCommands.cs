// 테마 요소별 사용자 지정(REQ-101) 원격 명령 — themedesign.elements·get·set·reset·resetall·contrast. 계산값은 셸의 실제 컨트롤에서 읽는다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 요소 디자인 원격 명령 군. 쓰기는 ThemeCatalog(SetDesign·ResetDesign·ResetAll), 값 해석은 ThemeElements 만 쓴다.
/// 대상 컨트롤은 시각 트리에서 요소별 판별 표(Matchers)로 찾는다 — 요소 추가는 표에 행만 더한다.
/// </summary>
internal static class RemoteThemeDesignCommands
{
    // 요소 → 대상 컨트롤 판별(형식 이름·클래스로 판별해 화면 층 형식을 참조하지 않는다 — 레이어 분리)
    private static readonly Dictionary<string, Func<Control, bool>> Matchers = new(StringComparer.Ordinal)
    {
        ["menuBar"] = c => c is MenuItem,
        ["docTitle"] = c => c is TextBlock && c.GetVisualAncestors().Any(a => a.GetType().Name == "MdiDocumentWindow"),
        ["windowTab"] = c => c.GetType() == typeof(Button) && c.Classes.Contains(WindowTabClass),
        ["button"] = c => c.GetType() == typeof(Button) && !ThemeElements.IsChromeButton((Button)c),
        ["input"] = c => c is TextBox,
        ["gridHeader"] = c => c.GetType().Name == "DataGridColumnHeader",
        ["gridCell"] = c => c.GetType().Name == "DataGridCell",
        ["panel"] = c => c.GetType().Name == "ToolControl",
        ["statusBar"] = c => c.GetType().Name == "ShellStatusBar",
    };

    // 창 탭 제목 단추의 스타일 클래스(MainView.axaml 탭 템플릿)
    private const string WindowTabClass = "tabTitle";

    private static ThemeCatalog Catalog => XkxkUi.Current.Themes;

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("themedesign.elements", "요소 카탈로그 → [{el, props, colors{역할:토큰}, label}]", _ => Elements());
        r.Register("themedesign.get", "요소 디자인 조회 el= → themeId·baseTemplateId·isBuiltin·stored·effective·computed(실제 컨트롤)·templateColors", a => Get(ReqEl(a)));
        r.Register("themedesign.set", "요소 속성 지정 el= prop= value=(padding 은 \"좌우,상하\") — 내장 선택 중이면 사용자 사본 자동 생성", a =>
        {
            var el = ReqEl(a);
            var prop = a.ReqStr("prop");
            var value = a.ReqStr("value");
            return Catalog.SetDesign(el, prop, value) ? JsonValue.Create(true)
                : throw new RemoteCommandException($"지정 실패(모르는 속성이거나 범위·형식 밖): {el}.{prop}={value}");
        });
        r.Register("themedesign.reset", "되돌리기 el= [prop=] — prop 생략 시 요소 전체(매핑 색 포함). 내장 선택 중이면 바꿀 것이 없어 changed=false", a =>
        {
            var el = ReqEl(a);
            var changed = Catalog.ResetDesign(el, a.Str("prop") is { Length: > 0 } p ? p : null);
            return new JsonObject { ["changed"] = changed };
        });
        r.Register("themedesign.resetall", "전체 되돌리기(Design 비움 + 색을 기준 템플릿 값으로). 내장 선택 중이면 changed=false", _ =>
            new JsonObject { ["changed"] = Catalog.ResetAll() });
        r.Register("themedesign.contrast", "요소 글자색 대 바탕색 대비 el= → {ratio, min, warn}", a => Contrast(ReqEl(a)));
    }

    private static string ReqEl(JsonObject a)
    {
        var name = a.ReqStr("el");
        return ThemeElements.All.FirstOrDefault(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new RemoteCommandException($"모르는 요소: {name} (themedesign.elements 로 목록 확인)");
    }

    private static JsonNode Elements() => new JsonArray(ThemeElements.All.Select(el => (JsonNode?)new JsonObject
    {
        ["el"] = el,
        ["label"] = ThemeElements.LabelOf(el),
        ["props"] = new JsonArray(ThemeElements.Props(el).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        ["colors"] = new JsonObject(ThemeElements.ColorRoles(el).Select(c => new KeyValuePair<string, JsonNode?>(c.Role, c.Token))),
    }).ToArray());

    private static JsonNode Get(string el)
    {
        var t = Catalog.Requested;
        var template = Catalog.BaseTemplateOf(t);
        var stored = new JsonObject();
        var effective = new JsonObject();
        foreach (var prop in ThemeElements.Props(el))
        {
            if (t.Design.TryGetValue(ThemeElements.DesignKey(el, prop), out var v))
            {
                stored[prop] = v;
            }

            effective[prop] = ThemeElements.Effective(t, el, prop);
        }

        var colors = new JsonObject();
        foreach (var token in ThemeElements.ColorTokens(el))
        {
            colors[token.StartsWith("PdBrush", StringComparison.Ordinal) ? token["PdBrush".Length..] : token] = Hex(template.GetColor(token, Colors.Transparent));
        }

        return new JsonObject
        {
            ["themeId"] = t.Id,
            ["baseTemplateId"] = t.IsBuiltin ? null : template.Id,
            ["isBuiltin"] = t.IsBuiltin,
            ["stored"] = stored,
            ["effective"] = effective,
            ["computed"] = Computed(el),
            ["templateColors"] = colors,
        };
    }

    private static JsonNode Contrast(string el)
    {
        var size = FindTarget(el) is { } target ? TextElement.GetFontSize(target) : XkxkUi.Current.Settings.ShellFontSize;
        var (ratio, min, warn) = ThemeElements.Contrast(Catalog.Requested, el, size);
        return new JsonObject { ["ratio"] = Math.Round(ratio, 2), ["min"] = min, ["warn"] = warn };
    }

    /// <summary>실제 컨트롤에서 읽은 계산값. 대상 컨트롤이 지금 화면에 없으면 요소 이름과 함께 막힘으로 보고한다(PASS 로 치지 않도록).</summary>
    private static JsonObject Computed(string el)
    {
        var target = FindTarget(el) ?? throw new RemoteNotReadyException("no-target", $"요소 {el} 의 대상 컨트롤이 화면에 없습니다(해당 화면을 먼저 여십시오).");
        var templated = target as TemplatedControl;
        var border = target as Border ?? target.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        var presenter = target.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
        var corner = templated?.CornerRadius ?? border?.CornerRadius ?? default;
        var thickness = templated?.BorderThickness ?? border?.BorderThickness ?? default;
        var padding = templated?.Padding ?? (target as Decorator)?.Padding ?? (target as TextBlock)?.Padding ?? default;
        var background = (templated?.Background) ?? (target as Panel)?.Background ?? border?.Background ?? presenter?.Background ?? (target as TextBlock)?.Background;
        var foreground = templated?.Foreground ?? (target as TextBlock)?.Foreground;
        return new JsonObject
        {
            ["FontSize"] = TextElement.GetFontSize(target),
            ["FontFamily"] = TextElement.GetFontFamily(target).Name,
            ["FontWeight"] = TextElement.GetFontWeight(target).ToString(),
            ["CornerRadius"] = corner.TopLeft,
            ["BorderThickness"] = thickness.Left,
            ["Padding"] = string.Create(CultureInfo.InvariantCulture, $"{padding.Left},{padding.Top},{padding.Right},{padding.Bottom}"),
            ["Background"] = BrushHex(background),
            ["Foreground"] = BrushHex(foreground),
        };
    }

    /// <summary>요소의 대상 컨트롤. app 은 메인 창, 나머지는 판별 표로 시각 트리에서 첫 일치(화면에 보이는 것 우선).</summary>
    private static Control? FindTarget(string el)
    {
        if (el == "app")
        {
            return VisualTreeInspector.Main() as Control;
        }

        if (!Matchers.TryGetValue(el, out var match))
        {
            return null;
        }

        var all = VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Control>().Where(match).ToList();
        return all.FirstOrDefault(c => c.IsEffectivelyVisible) ?? all.FirstOrDefault();
    }

    private static string? BrushHex(IBrush? brush) => brush is ISolidColorBrush s ? Hex(s.Color) : null;

    private static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
#endif
