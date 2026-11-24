// 데스크톱 호스트 렌더링 옵션(GPU 가속모드·--render·--composition) 적용과 실제 활성 렌더 경로 진단 로그
using Pd.Client.Data.Rendering;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Rendering;

namespace Pd.Poc.Desktop;

/// <summary>
/// 진단·A/B 측정용 호스트 인자.
/// --render angle,wgl,vulkan,software  → Win32PlatformOptions.RenderingMode(앞쪽 우선, 미지정 시 Avalonia 기본 AngleEgl→Software)
/// --composition winui,dcomp,lowlatency,redirect → Win32PlatformOptions.CompositionMode(미지정 시 기본 WinUI→DComp→Redirection)
/// --render-dirty-regions on|off (진단 빌드만) → 영역 dirty 추적(시작 옵션 RenderDirtyRegions, 설정값보다 우선, 이번 실행만)
/// 유휴 정지 아티팩트 제거(연속 렌더)는 Shell PerfProbe 의 --frame-pump 가 담당한다.
/// </summary>
public static class HostRenderOptions
{
    /// <summary>
    /// 렌더링 옵션 적용. 우선순위: 진단 --render(GpuSetting.MarkOverridden 로 기록) &gt; GPU 가속모드(UI-17, --gpu &gt; settings.json).
    /// GpuMode.Off → RenderingMode=[Software], Auto → 옵션을 주지 않는다(Avalonia 기본 AngleEgl→Software, 지금 동작 그대로).
    /// </summary>
    /// <param name="overlayPopups">
    /// 비전경(--bg-pos) 시험 기동 때만 true: 메뉴·콤보·플라이아웃·툴팁·컨텍스트 메뉴 팝업을 별도 OS 창이 아닌 소유 창의 오버레이 레이어에 그린다.
    /// 별도 팝업 TopLevel 은 OS·Avalonia 가 화면 경계 안으로 끌어와 화면 밖 시험 중 사용자 화면에 노출됐다(lead 10:4x, (2357,32) 6회).
    /// 근거: Avalonia.Win32 12.1.4 문서 Win32PlatformOptions.OverlayPopups "Embeds popups to the window when set to true. The default value is false."
    /// 제품 기동(--bg-pos 없음)은 false 라 지금 동작 그대로다.
    /// </param>
    /// <param name="settingsJson">settings.json 원문(없으면 null). 시작 옵션 RenderDirtyRegions 를 읽는다 — 호출부가 넘기지 않으면 설정값은 쓰지 않고 기본(끔)·진단 인자만 따른다.</param>
    public static AppBuilder Apply(AppBuilder builder, string? render, string? composition, GpuMode gpu = GpuMode.Auto, bool overlayPopups = false, string? settingsJson = null)
    {
#if PD_DIAGNOSTICS
        ApplyTimerResolution();
#endif
        builder = ApplyDirtyRegions(builder, settingsJson);
        var softwareOnly = string.IsNullOrWhiteSpace(render) && gpu == GpuMode.Off;
        if (!string.IsNullOrWhiteSpace(render))
        {
            GpuSetting.MarkOverridden(render);
        }

        if (string.IsNullOrWhiteSpace(render) && string.IsNullOrWhiteSpace(composition) && !softwareOnly && !overlayPopups)
        {
            return builder;
        }

        var options = new Win32PlatformOptions { OverlayPopups = overlayPopups };
        if (!string.IsNullOrWhiteSpace(render))
        {
            options.RenderingMode = Split(render).Select(ParseRender).ToArray();
        }
        else if (softwareOnly)
        {
            options.RenderingMode = [Win32RenderingMode.Software];
        }

        if (!string.IsNullOrWhiteSpace(composition))
        {
            options.CompositionMode = Split(composition).Select(ParseComposition).ToArray();
        }

        return builder.With(options);
    }

    /// <summary>주 창이 열린 뒤 호출 — 활성 그래픽·렌더 루프 형식을 로그와 perf JSON(text)에 남긴다.</summary>
    public static void LogActive(Window main, Action<string> log)
    {
        var graphics = TypeName(Locate("Avalonia.Platform.IPlatformGraphics, Avalonia.Base"));
        var loop = Locate("Avalonia.Rendering.IRenderLoop, Avalonia.Base");
        var timer = loop is null ? null : FieldsOfInterest(loop);
        log($"render graphics={graphics} loop={TypeName(loop)} timer={timer ?? "?"} scaling={main.DesktopScaling}");
        PerfProbe.Current.SetText("render_graphics", graphics);
        PerfProbe.Current.SetText("render_timer", timer ?? "?");
        PerfProbe.Current.SetMetric("host_main_opened_ms", PerfProbe.Current.NowMs);
    }

    /// <summary>
    /// 영역 dirty 추적(시작 옵션, 01 §4.2 RenderDirtyRegions). 우선순위: 진단 인자 --render-dirty-regions on|off(진단 빌드만) &gt; settings.json &gt; 기본(끔).
    /// 켜면 AppBuilder.With(CompositionOptions{UseRegionDirtyRectClipping=true}) — Avalonia 12.1.4 Compositor 가 AvaloniaLocator 에서 읽는다(앱 시작 전에만 정할 수 있음).
    /// 적용값은 MdiMoveLight.RenderDirtyRegions(원격 chrome.moveLight 조회)와 perf JSON 텍스트 render_dirty_regions 로 남긴다.
    /// </summary>
    private static AppBuilder ApplyDirtyRegions(AppBuilder builder, string? settingsJson)
    {
        var on = Pd.Poc.Shell.Docking.MdiMoveLight.RenderDirtyRegionsFromSettingsJson(settingsJson) ?? Pd.Poc.Shell.Docking.MdiMoveLight.DefaultRenderDirtyRegions;
#if PD_DIAGNOSTICS
        if (ArgValue("--render-dirty-regions")?.ToLowerInvariant() is { } v && v is "on" or "off")
        {
            on = v == "on";
        }
#endif
        Pd.Poc.Shell.Docking.MdiMoveLight.RenderDirtyRegions = on;
        PerfProbe.Current.SetText("render_dirty_regions", on ? "on" : "off");
        return on ? builder.With(new Avalonia.Rendering.Composition.CompositionOptions { UseRegionDirtyRectClipping = true }) : builder;
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static Win32RenderingMode ParseRender(string v) => v.ToLowerInvariant() switch
    {
        "angle" or "angleegl" => Win32RenderingMode.AngleEgl,
        "wgl" => Win32RenderingMode.Wgl,
        "vulkan" => Win32RenderingMode.Vulkan,
        "software" => Win32RenderingMode.Software,
        _ => throw new ArgumentException($"--render 값 오류: '{v}' (angle|wgl|vulkan|software)"),
    };

    private static Win32CompositionMode ParseComposition(string v) => v.ToLowerInvariant() switch
    {
        "winui" => Win32CompositionMode.WinUIComposition,
        "dcomp" => Win32CompositionMode.DirectComposition,
        "lowlatency" => Win32CompositionMode.LowLatencyDxgiSwapChain,
        "redirect" => Win32CompositionMode.RedirectionSurface,
        _ => throw new ArgumentException($"--composition 값 오류: '{v}' (winui|dcomp|lowlatency|redirect)"),
    };

    // AvaloniaLocator 는 공개 안정 API 가 아니므로 리플렉션으로만 조회한다(실패 시 null).
    private static object? Locate(string serviceType)
    {
        try
        {
            var locator = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base");
            var current = locator?.GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var type = Type.GetType(serviceType);
            var get = current?.GetType().GetMethod("GetService", [typeof(Type)]);
            return type is null || get is null ? null : get.Invoke(current, [type]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 렌더 루프 내부의 타이머 필드 형식(예: WinUiCompositorConnection) — 컴포지션 모드 판별용
    private static string? FieldsOfInterest(object loop)
    {
        var names = loop.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.GetValue(loop))
            .Where(v => v is not null && v.GetType().Name.Contains("Timer", StringComparison.OrdinalIgnoreCase)
                        || v?.GetType().Name.Contains("Connection", StringComparison.OrdinalIgnoreCase) == true)
            .Select(v => v!.GetType().FullName ?? v.GetType().Name)
            .ToArray();
        return names.Length == 0 ? null : string.Join('+', names);
    }

    private static string TypeName(object? o) => o?.GetType().FullName ?? "?";

#if PD_DIAGNOSTICS
    /// <summary>
    /// 진단 A/B 전용(기본 동작 불변): --timer-res N(또는 --timer-res=N, 환경변수 PD_TIMER_RES=N, N=1~15 ms)이 있으면 기동 시 winmm timeBeginPeriod(N),
    /// 종료 시 timeEndPeriod(N). 근거: b2_split4 프레임 간격이 31.25ms(15.625×2, Windows 기본 타이머 분해능 2틱)에 몰리고 합성 방식(winui·dcomp·lowlatency)과 무관했다 —
    /// 타이머 분해능이 원인인지 가르는 대조 실험이다. 인자는 호스트 전용 목록에 없어 셸 AutomationArgs.Unknown 으로 흘러가지만 쓰이지 않는다(무해).
    /// 적용 값은 perf JSON 텍스트 timer_res(미지정 "default")와 호스트 로그로 남긴다.
    /// </summary>
    private static void ApplyTimerResolution()
    {
        var raw = ArgValue("--timer-res") ?? Environment.GetEnvironmentVariable("PD_TIMER_RES");
        if (!OperatingSystem.IsWindows() || !uint.TryParse(raw, out var ms) || ms is < 1 or > 15)
        {
            PerfProbe.Current.SetText("timer_res", "default");
            return;
        }

        var rc = timeBeginPeriod(ms); // 0 = TIMERR_NOERROR
        PerfProbe.Current.SetText("timer_res", rc == 0 ? ms + "ms" : $"fail rc={rc}");
        Console.WriteLine($"[host] timer_res {ms}ms rc={rc}");
        if (rc == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => timeEndPeriod(ms);
        }
    }

    // 진단 인자 값("--이름=값" 또는 "--이름 값"). 호스트 전용 목록에 없어 셸 AutomationArgs.Unknown 으로 흘러가지만 쓰이지 않는다(무해)
    private static string? ArgValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) { return args[i][(name.Length + 1)..]; }
            if (args[i] == name && i + 1 < args.Length) { return args[i + 1]; }
        }

        return null;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);
#endif
}
