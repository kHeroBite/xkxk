// 테마 자동화 표면(UI-07 L0) — AutomationBridge·원격 제어가 호출하는 테마 명령(선택·순환·복제·색 지정·저장·삭제·파일 내보내기/가져오기·편집기 열기)과 상태 JSON
#if PD_DIAGNOSTICS // 진단 빌드 전용 — 호출부(AutomationBridge.Commands·PerfUi, RemoteCommands.Settings)가 모두 진단 전용(Q9)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 모든 메서드는 UI 스레드에서 호출한다. 실패는 예외 대신 (성공 여부, 메시지)로 돌려준다.
/// 명령 문자열 형식은 TryRun 주석 참조 — AutomationBridge 의 --cmd 값을 그대로 넘기면 된다.
/// </summary>
public static class ThemeAutomation
{
    private static ThemeCatalog Catalog => ThemeCatalog.Current;

    /// <summary>
    /// 명령 실행. 지원 형식:
    /// theme:&lt;id&gt; · theme-next · theme-clone:&lt;새이름&gt; · theme-set:&lt;토큰&gt;=&lt;#hex&gt;[,&lt;토큰&gt;=&lt;#hex&gt;…] ·
    /// theme-save · theme-delete:&lt;id&gt; · theme-export:&lt;경로&gt;[|&lt;id&gt;] · theme-import:&lt;경로&gt; · theme-open-editor ·
    /// theme-design-set:&lt;요소&gt;.&lt;속성&gt;=&lt;값&gt; · theme-design-reset[:&lt;요소&gt;[.&lt;속성&gt;]] ·
    /// theme-style:&lt;디자인테마ID&gt; · theme-style-effect:&lt;효과키&gt;=&lt;값&gt;(값 비움 = 덮어쓰기 해제) · motion-level:&lt;system|full|reduced|off&gt;.
    /// 토큰은 전체 키(PdBrushAccent) 또는 접두 생략(Accent) 모두 허용. theme- 로 시작하지 않으면(motion-level 제외) handled=false.
    /// </summary>
    public static bool TryRun(string command, out bool ok, out string message)
    {
        var (name, arg) = Split(command.Trim());
        var handled = name == "theme" || name.StartsWith("theme-", StringComparison.Ordinal) || name == "motion-level";
        (ok, message) = name switch
        {
            "theme" => Select(arg),
            "theme-next" => Next(),
            "theme-clone" => Clone(arg),
            "theme-set" => SetColors(arg),
            "theme-save" => Save(),
            "theme-delete" => Delete(arg),
            "theme-export" => Export(arg),
            "theme-import" => Import(arg),
            "theme-open-editor" => OpenEditor(),
            "theme-design-set" => SetDesign(arg),
            "theme-design-reset" => ResetDesign(arg),
            "theme-style" => SelectStyle(arg),
            "theme-style-effect" => SetStyleEffect(arg),
            "motion-level" => SetMotionLevel(arg),
            _ => (false, handled ? $"알 수 없는 테마 명령: {name}" : ""), // i18n-ok 자동화 응답(원격·시험용, 사용자 비노출)
        };
        return handled;
    }

    /// <summary>디자인 테마(내장 5종) 선택. 모르는 ID 는 거부(저장값 보존).</summary>
    public static (bool, string) SelectStyle(string id)
    {
        if (DesignThemes.Find(id) is not { } def)
        {
            return (false, $"디자인 테마 없음: {id}"); // i18n-ok 자동화 응답
        }

        UiSettings.Current.DesignThemeId = def.Id;
        return (true, $"디자인 테마 적용: {def.Id}"); // i18n-ok 자동화 응답
    }

    /// <summary>효과 1개 덮어쓰기. 형식 &lt;효과키&gt;=&lt;값&gt;. 값이 비면 해제. 허용 밖 값은 거부.</summary>
    public static (bool, string) SetStyleEffect(string arg)
    {
        var eq = arg.IndexOf('=');
        var key = (eq < 0 ? arg : arg[..eq]).Trim();
        var value = eq < 0 ? "" : arg[(eq + 1)..].Trim();
        if (DesignThemes.Spec(key) is not { IsEffect: true } spec)
        {
            return (false, $"효과 키 없음: {key}"); // i18n-ok 자동화 응답
        }

        if (value.Length == 0)
        {
            UiSettings.Current.SetEffectOverride(spec.Key, null);
            return (true, $"효과 덮어쓰기 해제: {spec.Key}"); // i18n-ok 자동화 응답
        }

        if (!DesignThemes.TryNormalize(spec.Key, value, out var normalized))
        {
            return (false, $"효과 값 거부: {spec.Key}={value}"); // i18n-ok 자동화 응답
        }

        UiSettings.Current.SetEffectOverride(spec.Key, normalized);
        return (true, $"효과 지정: {spec.Key}={normalized}"); // i18n-ok 자동화 응답
    }

    /// <summary>움직임 수준 지정(system|full|reduced|off).</summary>
    public static (bool, string) SetMotionLevel(string value)
    {
        if (!Enum.TryParse<MotionLevel>(value.Trim(), true, out var level) || !Enum.IsDefined(level))
        {
            return (false, $"움직임 수준 거부: {value}"); // i18n-ok 자동화 응답
        }

        UiSettings.Current.MotionLevel = level;
        return (true, $"움직임 수준: {level}"); // i18n-ok 자동화 응답
    }

    /// <summary>요소 디자인 지정. 형식 &lt;요소&gt;.&lt;속성&gt;=&lt;값&gt;. 내장 테마 선택 중이면 사용자 지정 사본을 자동 생성한다.</summary>
    public static (bool, string) SetDesign(string arg)
    {
        var eq = arg.IndexOf('=');
        var dot = arg.IndexOf('.');
        if (eq <= 0 || dot <= 0 || dot > eq)
        {
            return (false, "형식: theme-design-set:<요소>.<속성>=<값>"); // i18n-ok 자동화 응답
        }

        var el = arg[..dot].Trim();
        var prop = arg[(dot + 1)..eq].Trim();
        var value = arg[(eq + 1)..].Trim();
        return Catalog.SetDesign(el, prop, value)
            ? (true, $"요소 디자인 지정: {el}.{prop}={value}") // i18n-ok 자동화 응답
            : (false, $"요소 디자인 값 거부: {el}.{prop}={value}"); // i18n-ok 자동화 응답
    }

    /// <summary>요소 디자인 되돌리기. 인자 없음 = 전체, &lt;요소&gt; = 그 요소, &lt;요소&gt;.&lt;속성&gt; = 속성 1개.</summary>
    public static (bool, string) ResetDesign(string arg)
    {
        var target = arg.Trim();
        var dot = target.IndexOf('.');
        var ok = target.Length == 0 ? Catalog.ResetAll()
            : dot < 0 ? Catalog.ResetDesign(target)
            : Catalog.ResetDesign(target[..dot], target[(dot + 1)..]);
        return ok
            ? (true, $"요소 디자인 되돌림: {(target.Length == 0 ? "전체" : target)}") // i18n-ok 자동화 응답
            : (false, "되돌릴 사용자 테마가 없음(내장 테마 선택 중)"); // i18n-ok 자동화 응답
    }

    public static (bool, string) Select(string id) =>
        Catalog.Select(id) ? (true, $"테마 적용: {id}") : (false, $"테마 없음: {id}"); // i18n-ok 자동화 응답

    public static (bool, string) Next() => (true, $"테마 적용: {Catalog.SelectNext().Id}"); // i18n-ok 자동화 응답

    /// <summary>활성 테마를 복제해 새 사용자 테마로 만들고 즉시 선택. 이름이 겹치면 " 2" 등 접미.</summary>
    public static (bool, string) Clone(string newName)
    {
        var source = Catalog.Requested;
        var copy = Catalog.Duplicate(source, string.IsNullOrWhiteSpace(newName) ? null : newName.Trim());
        Catalog.Select(copy.Id);
        return (true, $"복제: {source.Id} → {copy.Id} ({copy.Name})"); // i18n-ok 자동화 응답
    }

    /// <summary>활성 사용자 테마의 토큰 색 지정(여러 개). 내장 테마가 활성이면 실패(먼저 theme-clone).</summary>
    public static (bool, string) SetColors(string spec)
    {
        if (Catalog.Requested is not { IsBuiltin: false } theme)
        {
            return (false, "활성 테마가 내장 테마라 편집 불가 — 먼저 theme-clone"); // i18n-ok 자동화 응답
        }

        var errors = new List<string>();
        var applied = 0;
        foreach (var pair in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq > 0 ? NormalizeToken(pair[..eq].Trim()) : null;
            if (key is null || !Catalog.SetColor(theme, key, pair[(eq + 1)..].Trim()))
            {
                errors.Add(pair);
                continue;
            }

            applied++;
        }

        return (errors.Count == 0 && applied > 0, $"색 지정 {applied}개" + (errors.Count > 0 ? $", 실패: {string.Join(" ", errors)}" : "")); // i18n-ok 자동화 응답
    }

    /// <summary>사용자 테마·선택 Id 즉시 저장(편집은 원래 자동 저장 — 명시 저장 시험용).</summary>
    public static (bool, string) Save()
    {
        Catalog.SaveNow();
        return (true, "저장 요청"); // i18n-ok 자동화 응답
    }

    public static (bool, string) Delete(string id) =>
        Catalog.Find(id) is { } t && Catalog.Delete(t) ? (true, $"삭제: {id}") : (false, $"삭제 불가(없음 또는 내장): {id}"); // i18n-ok 자동화 응답

    /// <summary>"경로" 또는 "경로|id". id 생략 시 활성 테마. 경로를 비우면 파일 대신 JSON 문자열을 돌려준다(브라우저는 파일 시스템 없음).</summary>
    public static (bool, string) Export(string arg)
    {
        var parts = arg.Split('|', 2);
        var theme = parts.Length > 1 ? Catalog.Find(parts[1]) : Catalog.Requested;
        if (theme is null)
        {
            return (false, "테마 없음"); // i18n-ok 자동화 응답
        }

        // 경로가 없으면(브라우저 wasm 은 파일 경로가 없다) 파일에 쓰지 않고 내보낸 JSON 문자열을 응답으로 돌려준다
        if (string.IsNullOrWhiteSpace(parts[0]))
        {
            return (true, ThemeCatalog.Export(theme));
        }

        try
        {
            File.WriteAllText(parts[0], ThemeCatalog.Export(theme), new UTF8Encoding(false));
            return (true, $"내보내기: {theme.Id} → {parts[0]}"); // i18n-ok 자동화 응답
        }
        catch (Exception ex)
        {
            return (false, $"내보내기 실패: {ex.Message}"); // i18n-ok 자동화 응답
        }
    }

    /// <summary>JSON 가져오기 → 첫 테마 즉시 선택. 인자가 JSON 문자열('{'·'['로 시작)이면 파일을 읽지 않고 그대로 쓴다(브라우저용), 아니면 파일 경로.</summary>
    public static (bool, string) Import(string path)
    {
        try
        {
            var text = path.TrimStart() is ['{', ..] or ['[', ..] ? path : File.ReadAllText(path);
            var added = Catalog.Import(text);
            if (added.Count > 0)
            {
                Catalog.Select(added[0].Id);
            }

            return (added.Count > 0, $"가져오기 {added.Count}개: {string.Join(",", added.Select(t => t.Id))}"); // i18n-ok 자동화 응답
        }
        catch (Exception ex)
        {
            return (false, $"가져오기 실패: {ex.Message}"); // i18n-ok 자동화 응답
        }
    }

    public static (bool, string) OpenEditor()
    {
        ViewRegistry.Current.OpenThemeEditor();
        return (true, "테마 편집기 열림"); // i18n-ok 자동화 응답
    }

    /// <summary>상태 JSON: 적용 완료 테마 Id·요청 테마 Id·모드·선택/시스템 짝 Id·내장 Id 목록·사용자 테마(id·name·base).</summary>
    public static string DescribeJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            var s = UiSettings.Current;
            w.WriteStartObject();
            w.WriteString("activeThemeId", ThemeApplier.Active?.Id);
            w.WriteString("requestedThemeId", Catalog.Requested.Id);
            w.WriteString("mode", s.Theme.ToString());
            w.WriteString("themeId", s.ThemeId);
            w.WriteString("systemLightThemeId", s.SystemLightThemeId);
            w.WriteString("systemDarkThemeId", s.SystemDarkThemeId);
            w.WriteStartArray("builtinThemes");
            foreach (var t in Catalog.Themes.Where(t => t.IsBuiltin))
            {
                w.WriteStringValue(t.Id);
            }

            w.WriteEndArray();
            w.WriteStartArray("userThemes");
            foreach (var t in Catalog.Themes.Where(t => !t.IsBuiltin))
            {
                w.WriteStartObject();
                w.WriteString("id", t.Id);
                w.WriteString("name", t.Name);
                w.WriteString("base", t.Base.ToString());
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>"Accent"·"pdbrushaccent" → "PdBrushAccent". 모르는 토큰이면 null.</summary>
    public static string? NormalizeToken(string token) =>
        ThemeTokens.Keys.FirstOrDefault(k => string.Equals(k, token, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(k, "PdBrush" + token, StringComparison.OrdinalIgnoreCase));

    private static (string Name, string Arg) Split(string command)
    {
        var colon = command.IndexOf(':');
        return colon < 0 ? (command.ToLowerInvariant(), "") : (command[..colon].ToLowerInvariant(), command[(colon + 1)..]);
    }
}
#endif
