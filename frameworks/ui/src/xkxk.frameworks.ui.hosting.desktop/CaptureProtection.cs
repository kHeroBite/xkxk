// 화면 캡처 방지(Win32 SetWindowDisplayAffinity) 적용·조회 — DesktopWindowHost 에서 분리한 책임 단위
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>⑬ 캡처 방지. 켜짐 여부(Enabled)는 호스트가 새 SDI 창을 등록할 때도 읽는다. 창 목록은 호출부가 인자로 넘긴다.</summary>
internal static class CaptureProtection
{
    private const uint WdaNone = 0x0, WdaMonitor = 0x1, WdaExcludeFromCapture = 0x11;

    public static bool Enabled { get; set; }

    // ⑬ WDA_EXCLUDEFROMCAPTURE(Windows 10 2004+), 실패하면 WDA_MONITOR 로 대체. 반환값·오류 코드를 로그에 남긴다.
    public static bool Apply(Window w, string tag)
    {
        if (!OperatingSystem.IsWindows())
        {
            HostLog.Write($"capture-protect {tag}: Windows 가 아니라 미지원");
            return false;
        }

        if (w.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            HostLog.Write($"capture-protect {tag}: 창 핸들 없음(아직 표시 전)");
            return false;
        }

        if (!Enabled)
        {
            var off = SetWindowDisplayAffinity(h, WdaNone);
            HostLog.Write($"capture-protect {tag} 끔 ok={off} err={(off ? 0 : Marshal.GetLastWin32Error())}");
            return off;
        }

        if (SetWindowDisplayAffinity(h, WdaExcludeFromCapture))
        {
            HostLog.Write($"capture-protect {tag} 켬 mode=EXCLUDEFROMCAPTURE");
            return true;
        }

        var err = Marshal.GetLastWin32Error();
        var fallback = SetWindowDisplayAffinity(h, WdaMonitor);
        HostLog.Write($"capture-protect {tag} EXCLUDEFROMCAPTURE 실패 err={err} → MONITOR ok={fallback} err={(fallback ? 0 : Marshal.GetLastWin32Error())}");
        return fallback;
    }

    /// <summary>메인 창의 실제 보호 여부. Windows 가 아니거나 핸들·조회 실패면 null.</summary>
    public static bool? IsProtected(Window? main)
    {
        if (!OperatingSystem.IsWindows() || main?.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            return null;
        }

        return GetWindowDisplayAffinity(h, out var affinity) ? affinity != WdaNone : null;
    }

#if PD_DIAGNOSTICS // 원격 판정 훅(DesktopDiagHooks) 전용 — 제품 빌드에는 없다
    /// <summary>⑬ 판정용: 메인 창과 열린 SDI 창마다 실제 SetWindowDisplayAffinity 값(0=없음, 1=MONITOR, 0x11=EXCLUDEFROMCAPTURE). 조회 실패 창은 null.</summary>
    public static IReadOnlyList<(string Window, uint? Affinity)> Affinities(Window? main, IEnumerable<Window> sdi)
    {
        var list = new List<(string, uint?)>();
        void Add(Window w, string name)
        {
            uint? value = null;
            if (OperatingSystem.IsWindows() && w.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero && GetWindowDisplayAffinity(h, out var a))
            {
                value = a;
            }

            list.Add((name, value));
        }

        if (main is not null)
        {
            Add(main, "main");
        }

        foreach (var w in sdi)
        {
            Add(w, $"sdi '{w.Title}'");
        }

        return list;
    }

    /// <summary>원격 판정 훅용: 모든 창이 보호 중이면 true, 하나라도 0 이면 false, 조회 불가면 null. 창별 값은 로그로 남긴다.</summary>
    public static bool? AllProtected(Window? main, IEnumerable<Window> sdi)
    {
        var all = Affinities(main, sdi);
        HostLog.Write("capture-probe " + string.Join(" ", all.Select(x => $"[{x.Window}=0x{x.Affinity:X}]")));
        if (all.Count == 0 || all.Any(x => x.Affinity is null))
        {
            return null;
        }

        return all.All(x => x.Affinity != WdaNone);
    }
#endif

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint affinity);
}
