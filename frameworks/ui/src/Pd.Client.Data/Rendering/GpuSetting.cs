// GPU 가속모드(GpuMode Auto|Off)를 Avalonia 초기화 전에 settings.json 에서 먼저 읽고 제품 인자 --gpu 로 덮는 공용 판정 — 호스트 3종이 플랫폼 옵션에 반영한다
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Pd.Client.Data.Rendering;

/// <summary>GPU 가속모드. 렌더링 백엔드는 기동 때 한 번만 고르므로(Avalonia 12) 바꾸면 다시 시작해야 적용된다.</summary>
public enum GpuMode
{
    /// <summary>플랫폼 기본: GPU 우선, 실패하면 소프트웨어로 대체(Windows AngleEgl→Software, 웹 WebGL2→WebGL1→Software2D, Android Egl→Software).</summary>
    Auto,

    /// <summary>소프트웨어 렌더링만(Windows Software, 웹 Software2D, Android Software).</summary>
    Off,
}

/// <summary>
/// 기동 전 GPU 가속모드 판정. 우선순위: 제품 인자 --gpu(이번 실행만, 저장 안 함) &gt; settings.json "GpuMode" &gt; 기본 Auto.
/// 진단 인자 --render(데스크톱)는 이보다 우선하며, 그 판정은 데스크톱 호스트가 한다(MarkOverridden).
/// 'GPU 만 강제(On)' 값은 두지 않는다 — GPU 초기화에 실패하면 대체 없이 예외로 앱이 뜨지 않는다(리드 결정).
/// </summary>
public static class GpuSetting
{
    /// <summary>settings.json 키(UiSettingsDto 의 같은 이름 속성, 문자열 열거값 "Auto"|"Off").</summary>
    public const string SettingsKey = "GpuMode";

    /// <summary>제품 인자 이름. 호스트는 이 인자를 셸 StartupArgs 로 넘기지 않는다(넘기면 레이아웃 자동 저장·복원이 꺼진다).</summary>
    public const string ArgName = "--gpu";

    /// <summary>이번 기동에 적용한 모드(Resolve 뒤 확정).</summary>
    public static GpuMode Applied { get; private set; } = GpuMode.Auto;

    /// <summary>적용 모드의 출처: default | settings | arg | render-arg(진단 --render 가 우선한 경우).</summary>
    public static string AppliedSource { get; private set; } = "default";

    /// <summary>설정 JSON(없으면 null)과 기동 인자로 이번 기동 모드를 정해 기록하고 돌려준다. 호스트가 Avalonia 초기화 전에 1회 부른다.</summary>
    public static GpuMode Resolve(string? settingsJson, IReadOnlyList<string>? args)
    {
        var mode = GpuMode.Auto;
        var source = "default";
        if (FromSettingsJson(settingsJson) is { } saved)
        {
            mode = saved;
            source = "settings";
        }

        if (FromArgs(args) is { } arg)
        {
            mode = arg;
            source = "arg";
        }

        Applied = mode;
        AppliedSource = source;
        return mode;
    }

    /// <summary>데스크톱 진단 --render 가 렌더링 모드를 직접 정했을 때 호스트가 부른다(GpuMode 는 무시됨을 기록).</summary>
    public static void MarkOverridden(string renderArg)
    {
        AppliedSource = "render-arg:" + renderArg;
    }

    /// <summary>settings.json 텍스트에서 GpuMode 만 읽는다(UiSettings 전체 역직렬화 전). 없거나 손상·알 수 없는 값이면 null.</summary>
    public static GpuMode? FromSettingsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(SettingsKey, out var p)
                && p.ValueKind == JsonValueKind.String
                ? Parse(p.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null; // 손상된 설정은 기본값으로 — 기동을 막지 않는다
        }
    }

    /// <summary>"--gpu off" 또는 "--gpu=off". 없거나 알 수 없는 값이면 null.</summary>
    public static GpuMode? FromArgs(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return null;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            if (a.StartsWith(ArgName + "=", StringComparison.OrdinalIgnoreCase))
            {
                return Parse(a[(ArgName.Length + 1)..]);
            }

            if (a.Equals(ArgName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return Parse(args[i + 1]);
            }
        }

        return null;
    }

    /// <summary>auto | off(대소문자 무시). 그 밖은 null.</summary>
    public static GpuMode? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "auto" => GpuMode.Auto,
        "off" => GpuMode.Off,
        _ => null,
    };

    /// <summary>인자 목록에서 --gpu 와 그 값을 뺀 사본(호스트가 셸에 넘길 인자를 만들 때).</summary>
    public static string[] StripArg(IReadOnlyList<string> args)
    {
        var list = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            if (a.StartsWith(ArgName + "=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (a.Equals(ArgName, StringComparison.OrdinalIgnoreCase))
            {
                i++; // 값도 건너뛴다
                continue;
            }

            list.Add(args[i]);
        }

        return list.ToArray();
    }
}
