// 실제로 채택된 렌더링 백엔드(GPU 종류·소프트웨어)를 조회해 로그·진단 기록·프로그램 정보 표시에 쓰는 공용 함수
using xkxk.frameworks.ui.data.Rendering;
using System;
using System.Reflection;

namespace xkxk.frameworks.ui.shell.Rendering;

/// <summary>
/// Avalonia 가 기동 때 고른 그래픽(IPlatformGraphics) 형식으로 실제 백엔드를 판정한다.
/// GPU 를 쓰지 않으면(소프트웨어) IPlatformGraphics 가 등록되지 않아 형식이 없다(Avalonia 12.1.4 소스 — Software 는 null 반환).
/// 로케이터는 공개 안정 API 가 아니므로 리플렉션으로만 조회한다(데스크톱 HostRenderOptions.LogActive 와 같은 방식). 실패하면 '?'.
/// Avalonia 플랫폼 초기화 뒤(주 창·주 화면이 뜬 뒤)에 불러야 맞는 값이 나온다.
/// </summary>
public static class RenderInfo
{
    private static bool s_logged;

    /// <summary>등록된 IPlatformGraphics 의 전체 형식명. 없으면 "none", 조회 실패면 "?".</summary>
    public static string GraphicsTypeName()
    {
        try
        {
            var locator = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base");
            var current = locator?.GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var type = Type.GetType("Avalonia.Platform.IPlatformGraphics, Avalonia.Base");
            var get = current?.GetType().GetMethod("GetService", [typeof(Type)]);
            if (type is null || get is null)
            {
                return "?";
            }

            return get.Invoke(current, [type]) is { } g ? g.GetType().FullName ?? g.GetType().Name : "none";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// 백엔드 종류(짧은 이름): gpu-angle-d3d11 · gpu-wgl · gpu-vulkan · gpu-egl · gpu-webgl · software · unknown.
    /// 웹 WebGL1/2 구분과 소프트웨어 판정(웹)은 형식명만으로는 확정하지 못한다(미실측) — 웹에서 그래픽이 없으면 software 로 본다.
    /// </summary>
    public static string Kind(string typeName) => typeName switch
    {
        "?" => "unknown",
        "none" => "software",
        _ when typeName.Contains("Angle", StringComparison.Ordinal) => "gpu-angle-d3d11",
        _ when typeName.Contains("Wgl", StringComparison.Ordinal) => "gpu-wgl",
        _ when typeName.Contains("Vulkan", StringComparison.Ordinal) => "gpu-vulkan",
        _ when typeName.Contains("Egl", StringComparison.Ordinal) => "gpu-egl",
        _ when typeName.Contains("Browser", StringComparison.Ordinal) => "gpu-webgl",
        _ => "unknown",
    };

    /// <summary>한 줄 요약: "kind=… graphics=… gpu_mode=Auto source=settings".</summary>
    public static string Describe()
    {
        var type = GraphicsTypeName();
        var host = HostProbe is null ? "" : $" host={SafeProbe()}";
        return $"kind={ActiveKind} graphics={type}{host} gpu_mode={GpuSetting.Applied} source={GpuSetting.AppliedSource}";
    }

    /// <summary>프로그램 정보 창 표시용(종류 + 설정). 예: "gpu-angle-d3d11 (GpuMode=Auto)".</summary>
    public static string DisplayText() => $"{ActiveKind} (GpuMode={GpuSetting.Applied})";

    private static string SafeProbe()
    {
        try
        {
            return HostProbe?.Invoke() ?? "null";
        }
        catch (Exception)
        {
            return "error";
        }
    }

    /// <summary>
    /// 호스트가 직접 판정한 백엔드(웹 전용: main.js __pdRenderer → "webgl2"|"webgl1"|"2d"|"none").
    /// Avalonia 웹은 WebGL 로 그려도 IPlatformGraphics 를 등록하지 않아 형식명만으로는 software 로 오판된다(i1023b otest-w-2 실측).
    /// 설정돼 있으면 ActiveKind·Describe 가 이 값을 우선 쓴다. 웹 호스트 Program.cs 가 JSImport 로 연결한다.
    /// </summary>
    public static Func<string?>? HostProbe { get; set; }

    /// <summary>
    /// 실제 채택 백엔드의 안정 식별자(gpu-angle-d3d11 · gpu-wgl · gpu-vulkan · gpu-egl · gpu-webgl · software · unknown).
    /// 사용자 화면은 이 값을 다국어 키 `sys.renderer.{값}` 으로 바꿔 보인다(문구는 system.*.json).
    /// </summary>
    public static string ActiveKind => HostKind() ?? Kind(GraphicsTypeName());

    /// <summary>HostProbe 결과를 종류로. 없거나 판정 불가면 null(형식명 판정으로 넘어감).</summary>
    private static string? HostKind()
    {
        string? probe;
        try
        {
            probe = HostProbe?.Invoke();
        }
        catch (Exception)
        {
            return null;
        }

        return probe switch
        {
            "webgl2" or "webgl1" => "gpu-webgl",
            "2d" => "software",
            _ => null,
        };
    }

    /// <summary>프로그램 정보 창용 실제 채택 모드 문자열(odev-1r 합의 이름, 영문 기술 표기). 조회 실패면 null.</summary>
    public static string? ActiveDescription => GraphicsTypeName() == "?" ? null : DisplayText();

    /// <summary>기동 뒤 1회 앱 로그(stdout·logcat·웹 콘솔)와 제품 로그(System 범주)에 남긴다. 두 번째 호출부터는 무시.</summary>
    public static void LogOnce()
    {
        if (s_logged)
        {
            return;
        }

        s_logged = true;
        var text = Describe();
        Console.WriteLine("[render] " + text);
        xkxk.frameworks.ui.data.Logging.PdLog.WriteSystem(xkxk.frameworks.ui.data.Logging.PdLogLevel.Information, "render " + text);
        IsResolved = true;
        Resolved?.Invoke();
    }

    /// <summary>첫 렌더 뒤 판정이 확정됐는지(LogOnce 가 불린 뒤 true). 그 전에 읽은 ActiveKind 는 unknown·software 일 수 있다.</summary>
    public static bool IsResolved { get; private set; }

    /// <summary>판정 확정 알림(첫 렌더 뒤 UI 스레드에서 1회). 메뉴·정보 창이 표시 값을 다시 읽을 때 쓴다(odev-2r 요청).</summary>
    public static event Action? Resolved;
}
