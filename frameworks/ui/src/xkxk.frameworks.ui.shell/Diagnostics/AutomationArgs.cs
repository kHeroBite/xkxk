// 실행 인자를 AutomationOptions 로 해석하고, 시나리오·이번 실행만 적용하는 임시 설정·그 밖(계측)으로 분류하는 판정 함수
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드는 AutomationBridge.Product.cs 가 항상 false·빈 집합을 돌려준다
using System;
using System.Collections.Generic;
using System.Globalization;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>자동화 실행 인자 해석·분류. 공개 진입점은 AutomationBridge 의 Parse·HasScenarioArgs·IsTransientSettingArg·TransientSettingArgs 다.</summary>
internal static class AutomationArgs
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "save-layout", "load-layout", "dedupe" };

    /// <summary>레이아웃·문서 상태를 만들거나 바꾸는 시나리오 인자. 있으면 셸이 레이아웃 자동 저장·기동 복원을 끈다.</summary>
    private static readonly HashSet<string> ScenarioKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "cmd", "layout", "sdi", "move-all", "load-layout", "save-layout", "exit-after",
        "notify-panel", // 알림 패널 없는 레이아웃 트리로 기동 — 그대로 자동 저장되면 사용자 레이아웃에서 패널이 빠진다
    };

    /// <summary>화면 설정을 바꾸지만 이번 실행에만 적용하는 인자. 셸은 이 항목을 settings.json 에 저장하지 않는다.</summary>
    private static readonly HashSet<string> TransientSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "theme", "font", "font-size", "zoom", "shadow", "frame", "chrome-buttons",
    };

    internal static AutomationOptions Parse(IReadOnlyList<string> args)
    {
        var o = new AutomationOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) { continue; }
            var body = a[2..];
            var eq = body.IndexOf('=');
            if (eq >= 0) { Apply(o, body[..eq], body[(eq + 1)..]); }
            else if (!Flags.Contains(body) && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) { Apply(o, body, args[++i]); }
            else { Apply(o, body, null); }
        }

        return o;
    }

    private static void Apply(AutomationOptions o, string key, string? value)
    {
        switch (key.ToLowerInvariant())
        {
            case "open": ParseOpen(o, value); break;
            case "layout": o.Layout = value?.ToLowerInvariant(); break;
            case "sdi": o.Sdi = (int)Num(value, 1); break;
            case "move-all": o.MoveAll = Pair(value); break;
            case "save-layout": o.SaveLayout = IsTrue(value); break;
            case "load-layout": o.LoadLayout = IsTrue(value); break;
            case "screenshot": o.Screenshot = value; break;
            case "perf-out": o.PerfOut = value; break;
            case "exit-after": o.ExitAfter = Num(value, 0); break;
            case "bg-pos": o.BgPos = Pair(value) is { } p ? ((int)p.Item1, (int)p.Item2) : null; break;
            case "dedupe": o.Dedupe = IsTrue(value); break;
            case "sample-load-delay-ms": xkxk.frameworks.ui.data.Samples.SampleLoadDelay.DelayMs = o.SampleLoadDelayMs = (int)Math.Max(0, Num(value, 0)); break; // 인자가 없으면 지연·점유 0
            case "sample-ui-block-ms": xkxk.frameworks.ui.data.Samples.SampleLoadDelay.BlockMs = o.SampleUiBlockMs = (int)Math.Max(0, Num(value, 0)); break;
            case "font-size": o.FontSize = Num(value, 0) is > 0 and var fs ? fs : null; break;
            case "font": o.Font = value; break;
            case "theme": o.Theme = value; break;
            case "frame-pump": o.FramePump = !(value is "off" or "0" or "false"); break;
            case "shadow": o.Shadow = !(value is "off" or "0" or "false"); break;
            case "profile": PerfUiRunner.Short = value?.ToLowerInvariant() == "short"; break; // perf-ui 짧은 모드(웹·모바일)
            case "reps": PerfUiRunner.Reps = (int)Math.Clamp(Num(value, 0), 0, 1000); break; // perf-ui 반복 수(0=기본)
            case "frame": o.Frame = value?.ToLowerInvariant() is "mars" or "pd" ? value.ToLowerInvariant() : null; break;
            case "zoom": o.Zoom = Num(value, 0) is > 0 and var z ? (z <= 5 ? z * 100 : z) : null; break; // 1.5 와 150 모두 허용
            case "cmd": SplitCmds(o.Cmds, value); break;
            case "chrome-buttons": o.ChromeButtons = value?.ToLowerInvariant(); break;
            case "chrome-inactive-dim": o.ChromeInactiveDim = value?.ToLowerInvariant(); break;
            case "chrome-inactive-opacity": o.ChromeInactiveOpacity = value; break;
            case "fluent-override": o.FluentOverride = !(value is "off" or "0" or "false"); break;
            case "fluent-palette": o.FluentPalette = value?.Trim().ToLowerInvariant() ?? "on"; break;
            case "theme-new-tokens": o.ThemeNewTokens = !(value is "off" or "0" or "false"); break;
            case "notify-panel" or "grid-open" or "grid-seed" or "layout-load" or "loc" or "perf-split" or "perf-split-n": break; // 다른 모듈이 App.StartupArgs 에서 직접 읽는다(odev-1·GridDocumentView.Perf·odev-6r) — 미해석 인자로 남기지 않기만
            default: o.Unknown.Add(key); break;
        }
    }

    /// <summary>
    /// 쉼표로 나누되 대소문자는 보존한다(theme-export 경로·theme-clone 이름). "토큰=#hex" 처럼 명령 이름 없이 '=' 만 있는 조각은
    /// 앞 명령(theme-set:A=#..,B=#..)의 연속으로 붙인다.
    /// </summary>
    private static void SplitCmds(List<string> into, string? value)
    {
        foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (into.Count > 0 && part.Contains('=') && !part.Contains(':') && into[^1].StartsWith("theme-set:", StringComparison.OrdinalIgnoreCase)) { into[^1] += "," + part; }
            else { into.Add(part); }
        }
    }

    private static void ParseOpen(AutomationOptions o, string? value)
    {
        foreach (var item in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split(':');
            if (Enum.TryParse<DocumentKind>(parts[0], true, out var kind)) { o.Open.Add((kind, parts.Length > 1 ? Math.Max(0, (int)Num(parts[1], 1)) : 1)); }
            else { o.Unknown.Add("open:" + item); }
        }
    }

    private static double Num(string? s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static (double, double)? Pair(string? s)
    {
        var p = (s ?? "").Split(',');
        return p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? (a, b) : null;
    }

    private static bool IsTrue(string? v) => v is null || v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>시나리오 인자가 하나라도 있으면 true.</summary>
    internal static bool HasScenarioArgs(IReadOnlyList<string> args)
    {
        foreach (var key in ArgKeys(args))
        {
            if (ScenarioKeys.Contains(key)) { return true; }
        }

        return false;
    }

    /// <summary>"theme"·"--theme"·"--theme=dark" 어느 모양이든 임시 설정 인자면 true.</summary>
    internal static bool IsTransientSettingArg(string name) => TransientSettingKeys.Contains(KeyOf(name));

    /// <summary>실제로 넘어온 임시 설정 인자 이름(하이픈 접두 없는 소문자, 예: theme·zoom).</summary>
    internal static IReadOnlySet<string> TransientSettingArgs(IReadOnlyList<string> args)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ArgKeys(args))
        {
            if (TransientSettingKeys.Contains(key)) { found.Add(key.ToLowerInvariant()); }
        }

        return found;
    }

    /// <summary>"--" 로 시작하는 토큰의 키만 뽑는다(Parse 와 같은 규칙 — 값 토큰은 "--" 로 시작하지 않으므로 건너뛴다).</summary>
    private static IEnumerable<string> ArgKeys(IReadOnlyList<string>? args)
    {
        if (args is null) { yield break; }
        foreach (var a in args)
        {
            if (a is not null && a.StartsWith("--", StringComparison.Ordinal)) { yield return KeyOf(a); }
        }
    }

    private static string KeyOf(string? token)
    {
        var body = (token ?? "").Trim();
        if (body.StartsWith("--", StringComparison.Ordinal)) { body = body[2..]; }
        var eq = body.IndexOf('=');
        return (eq >= 0 ? body[..eq] : body).Trim();
    }
}
#endif
